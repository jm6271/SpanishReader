namespace SpanishReader.Core.Abstractions;

/// <summary>Runs independent, single-turn chats against a caller-supplied offline model.</summary>
public interface ILLMService
{
    Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
