namespace Jarvis.Workflows;

public static class TemporalAddress
{
    public const string Default = "localhost:7233";

    /// <summary>
    /// Temporal's client wants <c>host:port</c>. Aspire endpoint references include a scheme
    /// (<c>http://</c> or <c>tcp://</c>), which the client rejects as a target host.
    /// </summary>
    public static string Normalize(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Default;
        var value = address.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return value;

        var host = uri.Host.Contains(':') && !uri.Host.StartsWith('[')
            ? $"[{uri.Host}]"
            : uri.Host;
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }
}
