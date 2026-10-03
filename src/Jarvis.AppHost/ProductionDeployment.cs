using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes.Swarm;

/// <summary>
/// The production stack, published as a Docker Compose file by <c>scripts/deploy/publish-compose.sh</c>.
/// Every value an operator sets comes from the env file as <c>${NAME}</c>; required ones fail fast with
/// <c>${NAME:?message}</c>. Paths are relative to the repository root, which deploys pass as the Compose project
/// directory. Service, network and volume names match the previous hand-written file so existing data volumes
/// (<c>jarvis_postgres-data</c>, ...) are reused.
/// </summary>
internal static class ProductionDeployment
{
    private const string User = "${JARVIS_UID:?Set JARVIS_UID}:${JARVIS_GID:?Set JARVIS_GID}";
    private const string PostgresPassword = "${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD}";
    private const string TemporalPassword = "${TEMPORAL_PASSWORD:?Set TEMPORAL_PASSWORD}";
    private const string ApiImage = "${JARVIS_API_IMAGE:-ghcr.io/ykdbontekoe/jarvis-ai/api:latest}";
    private const string WorkerImage = "${JARVIS_WORKER_IMAGE:-ghcr.io/ykdbontekoe/jarvis-ai/worker:latest}";
    private const string ConnectionString =
        "Host=postgres;Port=5432;Database=jarvis;Username=jarvis;Password=" + PostgresPassword;
    private const string KeysDirectory = "${JARVIS_DATA_PROTECTION_KEYS_DIR:?Set JARVIS_DATA_PROTECTION_KEYS_DIR}";
    private const string McpRunnerToken = "${MCP_RUNNER_TOKEN:?Set MCP_RUNNER_TOKEN}";

    /// <summary>Compose settings Aspire's model has no property for; added to the file after publishing.</summary>
    public static readonly IReadOnlyDictionary<string, (int? PidsLimit, string? Platform)> ExtraSettings =
        new Dictionary<string, (int?, string?)>
        {
            ["clamav"] = (null, "linux/amd64"),
            ["jarvis-worker"] = (256, null),
            ["mcp-runner"] = (512, null),
            ["playwright-mcp"] = (256, null),
            ["browser-egress-proxy"] = (128, null),
        };

