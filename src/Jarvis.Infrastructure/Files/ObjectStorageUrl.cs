namespace Jarvis.Infrastructure.Files;

public static class ObjectStorageUrl
{
    /// <summary>
    /// S3 clients need an http or https endpoint. Aspire publishes a non-HTTP
    /// container port as <c>tcp://host:port</c>, which the AWS SDK rejects.
    /// </summary>
    public static string Normalize(string serviceUrl)
    {
        var value = serviceUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return value;

        if (uri.Scheme is "http" or "https")
            return uri.GetLeftPart(UriPartial.Authority);

        var builder = new UriBuilder(uri) { Scheme = "http" };
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }
}
