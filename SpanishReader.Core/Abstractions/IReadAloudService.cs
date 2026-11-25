namespace SpanishReader.Core.Abstractions;

public sealed record SpeechVoice(string Id, string Name)
{
    public override string ToString() => Name;
}

public enum ReadAloudEngine { Windows, Kokoro }

// The adapter owns the audio format. Prepared audio holds no native handles.
public delegate Task PreparedReadAloudSentence(Action playbackStarted, CancellationToken cancellationToken);

public interface IReadAloudService
{
    IReadOnlyList<SpeechVoice> Voices { get; }
    string? DefaultVoiceId { get; }
    bool IsAvailable { get; }
    event Action? AvailabilityChanged;
    // Report playback start on the caller's context, once per sentence.
    Task<PreparedReadAloudSentence> PrepareAsync(string text, string voiceId, double speed,
        CancellationToken cancellationToken);
    void Pause();
    void Resume();
    void Stop();
}
