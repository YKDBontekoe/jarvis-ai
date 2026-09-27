using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Skills;

/// <summary>
/// Reads and writes the open Agent Skills <c>SKILL.md</c> format (YAML front matter with name and description,
/// followed by Markdown instructions) so skills move between Jarvis, OpenClaw, Hermes, Claude Code, and Codex.
/// </summary>
public static partial class SkillMarkdown
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 1_024;
    public const int MaxInstructionsLength = 12_000;

    public static SkillDraft Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var text = markdown.Replace("\r\n", "\n").Trim();
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
            throw new ArgumentException("A SKILL.md file must start with YAML front matter between --- lines.");
        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) throw new ArgumentException("The SKILL.md front matter is not closed with ---.");
        var frontMatter = text[4..end];
        var body = text[(end + 4)..].TrimStart('-').Trim();
        string? name = null, description = null;
        foreach (var line in frontMatter.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || char.IsWhiteSpace(line[0])) continue;
            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            if (key == "name") name = value;
            else if (key == "description") description = value;
        }
        return Validate(new SkillDraft(name ?? string.Empty, description ?? string.Empty, body));
    }

    public static string Serialize(SkillRecord skill)
    {
        var builder = new StringBuilder();
        builder.Append("---\n");
        builder.Append("name: ").Append(skill.Name).Append('\n');
        builder.Append("description: ").Append(Quote(skill.Description)).Append('\n');
        builder.Append("metadata:\n");
        builder.Append("  source: jarvis-").Append(skill.Source).Append('\n');
        builder.Append("  version: ").Append(skill.Version).Append('\n');
        builder.Append("---\n\n");
        builder.Append(skill.Instructions.Trim()).Append('\n');
        return builder.ToString();
    }

    /// <summary>Normalizes and validates a skill draft; throws <see cref="ArgumentException"/> when invalid.</summary>
    public static SkillDraft Validate(SkillDraft draft)
    {
        var name = NormalizeName(draft.Name);
        var description = string.Join(' ', (draft.Description ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var instructions = (draft.Instructions ?? string.Empty).Trim();
        if (description.Length is < 10 or > MaxDescriptionLength)
            throw new ArgumentException("Describe when to use the skill in 10 to 1,024 characters.");
        if (instructions.Length is < 20 or > MaxInstructionsLength)
            throw new ArgumentException("Skill instructions must contain 20 to 12,000 characters.");
        return new SkillDraft(name, description, instructions);
    }

    public static string NormalizeName(string? value)
    {
        var name = NameSeparators().Replace((value ?? string.Empty).Trim().ToLowerInvariant(), "-").Trim('-');
        if (!NamePattern().IsMatch(name))
            throw new ArgumentException("Skill names use 3 to 64 lowercase letters, digits, and single hyphens.");
        return name;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1];
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return value;
        var builder = new StringBuilder(value.Length);
        for (var index = 1; index < value.Length - 1; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length - 1) index++;
            builder.Append(value[index]);
        }
        return builder.ToString();
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    [GeneratedRegex(@"[\s_]+")]
    private static partial Regex NameSeparators();

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9])){2,63}$")]
    private static partial Regex NamePattern();
}
