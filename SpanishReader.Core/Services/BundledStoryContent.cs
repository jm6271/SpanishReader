using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;

namespace SpanishReader.Core.Services;

/// <summary>Shared preparation and validation for the CLI and the installed library.</summary>
public static class BundledStoryContent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<CEFR>(allowIntegerValues: false) }
    };

    private static readonly BundledStoryJsonContext JsonContext = new(JsonOptions);

    public static string Serialize(BundledStory story)
    {
        Validate(story);
        return JsonSerializer.Serialize(story, JsonContext.BundledStory) + "\n";
    }

    public static BundledStory Deserialize(string json)
    {
        var story = JsonSerializer.Deserialize(json, JsonContext.BundledStory)
            ?? throw new InvalidDataException("The bundled story is empty.");
        Validate(story);
        return story;
    }

    public static void Validate(BundledStory story, bool requireTranslations = false)
    {
        if (story.FormatVersion != 1 || string.IsNullOrWhiteSpace(story.Id) ||
            string.IsNullOrWhiteSpace(story.Title) || story.EnglishTitle is null || story.Author is null ||
            !Enum.IsDefined(story.Level) || story.Sentences is null || story.Sentences.Count == 0 ||
            story.Sentences.Any(s => s is null || string.IsNullOrWhiteSpace(s.SpanishText) || s.EnglishText is null))
            throw new InvalidDataException("Invalid bundled story: check formatVersion, id, metadata, level and sentences.");
        if (requireTranslations && (string.IsNullOrWhiteSpace(story.EnglishTitle) ||
            story.Sentences.Any(s => string.IsNullOrWhiteSpace(s.EnglishText))))
            throw new InvalidDataException($"Bundled story '{story.Id}' has missing English translations.");
    }

    public static BundledStory ParseMarkdown(string markdown)
    {
        // The authoring format deliberately supports only flat, single-line YAML
        // strings. Reject other YAML constructs rather than silently corrupting them.
        using var reader = new StringReader(markdown.TrimStart('\uFEFF'));
        if (reader.ReadLine() != "---") throw new InvalidDataException("Expected opening --- metadata delimiter.");
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) is not null && line != "---")
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) throw new InvalidDataException($"Invalid metadata line: {line}");
            var key = line[..colon].Trim();
            if (key is not ("id" or "title" or "englishTitle" or "level" or "author"))
                throw new InvalidDataException($"Unknown metadata key: {key}");
            var value = line[(colon + 1)..].Trim();
            if (value.StartsWith('"'))
                value = JsonSerializer.Deserialize(value, JsonContext.String) ?? string.Empty;
            else if (value.StartsWith('\''))
            {
                if (value.Length < 2 || !value.EndsWith('\'')) throw new InvalidDataException("Unclosed metadata quote.");
                value = value[1..^1].Replace("''", "'");
            }
            else if (value.Length > 0 && "|>[]{&*!".Contains(value[0]))
                throw new InvalidDataException("Use plain or quoted single-line metadata strings.");
            if (!metadata.TryAdd(key, value)) throw new InvalidDataException($"Duplicate metadata key: {key}");
        }
        if (line is null) throw new InvalidDataException("Expected closing --- metadata delimiter.");
        if (!metadata.TryGetValue("level", out var levelText) ||
            !Enum.TryParse<CEFR>(levelText, out var level) || !Enum.IsDefined(level) || level.ToString() != levelText)
            throw new InvalidDataException("Specify a CEFR level from A1 to C2.");

        // Blank lines separating the header from prose are not part of the story;
        // all whitespace within the prose survives splitting and JSON round trips.
        var body = reader.ReadToEnd().TrimStart('\r', '\n');
        var story = new BundledStory
        {
            Id = metadata.GetValueOrDefault("id", ""), Title = metadata.GetValueOrDefault("title", ""),
            EnglishTitle = metadata.GetValueOrDefault("englishTitle", ""),
            Author = metadata.GetValueOrDefault("author", ""), Level = level,
            Sentences = SpanishSentenceSplitter.Split(body).Select(s => new BundledSentence { SpanishText = s }).ToList()
        };
        Validate(story);
        return story;
    }

    public static async Task TranslateAsync(BundledStory story, ITranslationService translator,
        BundledStory? previous = null, bool regenerate = false, Action<int, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(story);
        if (previous is not null && previous.Id != story.Id)
            throw new InvalidDataException("The output belongs to another story ID. Choose another output path.");

        // Match repeated sentences by occurrence, just like story editing. Changes
        // to Spanish invalidate that sentence's translation; reviewed English stays.
        var reusable = (previous?.Sentences ?? []).GroupBy(s => s.SpanishText.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<BundledSentence>(g), StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(story.EnglishTitle))
            story.EnglishTitle = !regenerate && previous?.Title == story.Title &&
                !string.IsNullOrWhiteSpace(previous.EnglishTitle) ? previous.EnglishTitle :
                (await translator.TranslateAsync(story.Title, cancellationToken)).Trim();

        for (var i = 0; i < story.Sentences.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sentence = story.Sentences[i];
            var prior = reusable.TryGetValue(sentence.SpanishText.Trim(), out var matches) && matches.Count > 0
                ? matches.Dequeue() : null;
            sentence.EnglishText = !regenerate && !string.IsNullOrWhiteSpace(prior?.EnglishText)
                ? prior.EnglishText : (await translator.TranslateAsync(sentence.SpanishText.Trim(), cancellationToken)).Trim();
            progress?.Invoke(i + 1, story.Sentences.Count);
        }
        Validate(story, requireTranslations: true);
    }
}
