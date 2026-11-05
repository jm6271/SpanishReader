namespace SpanishReader.Core.Abstractions;

public sealed record SpeechVoice(string Id, string Name)
{
    public override string ToString() => Name;
}
public sealed record SpokenWord(int Start, int Length);

public interface IReadAloudService
{
    IReadOnlyList<SpeechVoice> Voices { get; }
    string? DefaultVoiceId { get; }
    event Action<SpokenWord?>? WordChanged;
    Task SpeakAsync(string text, string voiceId, double speed, CancellationToken cancellationToken);
    void Pause();
    void Resume();
    void Stop();
}