    public static void AddProductionDeployment(this IDistributedApplicationBuilder builder, JarvisFeatures features)
    {
        var volumes = new List<string>
        {
            "postgres-data", "temporal-postgres-data", "garage-meta", "garage-data", "clamav-signatures",
            "caddy-data", "caddy-config", "signal-cli-data", "whatsapp-bridge-data", "embeddings-data",
        };
        if (features.Coding) volumes.Add("coding-worktrees");

        builder.AddDockerComposeEnvironment("jarvis")
            .WithDashboard(false)
            .ConfigureComposeFile(file =>
            {
                file.Name = "jarvis";
                file.Networks.Clear();
                file.AddNetwork(new Network { Name = "edge" });
                file.AddNetwork(new Network { Name = "application" });
                file.AddNetwork(new Network { Name = "data", Internal = true });
                file.AddNetwork(new Network { Name = "temporal-data", Internal = true });
                // API and worker reach the MCP runner here; the runner reaches the internet only via mcp-egress.
                file.AddNetwork(new Network { Name = "mcp", Internal = true });
                file.AddNetwork(new Network { Name = "mcp-egress" });
                if (features.Browser)
                {
                    file.AddNetwork(new Network { Name = "agents", Driver = "bridge", Internal = true });
                    file.AddNetwork(new Network { Name = "browser-egress", Driver = "bridge" });
                }
                foreach (var volume in volumes) file.AddVolume(new Volume { Name = volume });
            });

        builder.Service("postgres", $"{JarvisImages.Postgres}:{JarvisImages.PostgresTag}", s =>
        {
            s.Restart = "unless-stopped";
            s.Env(("POSTGRES_DB", "jarvis"), ("POSTGRES_USER", "jarvis"), ("POSTGRES_PASSWORD", PostgresPassword));
            s.Volumes.Add(Named("postgres-data", "/var/lib/postgresql"));
            s.Volumes.Add(Bind("./infra/postgres/init", "/docker-entrypoint-initdb.d", readOnly: true));
            s.Healthcheck = Check(["CMD-SHELL", "pg_isready -U jarvis -d jarvis"], "5s", "3s", 20);
            s.Networks = ["data"];
        });

        builder.Service("temporal-postgres", "postgres:18", s =>
        {
            s.Restart = "unless-stopped";
            s.Env(("POSTGRES_DB", "temporal"), ("POSTGRES_USER", "temporal"), ("POSTGRES_PASSWORD", TemporalPassword));
            s.Volumes.Add(Named("temporal-postgres-data", "/var/lib/postgresql"));
            s.Volumes.Add(Bind("./infra/temporal/init", "/docker-entrypoint-initdb.d", readOnly: true));
            s.Healthcheck = Check(["CMD-SHELL", "pg_isready -U temporal -d temporal"], "5s", "3s", 20);
            s.Networks = ["temporal-data"];
        });

        // Nightly pg_dump of both databases plus the data protection key ring (docs/operations/backup-and-restore.md).
        builder.Service("backup", $"{JarvisImages.Postgres}:{JarvisImages.PostgresTag}", s =>
        {
            s.Restart = "unless-stopped";
            s.User = User;
            s.ReadOnly = true;
            s.Entrypoint = ["/usr/local/bin/jarvis-backup"];
            s.Env(("BACKUP_HOUR_UTC", "${BACKUP_HOUR_UTC:-3}"),
                ("BACKUP_RETENTION_DAYS", "${BACKUP_RETENTION_DAYS:-14}"),
                ("BACKUP_PASSPHRASE", "${BACKUP_PASSPHRASE:-}"),
                ("JARVIS_DB_PASSWORD", PostgresPassword),
                ("TEMPORAL_DB_PASSWORD", TemporalPassword));
            s.Volumes.Add(Bind("./infra/backup/backup.sh", "/usr/local/bin/jarvis-backup", readOnly: true));
            s.Volumes.Add(Bind("${JARVIS_BACKUP_DIR:?Set JARVIS_BACKUP_DIR}", "/backups"));
            s.Volumes.Add(Bind(KeysDirectory, "/data-protection-keys", readOnly: true));
            s.After("postgres", Healthy).After("temporal-postgres", Healthy);
            s.Networks = ["data", "temporal-data"];
        });

        builder.Service("temporal", "temporalio/auto-setup:latest", s =>
        {
            s.Restart = "unless-stopped";
            s.Env(("DB", "postgres12"), ("DB_PORT", "5432"), ("POSTGRES_USER", "temporal"),
                ("POSTGRES_PWD", TemporalPassword), ("POSTGRES_SEEDS", "temporal-postgres"), ("DBNAME", "temporal"),
                ("VISIBILITY_DBNAME", "temporal_visibility"));
            s.After("temporal-postgres", Healthy);
            s.Networks = ["application", "temporal-data"];
        });

        builder.Service("temporal-ui", "temporalio/ui:latest", s =>
        {
            s.Restart = "unless-stopped";
            s.Env(("TEMPORAL_ADDRESS", "temporal:7233"));
            s.After("temporal", Started);
            s.Networks = ["application"];
        });

        builder.Service("garage", "dxflrs/garage:v2.4.1", s =>
        {
            s.Command = ["/garage", "server", "--single-node", "--default-bucket"];
            s.Restart = "unless-stopped";
            s.Env(("GARAGE_CONFIG_FILE", "/etc/garage.toml"),
                ("GARAGE_DEFAULT_ACCESS_KEY", "${S3_ACCESS_KEY:?Set S3_ACCESS_KEY}"),
                ("GARAGE_DEFAULT_SECRET_KEY", "${S3_SECRET_KEY:?Set S3_SECRET_KEY}"),
                ("GARAGE_DEFAULT_BUCKET", "jarvis-files"));
            s.Volumes.Add(Named("garage-meta", "/var/lib/garage/meta"));
            s.Volumes.Add(Named("garage-data", "/var/lib/garage/data"));
            s.Volumes.Add(Bind("${GARAGE_CONFIG_FILE:?Set GARAGE_CONFIG_FILE}", "/etc/garage.toml", readOnly: true));
            s.Healthcheck = Check(["CMD", "/garage", "status"], "15s", "10s", 10, "45s");
            s.Networks = ["data"];
        });

        builder.Service("clamav", "clamav/clamav:stable", s =>
        {
            s.Restart = "unless-stopped";
            s.Limit("2g", "2.0");
            s.Volumes.Add(Named("clamav-signatures", "/var/lib/clamav"));
            s.Healthcheck = Check(["CMD-SHELL", "clamdscan --ping=3 >/dev/null 2>&1"], "15s", "10s", 20, "5m");
            s.Networks = ["application"];
        });

        builder.Service("livekit", "livekit/livekit-server:latest", s =>
        {
            s.Command = ["--config", "/etc/livekit.yaml", "--bind", "0.0.0.0"];
            s.Restart = "unless-stopped";
            s.Env(("LIVEKIT_KEYS", "${LIVEKIT_API_KEY:?Set LIVEKIT_API_KEY}: ${LIVEKIT_API_SECRET:?Set LIVEKIT_API_SECRET}"));
            // Behind Caddy all media uses port 443: UDP straight to LiveKit, ICE-TCP through Caddy's TCP 443. With the
            // tunnel feature another proxy owns 443, so media keeps its own ports. Loopback 7880 serves a host proxy.
            s.Volumes.Add(Bind(features.Tunnel ? "./infra/livekit/production-tunnel.yaml" : "./infra/livekit/production.yaml",
                "/etc/livekit.yaml", readOnly: true));
            s.Ports = features.Tunnel
                ? ["127.0.0.1:7880:7880/tcp", "${LIVEKIT_BIND_ADDRESS:-0.0.0.0}:7881:7881/tcp",
                    "${LIVEKIT_BIND_ADDRESS:-0.0.0.0}:50000-50100:50000-50100/udp"]
                : ["127.0.0.1:7880:7880/tcp", "${LIVEKIT_BIND_ADDRESS:-0.0.0.0}:443:443/udp"];
            s.Networks = ["application"];
        });

        builder.Service("signal-cli", "bbernhard/signal-cli-rest-api:latest", s =>
        {
            s.Restart = "unless-stopped";
            s.Env(("MODE", "json-rpc"));
            s.Volumes.Add(Named("signal-cli-data", "/home/.local/share/signal-cli"));
            s.Limit("2g", "1.0");
            s.Networks = ["application"];
        });

        // Links WhatsApp as a companion device. Built from source on the host on every `up`; only the API and
        // worker reach it.
        builder.Service("whatsapp-bridge", null, s =>
        {
            s.Build = new Build { Context = "./workers/whatsapp-bridge" };
            s.PullPolicy = "build";
            s.Restart = "unless-stopped";
            s.Env(("BRIDGE_TOKEN", "${WHATSAPP_BRIDGE_TOKEN:-}"),
                ("SENTRY_DSN", "${SENTRY_DSN:-" + JarvisImages.DefaultSentryDsn + "}"),
                ("SENTRY_ENVIRONMENT", "${SENTRY_ENVIRONMENT:-production}"),
                ("SENTRY_RELEASE", "${SENTRY_RELEASE:-}"));
            s.Volumes.Add(Named("whatsapp-bridge-data", "/data"));
            s.Limit("512m", "0.5");
            s.Networks = ["application"];
        });

        // Local CPU embedding model for semantic memory search. Set EMBEDDINGS_BASE_URL= (empty) to switch it off.
        builder.Service("embeddings", $"${{EMBEDDINGS_IMAGE:-{JarvisImages.Embeddings}:{JarvisImages.EmbeddingsTag}}}", s =>
        {
            s.Restart = "unless-stopped";
            s.Command = ["--model-id", $"${{EMBEDDINGS_MODEL:-{JarvisImages.EmbeddingsModel}}}", "--port", "80",
                "--auto-truncate"];
            s.Env(("HF_HOME", "/data"));
            s.Volumes.Add(Named("embeddings-data", "/data"));
            s.Limit("2g", "2.0");
            s.Networks = ["application"];
        });

        builder.Service("jarvis-api", ApiImage, s =>
        {
            s.Build = DotnetImage("src/Jarvis.Api/Jarvis.Api.csproj");
            s.User = User;
            s.ReadOnly = true;
            s.Tmpfs = ["/tmp:rw,nosuid,size=1g", "/dev/shm:rw,nosuid,size=256m"];
            s.Restart = "unless-stopped";
            s.Env(("ASPNETCORE_ENVIRONMENT", "Production"), ("ASPNETCORE_URLS", "http://+:5082"),
                ("HOME", "/tmp"), ("ReverseProxy__TrustForwardedHeaders", "true"));
            s.Env(SharedAppEnvironment());
            s.Env(("Database__ApplyMigrationsAtStartup", "false"),
                ("ObjectStorage__MaxUploadBytes", "${MAX_UPLOAD_BYTES:-20971520}"),
                ("Antivirus__Host", "clamav"), ("Antivirus__Port", "3310"), ("Antivirus__TimeoutSeconds", "60"),
                ("Authentication__Issuer", "${AUTH_ISSUER:?Set AUTH_ISSUER}"),
                ("Authentication__Audience", "${AUTH_AUDIENCE:?Set AUTH_AUDIENCE}"),
                ("Authentication__SigningKey", "${AUTH_SIGNING_KEY:?Set AUTH_SIGNING_KEY}"),
                ("Authentication__AllowRegistration", "${AUTH_ALLOW_REGISTRATION:-true}"),
                ("Cors__AllowedOrigins__0", "${JARVIS_WEB_ORIGIN:?Set JARVIS_WEB_ORIGIN}"),
                ("LiveKit__ApiKey", "${LIVEKIT_API_KEY:?Set LIVEKIT_API_KEY}"),
                ("LiveKit__ApiSecret", "${LIVEKIT_API_SECRET:?Set LIVEKIT_API_SECRET}"),
                ("LiveKit__InternalUrl", "http://livekit:7880"),
                ("LiveKit__PublicUrl", "wss://${LIVEKIT_DOMAIN:?Set LIVEKIT_DOMAIN}"),
                ("Voice__WorkerSecret", "${VOICE_WORKER_SECRET:?Set VOICE_WORKER_SECRET}"),
                ("Jarvis__PublicBaseUrl", "${JARVIS_WEB_ORIGIN:?Set JARVIS_WEB_ORIGIN}"),
                ("Channels__PublicBaseUrl", "${JARVIS_WEB_ORIGIN:?Set JARVIS_WEB_ORIGIN}"),
                ("Channels__Signal__BaseUrl", "${SIGNAL_CLI_REST_URL:-http://signal-cli:8080}"),
                ("Push__FirebaseProjectId", "${FIREBASE_PROJECT_ID:-}"),
                ("Push__GoogleServiceAccountFile", "/run/secrets/firebase-service-account.json"));
            s.Volumes.Add(Bind("${CODEX_HOME_DIR:?Set CODEX_HOME_DIR}", "/codex"));
            s.Volumes.Add(Bind("${FIREBASE_SERVICE_ACCOUNT_FILE:-/dev/null}", "/run/secrets/firebase-service-account.json",
                readOnly: true));
            s.Volumes.Add(Bind(KeysDirectory, "/var/lib/jarvis-data-protection-keys"));
            s.After("postgres", Healthy).After("temporal", Started).After("garage", Healthy)
                .After("clamav", Healthy).After("livekit", Started).After("signal-cli", Started)
                .After("whatsapp-bridge", Started);
            s.Healthcheck = Check(["CMD", "node", "-e",
                "fetch('http://127.0.0.1:5082/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"],
                "15s", "5s", 10, "90s");
            s.Networks = ["application", "data", "mcp"];
            if (features.Tunnel) s.Ports = ["127.0.0.1:15082:5082"];
            if (features.GitHub) s.Env(JarvisMcpServers.GitHub());
            if (features.HomeAssistant)
                s.Env(JarvisMcpServers.HomeAssistant(
                    "${HOME_ASSISTANT_MCP_URL:?Set HOME_ASSISTANT_MCP_URL to an HTTPS Home Assistant /api/mcp URL}"));
            if (features.Coding) s.AddCodingCheckout();
            if (features.Browser)
            {
                s.Env(JarvisMcpServers.Browser("http://playwright-mcp:8931/mcp"));
                s.After("playwright-mcp", Healthy).After("browser-egress-proxy", Started);
                s.Networks.Add("agents");
            }
        });

        // One-shot deployment task, profile-gated so a normal `up` never reruns migrations; remote-up invokes it
        // explicitly before replacing the API.
        builder.Service("jarvis-migrate", ApiImage, s =>
        {
            s.Profiles = ["migration"];
            s.Command = ["Jarvis.Api.dll", "migrate"];
            s.Restart = "no";
            s.ReadOnly = true;
            s.User = User;
            s.Env(("DOTNET_ENVIRONMENT", "Production"), ("HOME", "/tmp"), ("ConnectionStrings__jarvis", ConnectionString));
            s.Env(SentryEnvironment());
            s.Tmpfs = ["/tmp:rw,nosuid,size=64m"];
            s.After("postgres", Healthy);
            s.Networks = ["data"];
        });

        builder.Service("jarvis-worker", WorkerImage, s =>
        {
            s.Build = DotnetImage("workers/Jarvis.Worker/Jarvis.Worker.csproj");
            s.Command = ["Jarvis.Worker.dll"];
            s.User = User;
            s.ReadOnly = true;
            s.Tmpfs = ["/tmp:rw,nosuid,size=512m"];
            s.Restart = "unless-stopped";
            s.Limit("2g", "2.0");
            s.Env(("DOTNET_ENVIRONMENT", "Production"), ("HOME", "/tmp"));
            s.Env(SharedAppEnvironment());
            s.Volumes.Add(Bind("${CODEX_HOME_DIR:?Set CODEX_HOME_DIR}", "/codex"));
            s.Volumes.Add(Bind(KeysDirectory, "/var/lib/jarvis-data-protection-keys"));
            s.After("jarvis-api", Healthy).After("garage", Healthy).After("temporal", Started);
            s.Networks = ["application", "data", "mcp"];
            if (features.Coding) s.AddCodingCheckout();
        });

        // Runs owner-installed npm and PyPI connectors away from the API and worker: no database networks, no
        // Docker socket, a read-only root, and a fresh temporary home per connection.
        builder.Service("mcp-runner", ApiImage, s =>
        {
            s.Command = ["Jarvis.Api.dll", "mcp-runner"];
            s.User = User;
            s.ReadOnly = true;
            s.Tmpfs = ["/tmp:rw,nosuid,size=1g", "/cache:rw,nosuid,size=2g"];
            s.CapDrop = ["ALL"];
            s.SecurityOpt = ["no-new-privileges:true"];
            s.Limit("2g", null);
            s.Restart = "unless-stopped";
            s.Env(("HOME", "/tmp"), ("McpRunner__Token", McpRunnerToken), ("McpRunner__ListenUrl", "http://+:8090"),
                ("McpRunner__WorkRoot", "/tmp/mcp-runner"), ("McpRunner__CacheRoot", "/cache"),
                ("McpRunner__MaxProcesses", "${MCP_RUNNER_MAX_PROCESSES:-16}"));
            s.Healthcheck = Check(["CMD", "node", "-e",
                "fetch('http://127.0.0.1:8090/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"],
                "15s", "5s", 5);
            s.Networks = ["mcp", "mcp-egress"];
        });

        // Built on the host from infra/caddy/Dockerfile (Caddy with the layer4 plugin), like the WhatsApp bridge.
        builder.Service("caddy", null, s =>
        {
            s.Build = new Build { Context = "./infra/caddy" };
            s.PullPolicy = "build";
            s.Restart = "unless-stopped";
            s.Env(("JARVIS_DOMAIN", "${JARVIS_DOMAIN:?Set JARVIS_DOMAIN}"),
                ("LIVEKIT_DOMAIN", "${LIVEKIT_DOMAIN:?Set LIVEKIT_DOMAIN}"));
            // Behind an existing host proxy or Cloudflare Tunnel, Caddy only starts with the direct-edge profile.
            if (features.Tunnel) s.Profiles = ["direct-edge"];
            // No UDP 443: LiveKit media owns it, so Caddy serves HTTP/1.1 and HTTP/2 only.
            else s.Ports = ["80:80/tcp", "443:443/tcp"];
            s.Volumes.Add(Bind("./infra/caddy/Caddyfile", "/etc/caddy/Caddyfile", readOnly: true));
            s.Volumes.Add(Named("caddy-data", "/data"));
            s.Volumes.Add(Named("caddy-config", "/config"));
            s.After("jarvis-api", Healthy).After("livekit", Started);
            s.Networks = ["edge", "application"];
        });

        if (features.Browser) builder.AddBrowser();
    }

