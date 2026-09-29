namespace Jarvis.Api.Errors;

internal static class StatusCodeProblemDefaults
{
    public static (string Code, string Title, string Detail) For(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => (
            ApiErrorCodes.ValidationFailed,
            "Validation failed",
            "The request was invalid."),
        StatusCodes.Status401Unauthorized => (
            ApiErrorCodes.AuthenticationRequired,
            "Authentication required",
            "Authentication is required."),
        StatusCodes.Status403Forbidden => (
            ApiErrorCodes.AuthorizationForbidden,
            "Forbidden",
            "You do not have permission to perform this action."),
        StatusCodes.Status404NotFound => (
            ApiErrorCodes.ResourceNotFound,
            "Resource not found",
            "The requested resource was not found."),
        StatusCodes.Status409Conflict => (
            ApiErrorCodes.Conflict,
            "Conflict",
            "The request could not be completed because of a conflict."),
        StatusCodes.Status422UnprocessableEntity => (
            ApiErrorCodes.MalwareRejected,
            "Unprocessable entity",
            "The request could not be processed."),
        StatusCodes.Status429TooManyRequests => (
            ApiErrorCodes.RateLimited,
            "Too many requests",
            "Too many requests were sent. Try again later."),
        StatusCodes.Status499ClientClosedRequest => (
            ApiErrorCodes.RequestCancelled,
            "Request cancelled",
            "The request was cancelled."),
        StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable => (
            ApiErrorCodes.DependencyUnavailable,
            "Dependency unavailable",
            "A required dependency is temporarily unavailable."),
        StatusCodes.Status504GatewayTimeout => (
            ApiErrorCodes.Timeout,
            "Timeout",
            "The request timed out."),
        _ when statusCode >= StatusCodes.Status500InternalServerError => (
            ApiErrorCodes.InternalError,
            "Internal server error",
            "An unexpected error occurred."),
        _ => (
            ApiErrorCodes.ValidationFailed,
            "Request failed",
            "The request could not be completed."),
    };
}
