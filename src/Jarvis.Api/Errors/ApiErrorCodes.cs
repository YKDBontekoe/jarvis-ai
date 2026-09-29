namespace Jarvis.Api.Errors;

/// <summary>Stable machine-readable codes for Jarvis API problem responses (contract version 1).</summary>
public static class ApiErrorCodes
{
    public const int ContractVersion = 1;

    public const string ValidationFailed = "validation_failed";
    public const string AuthenticationRequired = "authentication_required";
    public const string AuthorizationForbidden = "authorization_forbidden";
    public const string ResourceNotFound = "resource_not_found";
    public const string Conflict = "conflict";
    public const string RateLimited = "rate_limited";
    public const string DependencyUnavailable = "dependency_unavailable";
    public const string Timeout = "timeout";
    public const string RequestCancelled = "request_cancelled";
    public const string MalwareRejected = "malware_rejected";
    public const string InternalError = "internal_error";
}
