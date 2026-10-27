using SpanishReader.Core.Models;

namespace SpanishReader.Core.Abstractions;

public interface IStoryRepository
{
    /// <summary>Saves story metadata and ordered sentences atomically. Empty English text is allowed.</summary>
    Task<Story> AddAsync(Story story);
    /// <summary>Updates an existing story atomically, preserving its identity and date added.</summary>
    Task<Story> UpdateAsync(Story story);
    /// <summary>Permanently deletes an existing story and its sentences.</summary>
    Task DeleteAsync(long storyId);
    /// <summary>Loads newest stories first, including each story's sentences in reading order.</summary>
    Task<IReadOnlyList<Story>> GetAllAsync();
    /// <summary>Updates one sentence's English text without changing the original story.</summary>
    Task UpdateSentenceTranslationAsync(long storyId, long sentenceId, string englishText);
}
