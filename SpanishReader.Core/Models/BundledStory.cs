using System.Text.Json.Serialization;

namespace SpanishReader.Core.Models;

/// <summary>Portable content format. Database IDs are assigned only when imported.</summary>
public sealed class BundledStory
{
    [JsonRequired] public int FormatVersion { get; set; } = 1;
    [JsonRequired] public string Id { get; set; } = string.Empty;
    [JsonRequired] public string Title { get; set; } = string.Empty;
    public string EnglishTitle { get; set; } = string.Empty;
    [JsonRequired] public CEFR Level { get; set; }
    public string Author { get; set; } = string.Empty;
    [JsonRequired] public List<BundledSentence> Sentences { get; set; } = [];

    public Story ToStory() => new()
    {
        Title = Title, EnglishTitle = EnglishTitle, Level = Level, Author = Author,
        Sentences = Sentences.Select(s => new StorySentence
        {
            SpanishText = s.SpanishText, EnglishText = s.EnglishText
        }).ToList()
    };
}

public sealed class BundledSentence
{
    [JsonRequired] public string SpanishText { get; set; } = string.Empty;
    public string EnglishText { get; set; } = string.Empty;
}
