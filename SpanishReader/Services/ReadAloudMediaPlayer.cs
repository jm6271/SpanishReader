using Windows.Media.Core;
using Windows.Media.Playback;

namespace SpanishReader.Services;

internal sealed partial class ReadAloudMediaPlayer
{
    private MediaPlayer? _player;
    private TaskCompletionSource? _completion;
    private bool _paused;

    public async Task PlayAsync(Stream audio, string contentType, Action? playbackStarted,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = audio.AsRandomAccessStream();
        using var source = MediaSource.CreateFromStream(stream, contentType);
        using var media = new MediaPlayer { AutoPlay = false };
        _player = media;

        var completion = _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        media.MediaEnded += (_, _) => completion.TrySetResult();
        media.MediaFailed += (_, args) => completion.TrySetException(
            new IOException($"Audio playback failed: {args.ErrorMessage}"));
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        var playback = media.PlaybackSession;
        if (playbackStarted is not null)
            playback.PlaybackStateChanged += OnPlaybackStateChanged;

        void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            if (sender.PlaybackState != MediaPlaybackState.Playing) return;
            App.DispatcherQueue.TryEnqueue(() =>
            {
                // Events can arrive after Stop or from a player that was replaced.
                if (!ReferenceEquals(_player, media) || completion.Task.IsCompleted || _paused) return;
                var started = playbackStarted;
                playbackStarted = null;
                started?.Invoke();
            });
        }

        try
        {
            media.Source = source;
            if (!_paused) media.Play();
            await completion.Task;
        }
        finally
        {
            playback.PlaybackStateChanged -= OnPlaybackStateChanged;
            if (ReferenceEquals(_player, media))
            {
                _player = null;
                _completion = null;
            }
            media.Pause();
            media.Source = null;
        }
    }

    public void Pause() { _paused = true; _player?.Pause(); }
    public void Resume() { _paused = false; _player?.Play(); }
    public void Stop()
    {
        _paused = false;
        var player = _player;
        _player = null;
        _completion?.TrySetCanceled();
        _completion = null;
        player?.Pause();
    }
}
