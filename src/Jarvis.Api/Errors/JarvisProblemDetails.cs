using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Errors;

internal static class JarvisProblemDetails
{
    public const string ProblemTypeBase = "https://jarvis.dev/problems/v1/";

    public static string TypeUriFor(string code) => $"{ProblemTypeBase}{code}";

    public static string ResolveTraceId(HttpContext httpContext) =>
        Activity.Current?.Id ?? httpContext.TraceIdentifier;

    public static ProblemDetails Create(string code, string title, int status, string detail, HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Type = TypeUriFor(code),
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        ApplyContractExtensions(problem, code, httpContext);
        return problem;
    }

    public static ValidationProblemDetails CreateValidation(
        IDictionary<string, string[]> errors,
        HttpContext httpContext,
        string? detail = null)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Type = TypeUriFor(ApiErrorCodes.ValidationFailed),
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        ApplyContractExtensions(problem, ApiErrorCodes.ValidationFailed, httpContext);
        return problem;
    }

    public static void ApplyContractExtensions(ProblemDetails problem, string code, HttpContext httpContext)
    {
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = ResolveTraceId(httpContext);
        problem.Extensions["apiErrorVersion"] = ApiErrorCodes.ContractVersion;
    }

    public static void ApplyProductionRedaction(ProblemDetails problem, bool isDevelopment)
    {
        if (isDevelopment) return;
        if (problem.Status is null or < StatusCodes.Status500InternalServerError) return;

        problem.Detail = "An unexpected error occurred.";
        problem.Extensions.Remove("exception");
        problem.Extensions.Remove("stackTrace");
        problem.Extensions.Remove("stack");
    }
}
