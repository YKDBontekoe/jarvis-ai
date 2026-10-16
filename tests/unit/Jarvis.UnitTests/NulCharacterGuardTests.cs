using System.Text.Json;
using Jarvis.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class NulCharacterGuardTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new NulStrippingStringConverter() }
    };

    private sealed record Body(string? Name, string[]? Tags, Dictionary<string, string>? Extra);

    [Fact]
    public void Text_in_a_body_loses_its_nul_characters_everywhere()
    {
        var body = JsonSerializer.Deserialize<Body>(
            "{\"name\":\"a\\u0000b\",\"tags\":[\"x\\u0000\",\"y\"],\"extra\":{\"k\":\"\\u0000v\"}}", Options)!;

        Assert.Equal("ab", body.Name);
        Assert.Equal(["x", "y"], body.Tags!);
        Assert.Equal("v", body.Extra!["k"]);
    }

    [Fact]
    public void Null_and_ordinary_text_pass_through_and_output_is_unchanged()
    {
        var body = JsonSerializer.Deserialize<Body>("{\"name\":null,\"tags\":[\"é😀\"]}", Options)!;

        Assert.Null(body.Name);
        Assert.Equal("é😀", body.Tags![0]);
        Assert.Equal(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(body, Options));
    }

    [Theory]
    [InlineData("/api/v1/memory", "", false)]
    [InlineData("/api/v1/search", "?q=hello%20world", false)]
    [InlineData("/api/v1/search", "?q=a%00b", true)]
    [InlineData("/api/v1/search", "?q=a%00B&x=%00", true)]
    public void A_nul_in_the_query_is_found(string path, string query, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        Assert.Equal(expected, NulCharacterGuard.HasNul(context.Request));
    }

    [Fact]
    public void A_nul_in_the_path_is_found()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/integrations/git\0hub/credentials/token";

        Assert.True(NulCharacterGuard.HasNul(context.Request));
    }
}