    private static void AddBrowser(this IDistributedApplicationBuilder builder)
    {
        builder.Service("playwright-mcp", $"{JarvisImages.Playwright}:latest", s =>
        {
            s.Restart = "unless-stopped";
            s.Init = true;
            s.Entrypoint = ["node", "/app/cli.js", "--headless", "--browser", "chromium", "--no-sandbox"];
            s.Command = ["--port", "8931", "--host", "0.0.0.0", "--allowed-hosts", "playwright-mcp:8931",
                "--block-service-workers", "--proxy-server", "http://browser-egress-proxy:3128"];
            s.ReadOnly = true;
            s.Tmpfs = ["/tmp:size=512m,mode=1777", "/home/node:size=512m,mode=1777"];
            s.ShmSize = "1gb";
            s.Limit("2g", "2.0");
            s.CapDrop = ["ALL"];
            s.SecurityOpt = ["no-new-privileges:true"];
            s.Healthcheck = Check(["CMD", "node", "-e",
                "const s=require('net').connect(8931,'127.0.0.1');s.on('connect',()=>{s.end();process.exit(0)});s.on('error',()=>process.exit(1))"],
                "5s", "3s", 20, "30s");
            s.Networks = ["agents"];
        });

        builder.Service("browser-egress-proxy", $"{JarvisImages.Squid}:latest", s =>
        {
            s.Restart = "unless-stopped";
            s.ReadOnly = true;
            s.Volumes.Add(Bind("./infra/compose/browser/squid.conf", "/etc/squid/squid.conf", readOnly: true));
            s.Tmpfs = ["/run/squid:size=16m,mode=0755,uid=13,gid=13", "/var/spool/squid:size=64m,mode=0755,uid=13,gid=13"];
            s.Limit("512m", "1.0");
            s.SecurityOpt = ["no-new-privileges:true"];
            s.Command = ["-N", "-f", "/etc/squid/squid.conf"];
            s.Networks = ["agents", "browser-egress"];
        });
    }

