using SpanishReader.Core.Models;

namespace SpanishReader.Core.Abstractions;

public interface IStoryRepository
{
    /// <summary>Saves story metadata and ordered sentences atomically. Empty English text is allowed.</summary>
    Task<Story> AddAsync(Story story);
    /// <summary>Loads newest stories first, including each story's sentences in reading order.</summary>
    Task<IReadOnlyList<Story>> GetAllAsync();
}
