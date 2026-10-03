using System.Reflection;
using Jarvis.Agents;
using Jarvis.Api.Endpoints;
using Jarvis.Application.Security;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Mcp;
using Jarvis.Memory;
using Jarvis.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// Guards the layering described in docs/architecture/layers-and-dependencies.md.
/// Each inner project may only reference the Jarvis projects listed here, and may not
/// pull in persistence, web, or orchestration frameworks.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Reminder).Assembly;
    private static readonly Assembly Application = typeof(SecretComparer).Assembly;
    private static readonly Assembly Mcp = typeof(McpAuthorizationRequiredException).Assembly;
    private static readonly Assembly Memory = typeof(JournalService).Assembly;
    private static readonly Assembly Workflows = typeof(FileProcessingWorkflow).Assembly;
    private static readonly Assembly Agents = typeof(JarvisAgentFactory).Assembly;
    private static readonly Assembly Infrastructure = typeof(JarvisDbContext).Assembly;
    private static readonly Assembly Api = typeof(ConversationEndpoints).Assembly;

    public static TheoryData<string, string[]> AllowedJarvisReferences => new()
    {
        { "Jarvis.Domain", [] },
        { "Jarvis.Application", ["Jarvis.Domain"] },
        { "Jarvis.Mcp", ["Jarvis.Application", "Jarvis.Domain"] },
        { "Jarvis.Memory", ["Jarvis.Application", "Jarvis.Domain"] },
        { "Jarvis.Workflows", ["Jarvis.Application", "Jarvis.Domain"] },
        { "Jarvis.Agents", ["Jarvis.Application", "Jarvis.Domain", "Jarvis.Mcp", "Jarvis.Workflows"] },
        { "Jarvis.Infrastructure", ["Jarvis.Application", "Jarvis.Domain"] },
    };

    [Theory]
    [MemberData(nameof(AllowedJarvisReferences))]
    public void Projects_only_reference_allowed_jarvis_layers(string assemblyName, string[] allowed)
    {
        var assembly = AssemblyByName(assemblyName);
        var unexpected = JarvisReferences(assembly).Except(allowed).ToArray();

        Assert.True(unexpected.Length == 0,
            $"{assemblyName} must not reference {string.Join(", ", unexpected)}.");
    }

    public static TheoryData<string, string[]> ForbiddenFrameworks => new()
    {
        { "Jarvis.Domain", ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "Temporalio", "Microsoft.Agents", "ModelContextProtocol"] },
        { "Jarvis.Application", ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "Temporalio", "Microsoft.Agents", "ModelContextProtocol"] },
        { "Jarvis.Memory", ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "Temporalio"] },
        { "Jarvis.Mcp", ["Microsoft.EntityFrameworkCore", "Npgsql", "Temporalio"] },
        { "Jarvis.Workflows", ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql"] },
        { "Jarvis.Agents", ["Microsoft.EntityFrameworkCore", "Npgsql"] },
    };

    [Theory]
    [MemberData(nameof(ForbiddenFrameworks))]
    public void Inner_layers_do_not_reference_infrastructure_frameworks(string assemblyName, string[] forbidden)
    {
        var assembly = AssemblyByName(assemblyName);
        var offending = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(offending.Length == 0,
            $"{assemblyName} must not reference {string.Join(", ", offending)}.");
    }

    [Fact]
    public void Api_does_not_use_the_database_context_outside_hosting()
    {
        // HTTP endpoints and background services go through Application ports; only startup
        // migration code may touch the EF context directly.
        var offenders = Api.GetTypes()
            .Where(UsesDbContext)
            .Select(OutermostType)
            .Where(type => type.Name != "Program"
                && type.Namespace?.StartsWith("Jarvis.Api.Hosting", StringComparison.Ordinal) != true)
            .Select(type => type.FullName)
            .Distinct()
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"Use an Application port instead of JarvisDbContext in: {string.Join(", ", offenders)}.");
    }

    private static bool UsesDbContext(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var dbContext = typeof(JarvisDbContext);

        return type.GetFields(flags).Any(field => field.FieldType == dbContext)
            || type.GetConstructors(flags).Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == dbContext))
            || type.GetMethods(flags).Any(method => method.GetParameters().Any(p => p.ParameterType == dbContext)
                || MethodBodyMentions(method, dbContext));
    }

    private static Type OutermostType(Type type)
    {
        while (type.DeclaringType is { } declaring)
        {
            type = declaring;
        }

        return type;
    }

    private static bool MethodBodyMentions(MethodInfo method, Type target)
    {
        try
        {
            return method.GetMethodBody()?.LocalVariables.Any(local => local.LocalType == target) == true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static IEnumerable<string> JarvisReferences(Assembly assembly) => assembly.GetReferencedAssemblies()
        .Select(reference => reference.Name ?? string.Empty)
        .Where(name => name.StartsWith("Jarvis.", StringComparison.Ordinal) && name != "Jarvis.ServiceDefaults")
        .Distinct();

    private static Assembly AssemblyByName(string name) => new[]
        {
            Domain, Application, Mcp, Memory, Workflows, Agents, Infrastructure, Api,
        }
        .Single(assembly => assembly.GetName().Name == name);
}