    /// <summary>Settings the API and worker share.</summary>
    private static IEnumerable<(string, string)> SharedAppEnvironment()
    {
        yield return ("McpRunner__Url", "ws://mcp-runner:8090/run");
        yield return ("McpRunner__Token", McpRunnerToken);
        yield return ("DataProtection__KeysDirectory", "/var/lib/jarvis-data-protection-keys");
        yield return ("CODEX_HOME", "/codex");
        yield return ("Codex__ExecutablePath", "/usr/local/bin/codex");
        foreach (var modelClass in new[] { "Fast", "Standard", "Reasoning", "Coding", "Vision", "Realtime" })
            yield return ($"Codex__ModelClasses__{modelClass}", $"${{CODEX_MODEL_{modelClass.ToUpperInvariant()}:-}}");
        yield return ("Jarvis__ModelClass", "${JARVIS_MODEL_CLASS:-}");
        yield return ("Embeddings__BaseUrl", "${EMBEDDINGS_BASE_URL-http://embeddings:80/v1}");
        yield return ("Embeddings__Model", $"${{EMBEDDINGS_MODEL:-{JarvisImages.EmbeddingsModel}}}");
        yield return ("ConnectionStrings__jarvis", ConnectionString);
        yield return ("Temporal__Address", "temporal:7233");
        yield return ("Channels__WhatsAppBridge__BaseUrl", "${WHATSAPP_BRIDGE_URL:-http://whatsapp-bridge:3000}");
        yield return ("Channels__WhatsAppBridge__Token", "${WHATSAPP_BRIDGE_TOKEN:-}");
        yield return ("ObjectStorage__ServiceUrl", "http://garage:3900");
        yield return ("ObjectStorage__AccessKey", "${S3_ACCESS_KEY:?Set S3_ACCESS_KEY}");
        yield return ("ObjectStorage__SecretKey", "${S3_SECRET_KEY:?Set S3_SECRET_KEY}");
        yield return ("ObjectStorage__Bucket", "jarvis-files");
        yield return ("ObjectStorage__Region", "us-east-1");
        yield return ("OTEL_EXPORTER_OTLP_ENDPOINT", "${OTEL_EXPORTER_OTLP_ENDPOINT:-}");
        foreach (var item in SentryEnvironment()) yield return item;
        yield return ("Sentry__TracesSampleRate", "${SENTRY_TRACES_SAMPLE_RATE:-0.2}");
        yield return ("Sentry__ProfilesSampleRate", "${SENTRY_PROFILES_SAMPLE_RATE:-0}");
        yield return ("Sentry__RecordAiContent", "${SENTRY_RECORD_AI_CONTENT:-false}");
    }

