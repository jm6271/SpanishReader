using System.ComponentModel;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Services;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Services;

public sealed partial class KokoroReadAloudService : IReadAloudService, IDisposable
{
    private readonly KokoroSpeechSynthesizer _synthesizer;
    private readonly ModelDownloadViewModel _modelSettings;
    private readonly ReadAloudMediaPlayer _player = new();

    public KokoroReadAloudService(KokoroSpeechSynthesizer synthesizer, ModelDownloadViewModel modelSettings)
    {
        _synthesizer = synthesizer;
        _modelSettings = modelSettings;
        _modelSettings.PropertyChanged += OnModelSettingsChanged;
    }

    public IReadOnlyList<SpeechVoice> Voices => KokoroSpeechSynthesizer.SpanishVoices;
    public string DefaultVoiceId => KokoroSpeechSynthesizer.DefaultVoiceId;
    public bool IsAvailable => _modelSettings.IsInstalled && !_modelSettings.IsBusy;
    public event Action? AvailabilityChanged;

    private void OnModelSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ModelDownloadViewModel.IsInstalled) or nameof(ModelDownloadViewModel.IsBusy))
            AvailabilityChanged?.Invoke();
    }

    public async Task<PreparedReadAloudSentence> PrepareAsync(string text, string voiceId, double speed,
        CancellationToken cancellationToken)
    {
        var chunks = await _synthesizer.PrepareWavClipsAsync(text, voiceId, speed, cancellationToken);
        return (started, token) => PlayAsync(chunks, started, token);
    }

    private async Task PlayAsync(IReadOnlyList<byte[]> chunks, Action playbackStarted,
        CancellationToken cancellationToken)
    {
        Action? notify = playbackStarted;
        foreach (var clip in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var wave = new MemoryStream(clip, writable: false);
            await _player.PlayAsync(wave, "audio/wav", notify is null ? null : ReportPlaybackStarted, cancellationToken);
        }

        void ReportPlaybackStarted()
        {
            var started = notify;
            notify = null;
            started?.Invoke();
        }
    }

    public void Pause() => _player.Pause();
    public void Resume() => _player.Resume();
    public void Stop() => _player.Stop();

    public void Dispose()
    {
        _modelSettings.PropertyChanged -= OnModelSettingsChanged;
        Stop();
    }
}
