using System.Text.Json;
using Jarvis.Application.Surfaces;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class UiSurfaceBlocksTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Normalize(string kind, string? items = null, string? fields = null,
        string? actions = null, string? blocks = null, bool multiSelect = false) =>
        Json(UiSurfaceSchema.Normalize(kind, "Week", null, items is null ? null : Json(items),
            fields is null ? null : Json(fields), actions is null ? null : Json(actions), null,
            blocks is null ? null : Json(blocks), multiSelect));

    [Fact]
    public void Custom_cards_keep_known_blocks_and_clamp_values()
    {
        var schema = Normalize("custom", blocks: """
            [
              {"type":"stats","items":[{"label":"Sleep","value":"7 h","delta":"+20 min","trend":"UP"},{"label":"Steps","value":"8,400","trend":"sideways"}]},
              {"type":"progress","label":"Budget","value":1.4,"tone":"warning"},
              {"type":"bars","unit":"h","items":[{"label":"Mon","value":"3.5"},{"label":"Tue","value":-2}]},
              {"type":"columns","children":[{"type":"chips","labels":["gym"," ","run"]},{"type":"callout","text":"Rest day","icon":"rocket","tone":"loud"}]}
            ]
            """);

        Assert.Equal("custom", schema.GetProperty("kind").GetString());
        var blocks = schema.GetProperty("blocks");
        Assert.Equal(4, blocks.GetArrayLength());
        var stats = blocks[0].GetProperty("items");
        Assert.Equal("up", stats[0].GetProperty("trend").GetString());
        Assert.Equal(JsonValueKind.Null, stats[1].GetProperty("trend").ValueKind);
        Assert.Equal(1, blocks[1].GetProperty("value").GetDouble());
        Assert.Equal(3.5, blocks[2].GetProperty("items")[0].GetProperty("value").GetDouble());
        Assert.Equal(0, blocks[2].GetProperty("items")[1].GetProperty("value").GetDouble());
        var children = blocks[3].GetProperty("children");
        Assert.Equal(["gym", "run"], children[0].GetProperty("labels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("info", children[1].GetProperty("tone").GetString());
        Assert.Equal("rocket", children[1].GetProperty("icon").GetString());
    }

    [Theory]
    [InlineData("""[{"type":"html","text":"<b>x</b>"}]""", "Unknown block type")]
    [InlineData("""[{"type":"stats","items":[]}]""", "needs items")]
    [InlineData("""[{"type":"columns","children":[{"type":"columns","children":[{"type":"divider"}]}]}]""", "cannot hold")]
    [InlineData("""[{"type":"heading"}]""", "needs a text")]
    public void Custom_cards_reject_unsafe_or_malformed_blocks(string blocks, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => Normalize("custom", blocks: blocks));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Custom_cards_need_blocks_and_respect_the_block_budget()
    {
        Assert.Throws<ArgumentException>(() => Normalize("custom"));
        var many = "[" + string.Join(",", Enumerable.Repeat("""{"type":"divider"}""", 25)) + "]";
        var error = Assert.Throws<ArgumentException>(() => Normalize("custom", blocks: many));
        Assert.Contains("24", error.Message);
    }

    [Fact]
    public void Items_carry_known_icons_values_and_badges()
    {
        var schema = Normalize("list", items: """
            [{"id":"a","title":"Rent","icon":"money","value":"€950","badge":"Due"},{"id":"b","title":"Gym","icon":"skull"}]
            """);

        var items = schema.GetProperty("items");
        Assert.Equal("money", items[0].GetProperty("icon").GetString());
        Assert.Equal("€950", items[0].GetProperty("value").GetString());
        Assert.Equal("Due", items[0].GetProperty("badge").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("icon").ValueKind);
    }

    [Fact]
    public void Multi_select_choices_get_a_done_button()
    {
        var schema = Normalize("choice", items: """[{"id":"a","title":"A"},{"id":"b","title":"B"}]""",
            multiSelect: true);

        Assert.Equal("multiple", schema.GetProperty("selection").GetString());
        var action = Assert.Single(schema.GetProperty("actions").EnumerateArray());
        Assert.Equal("done", action.GetProperty("id").GetString());
        Assert.Equal("primary", action.GetProperty("style").GetString());
    }

    [Fact]
    public void Multi_select_only_applies_to_choices()
    {
        var schema = Normalize("list", items: """[{"id":"a","title":"A"}]""", multiSelect: true);
        Assert.Equal(JsonValueKind.Null, schema.GetProperty("selection").ValueKind);
        Assert.Empty(schema.GetProperty("actions").EnumerateArray());
    }

    [Fact]
    public void Slider_fields_keep_a_valid_range()
    {
        var schema = Normalize("form", fields: """
            [{"id":"energy","label":"Energy","type":"slider","min":1,"max":5,"step":9,"unit":"pts"},
             {"id":"day","label":"Day","type":"date"},{"id":"note","label":"Note","type":"longtext"}]
            """);

        var slider = schema.GetProperty("fields")[0];
        Assert.Equal(1, slider.GetProperty("min").GetDouble());
        Assert.Equal(5, slider.GetProperty("max").GetDouble());
        Assert.Equal(JsonValueKind.Null, slider.GetProperty("step").ValueKind);
        Assert.Equal("pts", slider.GetProperty("unit").GetString());
        Assert.Equal("date", schema.GetProperty("fields")[1].GetProperty("type").GetString());

        Assert.Throws<ArgumentException>(() => Normalize("form",
            fields: """[{"id":"x","label":"X","type":"slider","min":5,"max":5}]"""));
    }
}
