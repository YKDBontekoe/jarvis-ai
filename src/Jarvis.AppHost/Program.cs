var builder = DistributedApplication.CreateBuilder(args);
var workspaceRoot = builder.Configuration["Coding:Repositories:0:Path"] ?? FindGitRepositoryRoot(Directory.GetCurrentDirectory());

var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg18")
    .WithDataVolume();
var database = postgres.AddDatabase("jarvis");
var voiceWorkerSecret = builder.Configuration["VOICE_WORKER_SECRET"] ?? "jarvis-local-voice-worker-development-secret";
var mcpRunnerToken = builder.Configuration["MCP_RUNNER_TOKEN"] ?? "jarvis-local-mcp-runner-development-token";
var livekitApiKey = builder.Configuration["LIVEKIT_API_KEY"] ?? "devkey";
var livekitApiSecret = builder.Configuration["LIVEKIT_API_SECRET"] ?? "jarvis-local-livekit-development-secret";

var temporal = builder.AddContainer("temporal", "temporalio/temporal", "latest")
    .WithArgs("server", "start-dev", "--ip", "0.0.0.0", "--db-filename", "/home/temporal/temporal.db")
    .WithEndpoint(targetPort: 7233, port: 7233, name: "grpc")
    .WithEndpoint(targetPort: 8233, port: 8233, name: "ui")
    .WithVolume("jarvis-temporal-data", "/home/temporal");

const string objectStorageAccessKey = "jarvis-local-access";
const string objectStorageSecretKey = "jarvis-local-secret";
const string objectStorageBucket = "jarvis-files";
var objectStorage = builder.AddContainer("object-storage", "chrislusf/seaweedfs", "4.47")
    .WithEnvironment("AWS_ACCESS_KEY_ID", objectStorageAccessKey)
    .WithEnvironment("AWS_SECRET_ACCESS_KEY", objectStorageSecretKey)
    .WithEnvironment("S3_BUCKET", objectStorageBucket)
    .WithVolume("jarvis-object-storage-data", "/data")
    .WithEndpoint(targetPort: 8333, port: 8333, name: "s3");

var livekit = builder.AddContainer("livekit", "livekit/livekit-server", "latest")
    .WithEnvironment("LIVEKIT_KEYS", $"{livekitApiKey}: {livekitApiSecret}")
    .WithArgs("--config", "/etc/livekit.yaml", "--dev", "--bind", "0.0.0.0")
    .WithBindMount(Path.Combine(workspaceRoot, "infra/livekit/livekit.yaml"), "/etc/livekit.yaml", isReadOnly: true)
    .WithEndpoint(targetPort: 7880, port: 7880, name: "ws")
    .WithEndpoint(targetPort: 7881, port: 7881, name: "rtc-tcp");
for (var port = 50000; port <= 50100; port++)
    livekit.WithEndpoint(targetPort: port, port: port, name: $"rtc-udp-{port}",
        protocol: System.Net.Sockets.ProtocolType.Udp);

var clamav = builder.AddContainer("clamav", "clamav/clamav", "stable")
    .WithContainerRuntimeArgs("--platform=linux/amd64")
    .WithVolume("jarvis-clamav-signatures", "/var/lib/clamav")
    .WithEndpoint(targetPort: 3310, port: 3310, name: "clamd");

var signalCliUrl = builder.Configuration["Channels:Signal:BaseUrl"]
                   ?? builder.Configuration["SIGNAL_CLI_REST_URL"];
IResourceBuilder<ContainerResource>? signalCli = null;
if (string.IsNullOrWhiteSpace(signalCliUrl))
{
    signalCli = builder.AddContainer("signal-cli", "bbernhard/signal-cli-rest-api", "latest")
        .WithEnvironment("MODE", "json-rpc")
        .WithVolume("jarvis-signal-cli-data", "/home/.local/share/signal-cli")
        .WithHttpEndpoint(port: 8080, targetPort: 8080);
}

var whatsAppBridgeUrl = builder.Configuration["Channels:WhatsAppBridge:BaseUrl"]
                        ?? builder.Configuration["WHATSAPP_BRIDGE_URL"];
IResourceBuilder<ContainerResource>? whatsAppBridge = null;
if (string.IsNullOrWhiteSpace(whatsAppBridgeUrl))
{
    whatsAppBridge = builder.AddDockerfile("whatsapp-bridge", Path.Combine(workspaceRoot, "workers", "whatsapp-bridge"))
        .WithVolume("jarvis-whatsapp-bridge-data", "/data")
        .WithHttpEndpoint(port: 3000, targetPort: 3000);
}

// Owner-installed npm and PyPI connectors run here, outside the API and worker, as in production.
var mcpRunner = builder.AddProject<Projects.Jarvis_Api>("mcp-runner", launchProfileName: null)
    .WithArgs("mcp-runner")
    .WithEnvironment("McpRunner__Token", mcpRunnerToken)
    .WithEnvironment("McpRunner__ListenUrl", "http://localhost:8090")
    .WithEnvironment("McpRunner__PassEnvironment", "HTTPS_PROXY,HTTP_PROXY,NO_PROXY,NODE_EXTRA_CA_CERTS,SSL_CERT_FILE");
const string mcpRunnerUrl = "ws://localhost:8090/run";

