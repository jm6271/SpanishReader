namespace SpanishReader.Core.Abstractions;

/// <summary>Translates plain Spanish text to English using the bundled offline model.</summary>
public interface ITranslationService
{
    /// <remarks>
    /// Cancellation prevents work from starting. The native engine cannot interrupt a
    /// translation that is already running.
    /// </remarks>
    Task<string> TranslateAsync(string spanish, CancellationToken cancellationToken = default);
}
