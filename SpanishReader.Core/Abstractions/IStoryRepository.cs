using SpanishReader.Core.Models;

namespace SpanishReader.Core.Abstractions;

public interface IStoryRepository
{
    Task<Story> AddAsync(Story story);
    Task<IReadOnlyList<Story>> GetAllAsync();
}
