using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Errors;

internal static class ApiProblemResults
{
    private static IHttpContextAccessor? HttpContextAccessor { get; set; }

    internal static HttpContext? CurrentContext => HttpContextAccessor?.HttpContext;

    public static void Configure(IHttpContextAccessor httpContextAccessor) =>
        HttpContextAccessor = httpContextAccessor;

    public static IResult Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult Validation(IDictionary<string, string[]> errors)
    {
        var httpContext = HttpContextAccessor?.HttpContext;
        if (httpContext is null)
            return Results.ValidationProblem(errors);
        return Validation(httpContext, errors);
    }

    public static IResult Validation(HttpContext httpContext, string field, string message) =>
        Validation(httpContext, new Dictionary<string, string[]> { [field] = [message] });

    public static IResult Validation(HttpContext httpContext, IDictionary<string, string[]> errors) =>
        TypedResults.Problem(JarvisProblemDetails.CreateValidation(errors, httpContext));

    public static IResult NotFound(string? detail = null)
    {
        var httpContext = HttpContextAccessor?.HttpContext;
        if (httpContext is null) return Results.NotFound();
        return NotFound(httpContext, detail);
    }

    public static IResult NotFound(HttpContext httpContext, string? detail = null) =>
        Problem(httpContext, ApiErrorCodes.ResourceNotFound, "Resource not found",
            StatusCodes.Status404NotFound, detail ?? "The requested resource was not found.");

    public static IResult Conflict(string detail)
    {
        var httpContext = HttpContextAccessor?.HttpContext;
        if (httpContext is null) return Results.Conflict(new { message = detail });
        return Conflict(httpContext, detail);
    }

    public static IResult Conflict(HttpContext httpContext, string detail) =>
        Problem(httpContext, ApiErrorCodes.Conflict, "Conflict", StatusCodes.Status409Conflict, detail);

    public static IResult DependencyUnavailable(string detail)
    {
        var httpContext = HttpContextAccessor?.HttpContext;
        if (httpContext is null)
            return Results.Problem(detail, statusCode: StatusCodes.Status503ServiceUnavailable);
        return DependencyUnavailable(httpContext, detail);
    }

    public static IResult DependencyUnavailable(HttpContext httpContext, string detail) =>
        Problem(httpContext, ApiErrorCodes.DependencyUnavailable, "Dependency unavailable",
            StatusCodes.Status503ServiceUnavailable, detail);

    public static IResult BadGateway(string detail)
    {
        var httpContext = HttpContextAccessor?.HttpContext;
        if (httpContext is null)
            return Results.Problem(detail, statusCode: StatusCodes.Status502BadGateway);
        return BadGateway(httpContext, detail);
    }

    public static IResult BadGateway(HttpContext httpContext, string detail) =>
        Problem(httpContext, ApiErrorCodes.DependencyUnavailable, "Dependency unavailable",
            StatusCodes.Status502BadGateway, detail);

    public static IResult MalwareRejected()
    {
        var httpContext = HttpContextAccessor?.HttpContext;
        if (httpContext is null)
        {
            return Results.UnprocessableEntity(new
            {
                message = "The uploaded file was rejected by malware scanning."
            });
        }

        return MalwareRejected(httpContext);
    }

    public static IResult MalwareRejected(HttpContext httpContext) =>
        Problem(httpContext, ApiErrorCodes.MalwareRejected, "Upload rejected",
            StatusCodes.Status422UnprocessableEntity,
            "The uploaded file was rejected by malware scanning.");

    public static IResult RequestCancelled(HttpContext httpContext) =>
        Problem(httpContext, ApiErrorCodes.RequestCancelled, "Request cancelled",
            StatusCodes.Status499ClientClosedRequest, "The request was cancelled.");

    public static IResult Failed(string detail) => BadGateway(detail);

    public static IResult Problem(HttpContext httpContext, string code, string title, int status, string detail)
    {
        var problem = JarvisProblemDetails.Create(code, title, status, detail, httpContext);
        return TypedResults.Problem(problem);
    }
}
