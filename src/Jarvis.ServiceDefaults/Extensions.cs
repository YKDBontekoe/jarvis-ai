using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks();
        builder.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Jarvis.Api")
                .AddMeter("Jarvis.Memory")
                .AddMeter("Jarvis.CodexChatClient")
                .AddOtlpExporter())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.RecordException = true)
                .AddHttpClientInstrumentation(options => options.RecordException = true)
                .AddSource("Jarvis.CodexChatClient")
                .AddSource("Jarvis.Api")
                .AddSource("Jarvis.Memory")
                .AddSource("Jarvis.Worker")
                .AddOtlpExporter());

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapHealthChecks("/alive", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();
        return app;
    }
}
