using SpanishReader.Core.Abstractions;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace SpanishReader.Services;

public sealed partial class WindowsReadAloudService : IReadAloudService, IDisposable
{
    private readonly ReadAloudMediaPlayer _player = new();
    private readonly VoiceInformation[] _voices = SpeechSynthesizer.AllVoices
        .Where(voice => voice.Language.Equals("es", StringComparison.OrdinalIgnoreCase)
            || voice.Language.StartsWith("es-", StringComparison.OrdinalIgnoreCase))
        .OrderBy(voice => voice.Language).ThenBy(voice => voice.DisplayName).ToArray();

    public WindowsReadAloudService()
    {
        Voices = Array.AsReadOnly(_voices.Select(voice =>
            new SpeechVoice(voice.Id, $"{voice.DisplayName} ({voice.Language})")).ToArray());
        DefaultVoiceId = Voices.Count == 0 ? null : SpeechSynthesizer.DefaultVoice?.Id;
    }

    public IReadOnlyList<SpeechVoice> Voices { get; }
    public string? DefaultVoiceId { get; }
    public bool IsAvailable => Voices.Count > 0;
    // Windows voices are enumerated when the app starts.
    public event Action? AvailabilityChanged { add { } remove { } }

    public async Task<PreparedReadAloudSentence> PrepareAsync(string text, string voiceId, double speed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var synthesizer = new SpeechSynthesizer { Voice = _voices.First(voice => voice.Id == voiceId) };
        synthesizer.Options.SpeakingRate = speed;
        using var speech = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask(cancellationToken);
        var contentType = speech.ContentType;
        // Copy the WAV so abandoned prefetches retain only managed memory.
        using var wave = new MemoryStream();
        using var output = wave.AsRandomAccessStream();
        using var input = speech.GetInputStreamAt(0);
        await RandomAccessStream.CopyAsync(input, output).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var audio = wave.ToArray();
        return async (started, token) =>
        {
            using var prepared = new MemoryStream(audio, writable: false);
            await _player.PlayAsync(prepared, contentType, started, token);
        };
    }

    public void Pause() => _player.Pause();
    public void Resume() => _player.Resume();
    public void Stop() => _player.Stop();
    public void Dispose() => Stop();
}
