namespace SpanishReader.Core.Models;

public sealed class StorySentence
{
    public long Id { get; set; }
    public long StoryId { get; set; }
    public int Position { get; set; }

    // Original-text slices retain adjacent whitespace, so concatenating sentences
    // reconstructs the story exactly, including paragraph breaks and indentation.
    public string SpanishText { get; set; } = string.Empty;

    // Empty means translation is unavailable and can be supplied later.
    public string EnglishText { get; set; } = string.Empty;
}
