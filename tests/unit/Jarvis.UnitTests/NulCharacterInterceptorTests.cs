using Jarvis.Infrastructure.Persistence;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class NulCharacterInterceptorTests
{
    [Theory]
    [InlineData("a\0b", "ab")]
    [InlineData("\0\0", "")]
    [InlineData("fine \u00e9 \U0001F600", "fine \u00e9 \U0001F600")]
    public void Plain_text_loses_only_its_nul_characters(string value, string expected) =>
        Assert.Equal(expected, NulCharacterInterceptor.Clean(value, json: false));

    [Theory]
    [InlineData("{\"a\":\"x\\u0000y\"}", "{\"a\":\"xy\"}")]
    [InlineData("{\"a\":\"x\\u0000y\\u0000\"}", "{\"a\":\"xy\"}")]
    [InlineData("{\"a\":\"x\\\\u0000y\"}", "{\"a\":\"x\\\\u0000y\"}")] // an escaped backslash then the text u0000
    [InlineData("{\"a\":\"x\\\\\\u0000y\"}", "{\"a\":\"x\\\\y\"}")] // an escaped backslash, then an escaped NUL
    public void Json_loses_the_escaped_nul_a_serializer_writes_but_not_look_alikes(string json, string expected) =>
        Assert.Equal(expected, NulCharacterInterceptor.Clean(json, json: true));

    [Fact]
    public void Text_without_a_nul_is_returned_as_is()
    {
        const string value = "nothing to remove";

        Assert.Same(value, NulCharacterInterceptor.Clean(value, json: false));
        Assert.Same(value, NulCharacterInterceptor.Clean(value, json: true));
    }
}
