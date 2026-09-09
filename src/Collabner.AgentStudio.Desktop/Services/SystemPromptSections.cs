// Copyright © 2026 Collabner. All rights reserved.

using System.Text;

namespace Collabner.AgentStudio.Desktop.Services;

/// <summary>
/// Splits a system prompt into markdown sections so the Agent tab can edit only the tool- and
/// skill-related parts while leaving the rest of <c>systemprompt.md</c> untouched. Sections are
/// delimited by ATX headings (<c># … ######</c>); a matching heading pulls in its nested subsections.
/// </summary>
internal static class SystemPromptSections
{
    private static readonly string[] Keywords = ["tool", "skill"];

    private sealed record Section(int Level, string Heading, string[] Lines, bool Relevant);

    /// <summary>Returns the concatenated tool/skill sections of <paramref name="text"/>, or empty when none match.</summary>
    public static string ExtractToolSkill(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var (_, sections) = Parse(text);
        var sb = new StringBuilder();
        foreach (var section in sections)
        {
            if (!section.Relevant)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(string.Join('\n', section.Lines));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Rebuilds the full prompt, replacing each original tool/skill section with the user's edited
    /// version (matched by heading). Non-matching sections are preserved; sections removed from the
    /// edited text are dropped, and any new sections are appended.
    /// </summary>
    public static string MergeToolSkill(string? original, string? edited)
    {
        var (preamble, sections) = Parse(original ?? string.Empty);
        var (_, editedSections) = Parse(edited ?? string.Empty);

        var editedByHeading = new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in editedSections)
        {
            if (!editedByHeading.TryGetValue(section.Heading, out var queue))
            {
                queue = new Queue<string>();
                editedByHeading[section.Heading] = queue;
            }

            queue.Enqueue(string.Join('\n', section.Lines));
        }

        var result = new List<string>();
        if (preamble.Length > 0)
        {
            result.Add(preamble);
        }

        foreach (var section in sections)
        {
            if (!section.Relevant)
            {
                result.Add(string.Join('\n', section.Lines));
                continue;
            }

            if (editedByHeading.TryGetValue(section.Heading, out var queue) && queue.Count > 0)
            {
                result.Add(queue.Dequeue());
            }
        }

        foreach (var queue in editedByHeading.Values)
        {
            while (queue.Count > 0)
            {
                result.Add(queue.Dequeue());
            }
        }

        return string.Join('\n', result);
    }

    private static (string Preamble, List<Section> Sections) Parse(string text)
    {
        var sections = new List<Section>();
        var lines = text.Split('\n');

        var heads = new List<(int Line, int Level, string Heading)>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (TryHeading(lines[i], out var level, out var heading))
            {
                heads.Add((i, level, heading));
            }
        }

        if (heads.Count == 0)
        {
            return (text, sections);
        }

        var firstHeadLine = heads[0].Line;
        var preamble = firstHeadLine == 0 ? string.Empty : string.Join('\n', lines[..firstHeadLine]);

        var raws = new List<(int Level, string Heading, string[] Lines)>();
        for (var h = 0; h < heads.Count; h++)
        {
            var start = heads[h].Line;
            var end = h + 1 < heads.Count ? heads[h + 1].Line : lines.Length;
            raws.Add((heads[h].Level, heads[h].Heading, lines[start..end]));
        }

        var relevant = new bool[raws.Count];
        for (var i = 0; i < raws.Count; i++)
        {
            if (!Matches(raws[i].Heading))
            {
                continue;
            }

            relevant[i] = true;
            for (var j = i + 1; j < raws.Count && raws[j].Level > raws[i].Level; j++)
            {
                relevant[j] = true;
            }
        }

        for (var i = 0; i < raws.Count; i++)
        {
            sections.Add(new Section(raws[i].Level, raws[i].Heading, raws[i].Lines, relevant[i]));
        }

        return (preamble, sections);
    }

    private static bool TryHeading(string line, out int level, out string heading)
    {
        level = 0;
        heading = string.Empty;

        var s = line.TrimEnd('\r');
        var lead = 0;
        while (lead < s.Length && s[lead] == ' ')
        {
            lead++;
        }

        var hashes = 0;
        var i = lead;
        while (i < s.Length && s[i] == '#')
        {
            hashes++;
            i++;
        }

        if (hashes is >= 1 and <= 6 && i < s.Length && s[i] == ' ')
        {
            level = hashes;
            heading = s[(i + 1)..].Trim();
            return true;
        }

        return false;
    }

    private static bool Matches(string heading)
        => Keywords.Any(k => heading.Contains(k, StringComparison.OrdinalIgnoreCase));
}