var api = builder.AddProject<Projects.Jarvis_Api>("jarvis-api")
    .WithReference(database)
    .WithEnvironment("Coding__Repositories__0__Name", "jarvis")
    .WithEnvironment("Coding__Repositories__0__Path", workspaceRoot)
    .WithEnvironment("Temporal__Address", temporal.GetEndpoint("grpc"))
    .WithEnvironment("ObjectStorage__ServiceUrl", objectStorage.GetEndpoint("s3"))
    .WithEnvironment("ObjectStorage__AccessKey", objectStorageAccessKey)
    .WithEnvironment("ObjectStorage__SecretKey", objectStorageSecretKey)
    .WithEnvironment("ObjectStorage__Bucket", objectStorageBucket)
    .WithEnvironment("ObjectStorage__Region", "us-east-1")
    .WithEnvironment("ObjectStorage__MaxUploadBytes", "20971520")
    .WithEnvironment("Antivirus__Host", "localhost")
    .WithEnvironment("Antivirus__Port", "3310")
    .WithEnvironment("Antivirus__TimeoutSeconds", "60")
    .WithEnvironment("LiveKit__ApiKey", livekitApiKey)
    .WithEnvironment("LiveKit__ApiSecret", livekitApiSecret)
    .WithEnvironment("LiveKit__InternalUrl", "http://localhost:7880")
    .WithEnvironment("LiveKit__PublicUrl", builder.Configuration["LIVEKIT_PUBLIC_URL"] ?? "ws://localhost:7880")
    .WithEnvironment("Voice__WorkerSecret", voiceWorkerSecret)
    .WithEnvironment("McpRunner__Url", mcpRunnerUrl)
    .WithEnvironment("McpRunner__Token", mcpRunnerToken)
    .WithEnvironment("ASPNETCORE_URLS", builder.Configuration["JARVIS_LISTEN_URL"] ?? "http://localhost:5082")
    .WaitFor(database)
    .WaitFor(temporal)
    .WaitFor(objectStorage)
    .WaitFor(clamav)
    .WaitFor(livekit)
    .WaitFor(mcpRunner);
if (signalCli is not null)
    api.WithEnvironment("Channels__Signal__BaseUrl", signalCli.GetEndpoint("http")).WaitFor(signalCli);
else
    api.WithEnvironment("Channels__Signal__BaseUrl", signalCliUrl!);
if (whatsAppBridge is not null)
    api.WithEnvironment("Channels__WhatsAppBridge__BaseUrl", whatsAppBridge.GetEndpoint("http")).WaitFor(whatsAppBridge);
else
    api.WithEnvironment("Channels__WhatsAppBridge__BaseUrl", whatsAppBridgeUrl!);
if (!string.IsNullOrWhiteSpace(builder.Configuration["Jarvis:ModelClass"]))
    api.WithEnvironment("Jarvis__ModelClass", builder.Configuration["Jarvis:ModelClass"]!);
foreach (var modelClass in builder.Configuration.GetSection("Codex:ModelClasses").GetChildren())
    if (!string.IsNullOrWhiteSpace(modelClass.Value))
        api.WithEnvironment($"Codex__ModelClasses__{modelClass.Key}", modelClass.Value);

var worker = builder.AddProject<Projects.Jarvis_Worker>("jarvis-worker")
    .WithReference(database)
    .WithEnvironment("Coding__Repositories__0__Name", "jarvis")
    .WithEnvironment("Coding__Repositories__0__Path", workspaceRoot)
    .WithEnvironment("Temporal__Address", temporal.GetEndpoint("grpc"))
    .WithEnvironment("Codex__ExecutablePath", "codex")
    .WithEnvironment("McpRunner__Url", mcpRunnerUrl)
    .WithEnvironment("McpRunner__Token", mcpRunnerToken)
    .WithEnvironment("ObjectStorage__ServiceUrl", objectStorage.GetEndpoint("s3"))
    .WithEnvironment("ObjectStorage__AccessKey", objectStorageAccessKey)
    .WithEnvironment("ObjectStorage__SecretKey", objectStorageSecretKey)
    .WithEnvironment("ObjectStorage__Bucket", objectStorageBucket)
    .WithEnvironment("ObjectStorage__Region", "us-east-1")
    .WaitFor(database)
    .WaitFor(temporal)
    .WaitFor(objectStorage);
if (whatsAppBridge is not null)
    worker.WithEnvironment("Channels__WhatsAppBridge__BaseUrl", whatsAppBridge.GetEndpoint("http"));
else
    worker.WithEnvironment("Channels__WhatsAppBridge__BaseUrl", whatsAppBridgeUrl!);
if (!string.IsNullOrWhiteSpace(builder.Configuration["Jarvis:ModelClass"]))
    worker.WithEnvironment("Jarvis__ModelClass", builder.Configuration["Jarvis:ModelClass"]!);
foreach (var modelClass in builder.Configuration.GetSection("Codex:ModelClasses").GetChildren())
    if (!string.IsNullOrWhiteSpace(modelClass.Value))
        worker.WithEnvironment($"Codex__ModelClasses__{modelClass.Key}", modelClass.Value);

builder.Build().Run();

static string FindGitRepositoryRoot(string startPath)
{
    var directory = new DirectoryInfo(Path.GetFullPath(startPath));
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new InvalidOperationException("Could not find a Git checkout for the Jarvis coding tool. Configure Coding:Repositories:0:Path.");
}
