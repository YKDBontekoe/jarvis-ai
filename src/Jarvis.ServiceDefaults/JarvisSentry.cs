using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using Sentry;
using Sentry.Extensions.Logging;
using Sentry.OpenTelemetry;
using Sentry.Profiling;

namespace Jarvis.ServiceDefaults;

/// <summary>
/// Turns Sentry on when a DSN is configured and keeps prompts, credentials, and client errors out of it.
/// </summary>
public static class JarvisSentry
{
    public const double DefaultProductionTracesSampleRate = 0.2;

    /// <summary>
    /// Optional gate set by the API so a handled 4xx does not become an issue. Unset hosts report every exception.
    /// </summary>
    public static Func<Exception, bool>? ShouldCaptureException { get; set; }

    public static bool IsEnabled(string? dsn) => !string.IsNullOrWhiteSpace(dsn);

    public static string? ResolveDsn(IConfiguration configuration) =>
        FirstNonEmpty(configuration["Sentry:Dsn"], Environment.GetEnvironmentVariable("SENTRY_DSN"));

    public static string? ResolveRelease(IConfiguration configuration) =>
        FirstNonEmpty(configuration["Sentry:Release"], Environment.GetEnvironmentVariable("SENTRY_RELEASE"));

    public static string ResolveEnvironment(IConfiguration configuration, string hostingEnvironment) =>
        FirstNonEmpty(configuration["Sentry:Environment"], Environment.GetEnvironmentVariable("SENTRY_ENVIRONMENT"))
        ?? hostingEnvironment;

    public static double ResolveTracesSampleRate(IConfiguration configuration, bool isDevelopment)
    {
        var configured = configuration.GetValue<double?>("Sentry:TracesSampleRate");
        if (configured is { } rate)
            return Math.Clamp(rate, 0, 1);
        return isDevelopment ? 1.0 : DefaultProductionTracesSampleRate;
    }

    public static double ResolveProfilesSampleRate(IConfiguration configuration)
    {
        var configured = configuration.GetValue<double?>("Sentry:ProfilesSampleRate") ?? 0;
        return Math.Clamp(configured, 0, 1);
    }

    public static bool RecordAiContent(IConfiguration configuration) =>
        configuration.GetValue("Sentry:RecordAiContent", false);

