using System.Net.Sockets;
using Jarvis.Application;
using Jarvis.Application.Files;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Api.Errors;

internal static class ExceptionProblemMapper
{
    internal readonly record struct MappedProblem(string Code, string Title, int Status, string Detail);

    public static MappedProblem Map(Exception exception, HttpContext? httpContext = null)
    {
        if (CancellationExceptions.Unwrap(exception) is not null)
        {
            if (exception is Jarvis.Api.Conversations.RunStoppedException ||
                httpContext?.RequestAborted.IsCancellationRequested == true)
            {
                return new MappedProblem(
                    ApiErrorCodes.RequestCancelled,
                    "Request cancelled",
                    StatusCodes.Status499ClientClosedRequest,
                    "The request was cancelled.");
            }

            return new MappedProblem(
                ApiErrorCodes.Timeout,
                "Timeout",
                StatusCodes.Status504GatewayTimeout,
                "The request timed out.");
        }

        return exception switch
        {
            UnauthorizedAccessException => new MappedProblem(
                ApiErrorCodes.AuthenticationRequired,
                "Authentication required",
                StatusCodes.Status401Unauthorized,
                SafeClientMessage(exception, "Authentication is required.")),

            MalwareDetectedException => new MappedProblem(
                ApiErrorCodes.MalwareRejected,
                "Upload rejected",
                StatusCodes.Status422UnprocessableEntity,
                "The uploaded file was rejected by malware scanning."),

            // Raised by the framework for bodies it cannot read (malformed JSON, wrong type, too large): the
            // client's fault, so keep its status instead of reporting a server error.
            BadHttpRequestException badRequest => FromStatusCode(badRequest.StatusCode),

            ArgumentException argument => new MappedProblem(
                ApiErrorCodes.ValidationFailed,
                "Validation failed",
                StatusCodes.Status400BadRequest,
                SafeClientMessage(argument, "The request was invalid.")),

            DbUpdateException => new MappedProblem(
                ApiErrorCodes.Conflict,
                "Conflict",
                StatusCodes.Status409Conflict,
                "The request could not be completed because of a conflicting change."),

            TimeoutException => new MappedProblem(
                ApiErrorCodes.Timeout,
                "Timeout",
                StatusCodes.Status504GatewayTimeout,
                "The request timed out."),

            HttpRequestException => new MappedProblem(
                ApiErrorCodes.DependencyUnavailable,
                "Dependency unavailable",
                StatusCodes.Status503ServiceUnavailable,
                "A required dependency is temporarily unavailable."),

            SocketException => new MappedProblem(
                ApiErrorCodes.DependencyUnavailable,
                "Dependency unavailable",
                StatusCodes.Status503ServiceUnavailable,
                "A required dependency is temporarily unavailable."),

            _ => new MappedProblem(
                ApiErrorCodes.InternalError,
                "Internal server error",
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred."),
        };
    }

    private static MappedProblem FromStatusCode(int statusCode)
    {
        var (code, title, detail) = StatusCodeProblemDefaults.For(statusCode);
        return new MappedProblem(code, title, statusCode,
            statusCode == StatusCodes.Status400BadRequest ? "The request could not be read." : detail);
    }

    private static string SafeClientMessage(Exception exception, string fallback)
    {
        var message = exception.Message;
        if (string.IsNullOrWhiteSpace(message)) return fallback;
        if (ContainsSensitiveData(message)) return fallback;
        return message;
    }

    private static bool ContainsSensitiveData(string message)
    {
        if (message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("apikey", StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Contains(" at ", StringComparison.Ordinal) &&
            (message.Contains(".cs:line", StringComparison.OrdinalIgnoreCase) ||
             message.Contains(" in ", StringComparison.Ordinal)))
            return true;

        if (message.Contains("SELECT ", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("INSERT ", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("DELETE ", StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Contains("://") && message.Contains('@'))
            return true;

        return false;
    }
}
