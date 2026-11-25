using CommunityToolkit.Mvvm.ComponentModel;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;
namespace SpanishReader.Core.ViewModels;

public partial class ReadAloudViewModel : ObservableObject, IDisposable
{
    private readonly IReadAloudService windows;
    private readonly IReadAloudService kokoro;
    private readonly ReadAloudPreferenceStore preferences;
    private ReadAloudPreferences saved;
    private CancellationTokenSource? session;
    private bool paused;
    private bool updatingVoice;
    public ReadAloudViewModel(IReadAloudService windows, IReadAloudService kokoro, ReadAloudPreferenceStore preferences)
    {
        this.windows = windows;
        this.kokoro = kokoro;
        this.preferences = preferences;
        saved = preferences.Load();
        windows.AvailabilityChanged += OnWindowsAvailabilityChanged;
        kokoro.AvailabilityChanged += OnKokoroAvailabilityChanged;
        RefreshVoice();
    }

    private IReadAloudService Service => Engine == ReadAloudEngine.Kokoro ? kokoro : windows;
    public ReadAloudEngine Engine => saved.Engine;
    public int SelectedEngineIndex
    {
        get => Engine == ReadAloudEngine.Kokoro ? 1 : 0;
        set
        {
            if (value is not (0 or 1) || value == SelectedEngineIndex) return;
            Stop();
            Error = string.Empty;
            saved = saved with { Engine = value == 1 ? ReadAloudEngine.Kokoro : ReadAloudEngine.Windows };
            RefreshVoice();
            SavePreferences();
            OnPropertyChanged(nameof(Engine));
            OnPropertyChanged(nameof(SelectedEngineIndex));
            NotifyAvailability();
        }
    }

    private void RefreshVoice()
    {
        var voiceId = Engine == ReadAloudEngine.Kokoro ? saved.KokoroVoice : saved.WindowsVoice;
        var voices = Voices;
        // Updating ItemsSource can clear the ComboBox selection before the new
        // voice is assigned. Keep binding updates out of preference writes.
        updatingVoice = true;
        try
        {
            Voice = null;
            OnPropertyChanged(nameof(Voices));
            Voice = voices.FirstOrDefault(v => v.Id == voiceId)
                ?? voices.FirstOrDefault(v => v.Id == Service.DefaultVoiceId)
                ?? voices.FirstOrDefault();
        }
        finally { updatingVoice = false; }
    }

    private void OnWindowsAvailabilityChanged()
    {
        if (Engine == ReadAloudEngine.Windows) NotifyAvailability();
    }

    private void OnKokoroAvailabilityChanged()
    {
        if (Engine == ReadAloudEngine.Kokoro) NotifyAvailability();
    }

    private void NotifyAvailability()
    {
        if (!CanPlay && IsActive) Stop();
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(UnavailableMessage));
    }
    [ObservableProperty] public partial StorySentence? SpeakingSentence { get; private set; }
    public IReadOnlyList<SpeechVoice> Voices => Service.Voices;
    public double[] Speeds => ReadAloudPreferenceStore.Speeds;
    [ObservableProperty] public partial SpeechVoice? Voice { get; set; }
    public double Speed
    {
        get => saved.Speed;
        set
        {
            if (value == saved.Speed || !Speeds.Contains(value)) return;
            saved = saved with { Speed = value };
            SavePreferences();
            OnPropertyChanged(nameof(Speed));
        }
    }
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaybackLoading))]
    public partial string PlayLabel { get; set; } = "Play";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaybackLoading))]
    public partial bool IsStartingKokoro { get; private set; }
    public bool ShowPlaybackLoading => IsStartingKokoro && !paused;
    [ObservableProperty] public partial string Error { get; set; } = string.Empty;
    [ObservableProperty] public partial string PreferenceError { get; private set; } = string.Empty;
    public bool CanPlay => Service.IsAvailable && Voices.Count > 0;
    public string UnavailableMessage => Engine == ReadAloudEngine.Kokoro
        ? "To use Kokoro, download its model in Settings or wait for the model update to finish."
        : "Install a Spanish speech voice in Windows Settings (Time & language → Speech), then restart SpanishReader.";
    public bool CanConfigure => !IsActive;
    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(CanConfigure));

    partial void OnVoiceChanged(SpeechVoice? value)
    {
        if (updatingVoice || value is null || !Voices.Contains(value)) return;
        saved = Engine == ReadAloudEngine.Kokoro
            ? saved with { KokoroVoice = value.Id }
            : saved with { WindowsVoice = value.Id };
        SavePreferences();
    }

    private void SavePreferences()
    {
        try
        {
            preferences.Save(saved);
            PreferenceError = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { PreferenceError = "Read-aloud preferences could not be saved. Changes apply for this session."; }
    }

    public async Task PlayAsync(IReadOnlyList<StorySentence> sentences, StorySentence? selected)
    {
        var service = Service;
        if (IsActive)
        {
            paused = !paused;
            if (paused) service.Pause(); else service.Resume();
            PlayLabel = paused ? "Play" : "Pause";
            return;
        }
        if (!CanPlay || Voice is not { } voice) return;
        var speed = Speed;
        Error = string.Empty;
        var current = session = new CancellationTokenSource();
        IsActive = true;
        paused = false;
        IsStartingKokoro = Engine == ReadAloudEngine.Kokoro;
        PlayLabel = "Pause";
        using var remaining = sentences
            .SkipWhile(sentence => selected is not null && !ReferenceEquals(sentence, selected))
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence.SpanishText))
            .GetEnumerator();
        Task<PreparedReadAloudSentence>? pending = null;
        try
        {
            pending = PrepareNext();
            while (pending is { } synthesis)
            {
                var sentence = remaining.Current;
                pending = null;
                var audio = await synthesis;
                current.Token.ThrowIfCancellationRequested();
                // Keep at most one sentence ahead. Only playback marks a sentence
                // as spoken, even if the engine has already prepared the next one.
                pending = PrepareNext();
                await audio(() =>
                {
                    if (ReferenceEquals(session, current))
                    {
                        IsStartingKokoro = false;
                        SpeakingSentence = sentence;
                    }
                }, current.Token);
                current.Token.ThrowIfCancellationRequested();
                SpeakingSentence = null;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Read aloud failed: {0}", ex);
            if (ReferenceEquals(session, current))
                Error = Engine == ReadAloudEngine.Kokoro
                    ? "Read aloud failed. Try again or reinstall the Kokoro model in Settings."
                    : "Read aloud failed. Check your Windows speech voice and try again.";
        }
        finally
        {
            if (ReferenceEquals(session, current)) Stop();
            // Stop cancels pending inference. Observe its result before disposing
            // the token, even when playback failed before reaching that sentence.
            if (pending is not null)
            {
                try { await pending; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { System.Diagnostics.Trace.TraceError("Read-aloud prefetch failed: {0}", ex); }
            }
            current.Dispose();
        }

        Task<PreparedReadAloudSentence>? PrepareNext() => remaining.MoveNext()
            ? service.PrepareAsync(remaining.Current.SpanishText, voice.Id, speed, current.Token)
            : null;
    }
    public void Stop()
    {
        session?.Cancel();
        session = null;
        Service.Stop();
        IsStartingKokoro = false;
        paused = false;
        IsActive = false;
        PlayLabel = "Play";
        SpeakingSentence = null;
    }

    public void Dispose()
    {
        windows.AvailabilityChanged -= OnWindowsAvailabilityChanged;
        kokoro.AvailabilityChanged -= OnKokoroAvailabilityChanged;
        Stop();
    }
}