    private static IEnumerable<(string, string)> SentryEnvironment() =>
    [
        ("SENTRY_DSN", "${SENTRY_DSN:-" + JarvisImages.DefaultSentryDsn + "}"),
        ("SENTRY_ENVIRONMENT", "${SENTRY_ENVIRONMENT:-production}"),
        ("SENTRY_RELEASE", "${SENTRY_RELEASE:-}"),
    ];

    private static void AddCodingCheckout(this Service s)
    {
        s.Env(("Coding__Repositories__0__Name", "${CODING_REPO_NAME:-jarvis}"),
            ("Coding__Repositories__0__Path", "/coding/repo"), ("Coding__WorktreeRoot", "/coding/worktrees"));
        s.Volumes.Add(Bind("${CODING_REPO_PATH:?Set CODING_REPO_PATH to a Git checkout}", "/coding/repo"));
        s.Volumes.Add(Named("coding-worktrees", "/coding/worktrees"));
    }

    private const string Healthy = "service_healthy";
    private const string Started = "service_started";

    private static void Service(this IDistributedApplicationBuilder builder, string name, string? image,
        Action<Service> configure) =>
        builder.AddContainer(name, "jarvis-placeholder")
            .PublishAsDockerComposeService((_, service) =>
            {
                service.Image = image!;
                service.Environment.Clear();
                service.Volumes.Clear();
                service.Networks = [];
                configure(service);
            });

