using SpanishReader.Core.Abstractions;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
namespace SpanishReader.Services;

public sealed partial class WindowsReadAloudService : IReadAloudService, IDisposable
{
    private MediaPlayer? player;
    private bool paused;
    private int generation;
    private readonly VoiceInformation[] voices = SpeechSynthesizer.AllVoices.Where(v => v.Language.StartsWith("es-", StringComparison.OrdinalIgnoreCase)).OrderBy(v => v.Language).ThenBy(v => v.DisplayName).ToArray();
    public IReadOnlyList<SpeechVoice> Voices => voices.Select(v => new SpeechVoice(v.Id, $"{v.DisplayName} ({v.Language})")).ToArray();
    public string? DefaultVoiceId => SpeechSynthesizer.DefaultVoice?.Id;
    public event Action<SpokenWord?>? WordChanged;
    public async Task SpeakAsync(string text, string voiceId, double speed, CancellationToken cancellationToken)
    {
        var version = ++generation;
        using var synth = new SpeechSynthesizer();
        synth.Voice = voices.First(v => v.Id == voiceId);
        synth.Options.SpeakingRate = speed;
        synth.Options.IncludeWordBoundaryMetadata = true;
        using var stream = await synth.SynthesizeTextToStreamAsync(text).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Keep the native speech metadata separate from the audio source. This
        // also avoids desktop media pipelines waiting on speech metadata tracks
        // while opening an otherwise ordinary WAV stream.
        using var audio = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var input = stream.GetInputStreamAt(0))
            await Windows.Storage.Streams.RandomAccessStream.CopyAsync(input, audio);
        audio.Seek(0);
        cancellationToken.ThrowIfCancellationRequested();
        using var source = MediaSource.CreateFromStream(audio, stream.ContentType);
        var item = new MediaPlaybackItem(source);
        using var media = new MediaPlayer { AutoPlay = false };
        player = media;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Media callbacks are raised off-thread; generation checks also run after
        // dispatch so a stopped session can never repaint a new story.
        void Publish(SpokenWord? word) => App.DispatcherQueue.TryEnqueue(() =>
        {
            if (version == generation && !cancellationToken.IsCancellationRequested) WordChanged?.Invoke(word);
        });
        media.MediaEnded += (_, _) => completion.TrySetResult();
        media.MediaFailed += (_, _) => completion.TrySetException(new IOException("Windows audio playback failed."));
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        // Windows word cues have zero duration: CueExited can fire immediately
        // after CueEntered. Read their timestamps against the audio clock and
        // retain each word until the next starts, including while paused.
        var words = stream.TimedMetadataTracks
            .SelectMany(track => track.Cues.OfType<SpeechCue>())
            .Where(cue => cue.StartPositionInInput.HasValue && cue.EndPositionInInput.HasValue)
            .OrderBy(cue => cue.StartTime).ToArray();
        var timer = App.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(30);
        SpeechCue? previous = null;
        timer.Tick += (_, _) =>
        {
            var position = media.PlaybackSession.Position;
            var cue = words.LastOrDefault(word => word.StartTime <= position &&
                (word.Duration == TimeSpan.Zero || position < word.StartTime + word.Duration));
            if (ReferenceEquals(cue, previous)) return;
            previous = cue;
            if (cue is null) Publish(null);
            else if (cue.StartPositionInInput is int start && cue.EndPositionInInput is int end &&
                     start >= 0 && end >= start && end < text.Length)
                Publish(new(start, end - start + 1));
        };
        try
        {
            media.Source = item;
            if (!paused) media.Play();
            timer.Start();
            await completion.Task;
        }
        finally
        {
            timer.Stop();
            if (ReferenceEquals(player, media)) player = null;
            media.Pause();
            media.Source = null;
            Publish(null);
        }
    }
    public void Pause() { paused = true; player?.Pause(); }
    public void Resume() { paused = false; player?.Play(); }
    public void Stop() { generation++; paused = false; player?.Pause(); }
    public void Dispose() { Stop(); player?.Dispose(); }
}

