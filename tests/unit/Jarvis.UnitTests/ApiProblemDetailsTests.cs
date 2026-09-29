using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ApiProblemDetailsTests
{
    [Fact]
    public void Create_includes_contract_extensions()
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        httpContext.Request.Path = "/api/v1/example";

        var problem = JarvisProblemDetails.Create(
            ApiErrorCodes.Conflict,
            "Conflict",
            StatusCodes.Status409Conflict,
            "Busy.",
            httpContext);

        Assert.Equal("https://jarvis.dev/problems/v1/conflict", problem.Type);
        Assert.Equal(ApiErrorCodes.Conflict, problem.Extensions["code"]);
        Assert.Equal("trace-123", problem.Extensions["traceId"]);
        Assert.Equal(ApiErrorCodes.ContractVersion, problem.Extensions["apiErrorVersion"]);
    }

    [Fact]
    public void CreateValidation_preserves_field_errors_extension()
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-456" };
        httpContext.Request.Path = "/api/v1/memory";

        var problem = JarvisProblemDetails.CreateValidation(
            new Dictionary<string, string[]> { ["kind"] = ["Choose a supported memory category."] },
            httpContext);

        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.Extensions["code"]);
        Assert.Equal("Choose a supported memory category.", problem.Errors["kind"][0]);
    }

    [Theory]
    [InlineData(typeof(MalwareDetectedException), ApiErrorCodes.MalwareRejected, 422)]
    [InlineData(typeof(UnauthorizedAccessException), ApiErrorCodes.AuthenticationRequired, 401)]
    [InlineData(typeof(TimeoutException), ApiErrorCodes.Timeout, 504)]
    public void ExceptionProblemMapper_maps_known_exceptions(Type exceptionType, string code, int status)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        var mapped = ExceptionProblemMapper.Map(exception);
        Assert.Equal(code, mapped.Code);
        Assert.Equal(status, mapped.Status);
    }

    [Fact]
    public void ApplyProductionRedaction_hides_internal_failure_details()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Detail = "Npgsql.PostgresException: password authentication failed for user \"jarvis\"",
        };
        problem.Extensions["stackTrace"] = "at Jarvis.Api.Program.Main()";

        JarvisProblemDetails.ApplyProductionRedaction(problem, isDevelopment: false);

        Assert.Equal("An unexpected error occurred.", problem.Detail);
        Assert.False(problem.Extensions.ContainsKey("stackTrace"));
    }

    [Fact]
    public void Serialized_problem_uses_application_problem_json_shape()
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-789" };
        httpContext.Request.Path = "/api/v1/files";
        var problem = JarvisProblemDetails.Create(
            ApiErrorCodes.MalwareRejected,
            "Upload rejected",
            StatusCodes.Status422UnprocessableEntity,
            "The uploaded file was rejected by malware scanning.",
            httpContext);

        var json = JsonSerializer.Serialize(problem);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("malware_rejected", root.GetProperty("code").GetString());
        Assert.Equal("trace-789", root.GetProperty("traceId").GetString());
        Assert.Equal(1, root.GetProperty("apiErrorVersion").GetInt32());
        Assert.Equal("The uploaded file was rejected by malware scanning.", root.GetProperty("detail").GetString());
    }
}