    private static void Env(this Service s, params (string Name, string Value)[] values) =>
        s.Env((IEnumerable<(string, string)>)values);

    private static void Env(this Service s, IEnumerable<(string Name, string Value)> values)
    {
        foreach (var (name, value) in values) s.Environment[name] = value;
    }

    private static void Env(this Service s, IEnumerable<KeyValuePair<string, string>> values)
    {
        foreach (var (name, value) in values) s.Environment[name] = value;
    }

    private static Service After(this Service s, string service, string condition)
    {
        s.DependsOn[service] = new ServiceDependency { Condition = condition };
        return s;
    }

    private static void Limit(this Service s, string? memory, string? cpus) =>
        s.Deploy = new Deploy { Resources = new Resources { Limits = new ResourceSpec { Memory = memory, Cpus = cpus } } };

    private static Build DotnetImage(string projectPath) => new()
    {
        Context = ".",
        Dockerfile = "infra/compose/Dockerfile",
        Args = new Dictionary<string, string> { ["PROJECT_PATH"] = projectPath },
    };

    private static Volume Named(string name, string target) =>
        new() { Name = name, Type = "volume", Source = name, Target = target };

    private static Volume Bind(string source, string target, bool readOnly = false) =>
        new() { Name = target, Type = "bind", Source = source, Target = target, ReadOnly = readOnly ? true : null };

    private static Healthcheck Check(List<string> test, string interval, string timeout, int retries,
        string? startPeriod = null) =>
        new() { Test = test, Interval = interval, Timeout = timeout, Retries = retries, StartPeriod = startPeriod! };
}
