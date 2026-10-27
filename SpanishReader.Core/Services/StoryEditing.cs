using SpanishReader.Core.Models;

namespace SpanishReader.Core.Services;

public static class StoryEditing
{
    public static Story Snapshot(Story story) => new()
    {
        Id = story.Id, Title = story.Title, EnglishTitle = story.EnglishTitle,
        Author = story.Author, Level = story.Level, DateAdded = story.DateAdded,
        Sentences = story.Sentences.Select(s => new StorySentence
        {
            Id = s.Id, StoryId = s.StoryId, Position = s.Position,
            SpanishText = s.SpanishText, EnglishText = s.EnglishText
        }).ToList()
    };

    /// <summary>Matches identical Spanish by occurrence, including repeated sentences.</summary>
    public static IEnumerable<(StorySentence Sentence, StorySentence? Original)> MatchSentences(
        IEnumerable<StorySentence> sentences, IEnumerable<StorySentence> originals)
    {
        // Boundary whitespace belongs to the lossless source slice, not the
        // translation. A paragraph-spacing edit should not require new inference.
        var queues = originals.GroupBy(s => s.SpanishText.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<StorySentence>(g), StringComparer.Ordinal);
        foreach (var sentence in sentences)
        {
            var original = queues.TryGetValue(sentence.SpanishText.Trim(), out var queue) && queue.Count > 0
                ? queue.Dequeue() : null;
            yield return (sentence, original);
        }
    }
}
