using Jarvis.Api.Hosting;
using Jarvis.Api.Security;
using Jarvis.Infrastructure.Identity;
using Jarvis.Worker.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// Builds the real API and worker containers with <see cref="ServiceProviderOptions.ValidateOnBuild"/> so a
/// registration whose dependencies cannot be resolved, or a singleton that captures a scoped service, fails
/// here instead of on the first request after a deploy. Nothing is started and no service connects anywhere.
/// </summary>
public sealed class ServiceRegistrationTests
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["ConnectionStrings:jarvis"] = "Host=localhost;Database=jarvis;Username=jarvis;Password=unused",
        ["ObjectStorage:ServiceUrl"] = "http://localhost:3900",
        ["ObjectStorage:AccessKey"] = "unused",
        ["ObjectStorage:SecretKey"] = "unused",
        ["ObjectStorage:Bucket"] = "jarvis-files",
        ["Temporal:Address"] = "localhost:7233",
    };

    private static readonly ServiceProviderOptions Validate = new() { ValidateOnBuild = true, ValidateScopes = true };

    [Fact]
    public void Api_container_resolves_every_registration()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration.AddInMemoryCollection(Settings);
        var accountTokens = AccountTokenOptions.From(builder.Configuration, isDevelopment: true);
        builder.Services.AddJarvisAuthentication(accountTokens, isDevelopment: true);
        builder.Services.AddJarvisApi(builder.Configuration, accountTokens);
        builder.Services.AddJarvisRateLimiting(builder.Configuration);

        using var provider = builder.Services.BuildServiceProvider(Validate);
    }

    [Fact]
    public void Worker_container_resolves_every_registration()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(Settings);
        builder.Services.AddJarvisWorker(builder.Configuration);

        using var provider = builder.Services.BuildServiceProvider(Validate);
    }
}
