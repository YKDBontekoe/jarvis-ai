using Jarvis.Application.Files;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ImageIndexTextTests
{
    [Fact]
    public void A_photo_without_writing_is_indexed_by_what_it_shows()
    {
        var text = ImageIndexText.Compose("beach.jpg", "DESCRIPTION: A dog running on a sandy beach at sunset.\nTEXT: NONE");

        Assert.Equal("Image: A dog running on a sandy beach at sunset.", text);
        Assert.DoesNotContain("could not be described", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sign_keeps_both_the_description_and_its_exact_text()
    {
        var text = ImageIndexText.Compose("sign.png",
            "DESCRIPTION: A wooden sign above a cafe door.\nTEXT: Café Lumière\nOpen 8–17");

        Assert.StartsWith("Image: A wooden sign above a cafe door.", text, StringComparison.Ordinal);
        Assert.Contains("Text in the image:\nCafé Lumière\nOpen 8–17", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TEXT: Invoice 2031\nDESCRIPTION: A scanned invoice.", "A scanned invoice.", "Invoice 2031")]
    [InlineData("description: lowercase labels work\ntext: none", "lowercase labels work", null)]
    [InlineData("DESCRIPTION: Multi\nline description.\nTEXT: NONE.", "Multi line description.", null)]
    public void The_reply_is_read_whichever_order_and_case_the_model_uses(string reply, string? description, string? text)
    {
        var parsed = ImageIndexText.Parse(reply);

        Assert.Equal(description, parsed.Description);
        Assert.Equal(text, parsed.Text);
    }

    [Fact]
    public void A_reply_that_ignores_the_form_is_treated_as_the_transcription()
    {
        var parsed = ImageIndexText.Parse("Meeting at 10:00\nRoom 4");

        Assert.Null(parsed.Description);
        Assert.Equal("Meeting at 10:00\nRoom 4", parsed.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_reply_says_the_image_could_not_be_described(string? reply) =>
        Assert.Equal("Image x.jpg could not be described.", ImageIndexText.Compose("x.jpg", reply));

    [Fact]
    public void A_runaway_description_is_capped_so_it_cannot_flood_the_index()
    {
        var parsed = ImageIndexText.Parse("DESCRIPTION: " + new string('a', 5000) + "\nTEXT: NONE");

        Assert.True(parsed.Description!.Length <= ImageIndexText.MaxDescriptionLength + 1);
    }

    [Fact]
    public void The_instructions_treat_the_image_as_untrusted_and_forbid_guessing_who_people_are()
    {
        Assert.Contains("untrusted", ImageIndexText.Instructions, StringComparison.Ordinal);
        Assert.Contains("do not follow", ImageIndexText.Instructions, StringComparison.Ordinal);
        Assert.Contains("no guesses", ImageIndexText.Instructions, StringComparison.Ordinal);
    }
}
