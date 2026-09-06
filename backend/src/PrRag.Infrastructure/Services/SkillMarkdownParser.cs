using PrRag.Application.Domain;

namespace PrRag.Infrastructure.Services;

/// <summary>
/// Parses a skill markdown file: <c>---</c> front matter carrying
/// <c>name</c>, <c>description</c> and <c>version</c>, followed by the guidance
/// body. Returns null when required metadata or a non-empty body is missing.
/// </summary>
internal static class SkillMarkdownParser
{
    public static Skill? Parse(string content)
    {
        using var reader = new StringReader(content);

        if (reader.ReadLine()?.Trim() != "---")
        {
            return null;
        }

        string? name = null;
        string? description = null;
        int? version = null;
        var body = new List<string>();
        var inFrontMatter = true;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (inFrontMatter)
            {
                if (line.Trim() == "---")
                {
                    inFrontMatter = false;
                    continue;
                }

                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                switch (key)
                {
                    case "name":
                        name = value;
                        break;
                    case "description":
                        description = value;
                        break;
                    case "version":
                        if (int.TryParse(value, out var parsed))
                        {
                            version = parsed;
                        }
                        break;
                }
            }
            else
            {
                body.Add(line);
            }
        }

        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(description)
            || version is null
            || version < 0)
        {
            return null;
        }

        var guidance = string.Join("\n", body).Trim();
        if (string.IsNullOrWhiteSpace(guidance))
        {
            return null;
        }

        return new Skill(name, description, version.Value, guidance);
    }
}