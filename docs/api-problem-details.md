# Jarvis API problem details (v1)

Jarvis APIs return errors as [RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) `application/problem+json` documents.

## Contract

Every error response includes:

| Member | Description |
| --- | --- |
| `type` | URI of the form `https://jarvis.dev/problems/v1/{code}` |
| `title` | Short human-readable summary |
| `status` | HTTP status code |
| `detail` | Safe, client-facing explanation |
| `instance` | Request path |
| `code` | Stable machine-readable identifier (extension) |
| `traceId` | Correlation / trace identifier (extension) |
| `apiErrorVersion` | Contract version, currently `1` (extension) |

Validation failures also include an `errors` object mapping field names to message arrays (ASP.NET `ValidationProblemDetails`).

## Stable codes

- `validation_failed`
- `authentication_required`
- `authorization_forbidden`
- `resource_not_found`
- `conflict`
- `rate_limited`
- `dependency_unavailable`
- `timeout`
- `request_cancelled`
- `malware_rejected`
- `internal_error`

## Backward compatibility

During the mobile rollout, clients should:

1. Prefer `code` when present.
2. Fall back to HTTP status and legacy `{ "message": "..." }` payloads where older endpoints still return them.
3. Use `firstProblemMessage` / `detail` / `errors` for user-visible text.

Production responses never include stack traces, SQL, credential-bearing URLs, or other sensitive internals. Internal failures use a generic `detail` with `code: internal_error`.

## Pipeline

- Unhandled exceptions are formatted by `IExceptionHandler` (`JarvisExceptionHandler`).
- `/api/*` responses with HTTP error status codes are normalized by `JarvisApiProblemResponseMiddleware`, including empty `404` responses and legacy `{ "message": "..." }` bodies.
- JWT bearer challenges return `application/problem+json` and `WWW-Authenticate: Bearer`.