    /// <summary>Health checks stay out of Sentry traces. The sampler returns 0 for those names.</summary>
    public static bool IsHealthTransaction(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var path = name.Trim();
        var space = path.LastIndexOf(' ');
        if (space >= 0 && space < path.Length - 1)
            path = path[(space + 1)..];
        var query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0) path = path[..query];
        return path is "/health" or "/alive";
    }

    public static double? SampleTransaction(string? transactionName) =>
        IsHealthTransaction(transactionName) ? 0 : null;

    public static bool ShouldCaptureHttpStatus(int statusCode) => statusCode >= 500;

    /// <summary>
    /// Jarvis activities go to Sentry. OpenTelemetry chat spans from the model clients do not:
    /// <c>AddSentry</c> already records those calls. The Codex completion span is the exception.
    /// </summary>
    public static bool ShouldExportActivity(string? sourceName, string? displayName)
    {
        if (string.IsNullOrEmpty(sourceName) || !sourceName.StartsWith("Jarvis.", StringComparison.Ordinal))
            return false;
        if (sourceName == "Jarvis.OpenRouterChatClient") return false;
        if (sourceName == "Jarvis.CodexChatClient")
            return displayName == "jarvis.model.completion";
        return true;
    }

    public static bool LooksSensitive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains("Password=", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("Pwd=", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("://", StringComparison.Ordinal) && value.Contains('@', StringComparison.Ordinal))
            return true;
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("Authorization:", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static void ScrubHeaders(IDictionary<string, string> headers)
    {
        foreach (var key in headers.Keys.ToArray())
        {
            if (IsSensitiveHeader(key))
                headers[key] = "[redacted]";
        }
    }

    public static SentryEvent? FilterEvent(SentryEvent sentryEvent)
    {
        if (ShouldCaptureException is { } gate && sentryEvent.Exception is { } exception && !gate(exception))
            return null;

        if (sentryEvent.Request.Headers is { Count: > 0 } headers)
            ScrubHeaders(headers);
        sentryEvent.Request.Cookies = null;
        if (sentryEvent.Message?.Message is { } message && LooksSensitive(message))
            sentryEvent.Message.Formatted = "[redacted]";
        return sentryEvent;
    }

    public static SentryLog? FilterLog(SentryLog log)
    {
        if (log.Level is SentryLogLevel.Trace or SentryLogLevel.Debug or SentryLogLevel.Info)
            return null;
        if (LooksSensitive(log.Message) || LooksSensitive(log.Template))
            return null;
        foreach (var parameter in log.Parameters)
        {
            if (LooksSensitive(Convert.ToString(parameter.Value, CultureInfo.InvariantCulture)))
                return null;
        }

        return log;
    }

    public static void Add(IHostApplicationBuilder builder)
    {
        var dsn = ResolveDsn(builder.Configuration);
        if (!IsEnabled(dsn)) return;

        if (builder is WebApplicationBuilder web)
            web.WebHost.UseSentry(options => Configure(options, builder, dsn!));
        else
            builder.Logging.AddSentry(options => Configure(options, builder, dsn!));
    }

    internal static void Configure(SentryLoggingOptions options, IHostApplicationBuilder builder, string dsn)
    {
        options.Dsn = dsn;
        options.SendDefaultPii = false;
        options.Debug = false;
        options.Environment = ResolveEnvironment(builder.Configuration, builder.Environment.EnvironmentName);
        var release = ResolveRelease(builder.Configuration);
        if (release is not null) options.Release = release;
        options.TracesSampleRate = ResolveTracesSampleRate(builder.Configuration, builder.Environment.IsDevelopment());
        options.TracesSampler = context => SampleTransaction(context.TransactionContext.Name);
        options.ProfilesSampleRate = ResolveProfilesSampleRate(builder.Configuration);
        if (options.ProfilesSampleRate > 0)
            options.AddProfilingIntegration();
        options.EnableLogs = true;
        options.MinimumBreadcrumbLevel = LogLevel.Information;
        options.MinimumEventLevel = LogLevel.None;
        // Native Sentry spans stay on so agent tracing can call StartSpan. The OpenTelemetry processor
        // still forwards Jarvis activities. UseOtlp would turn those Sentry spans off.
#pragma warning disable CS0618
        options.UseOpenTelemetry();
#pragma warning restore CS0618
        options.SetBeforeSend(sentryEvent => FilterEvent(sentryEvent));
        options.SetBeforeSendLog(FilterLog);
    }

    private static bool IsSensitiveHeader(string key) =>
        key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
        || key.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
        if (!string.IsNullOrWhiteSpace(second)) return second.Trim();
        return null;
    }
}

/// <summary>
/// Forwards Jarvis activities to Sentry after the SDK has started. Model-client telemetry stays on the OTLP exporter.
/// </summary>
internal sealed class JarvisSentrySpanProcessor : BaseProcessor<Activity>
{
    private SentrySpanProcessor? _inner;

    public override void OnStart(Activity data)
    {
        if (!JarvisSentry.ShouldExportActivity(data.Source.Name, data.DisplayName)) return;
        Inner()?.OnStart(data);
    }

    public override void OnEnd(Activity data)
    {
        if (!JarvisSentry.ShouldExportActivity(data.Source.Name, data.DisplayName)) return;
        Inner()?.OnEnd(data);
    }

    private SentrySpanProcessor? Inner()
    {
        if (_inner is not null) return _inner;
        if (!SentrySdk.IsEnabled) return null;
        try
        {
            return _inner = new SentrySpanProcessor();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
