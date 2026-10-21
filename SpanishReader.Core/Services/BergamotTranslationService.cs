using BergamotTranslatorSharp;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.Services;

/// <summary>Owns a lazily loaded Spanish-to-English Bergamot engine.</summary>
public sealed class BergamotTranslationService : ITranslationService, IDisposable
{
    private readonly string _configPath;
    private readonly object _gate = new();
    private BlockingService? _translator;
    private bool _disposed;

    public BergamotTranslationService(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        if (!Path.IsPathFullyQualified(configPath))
            throw new ArgumentException("An absolute model configuration path is required.", nameof(configPath));

        _configPath = configPath;
    }

    public Task<string> TranslateAsync(string spanish, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spanish);

        // Both model initialization and inference are blocking native calls. Keep them
        // off the UI thread, and reuse the model for subsequent sentence selections.
        return Task.Run(() =>
        {
            // Bergamot exposes no thread-safety guarantee. Disposal takes the same lock
            // so closing the app cannot free native memory while it is being used.
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();

                if (_translator is null)
                {
                    if (!File.Exists(_configPath))
                        throw new FileNotFoundException("The offline translation model configuration is missing.", _configPath);

                    _translator = new BlockingService(_configPath);
                }

                // A request may have been cancelled while the model was loading.
                cancellationToken.ThrowIfCancellationRequested();
                return _translator.Translate(spanish);
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _translator?.Dispose();
            _translator = null;
        }
    }
}
