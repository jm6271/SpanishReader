using CommunityToolkit.Mvvm.ComponentModel;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;
namespace SpanishReader.Core.ViewModels;

public partial class ReadAloudViewModel : ObservableObject
{
    private readonly IReadAloudService service;
    private readonly ReadAloudPreferenceStore preferences;
    private CancellationTokenSource? session;
    private bool paused;
    private bool initialized;
    public ReadAloudViewModel(IReadAloudService service, ReadAloudPreferenceStore preferences)
    {
        this.service = service;
        this.preferences = preferences;
        var saved = preferences.Load();
        Voice = Voices.FirstOrDefault(v => v.Id == saved.Voice) ?? Voices.FirstOrDefault(v => v.Id == service.DefaultVoiceId) ?? Voices.FirstOrDefault();
        Speed = saved.Speed;
        service.WordChanged += word =>
        {
            if (IsActive) WordChanged?.Invoke(word is null ? null : word with { Start = word.Start + offset });
        };
        initialized = true;
    }
    private int offset;
    public event Action<SpokenWord?>? WordChanged;
    public IReadOnlyList<SpeechVoice> Voices => service.Voices;
    public double[] Speeds => ReadAloudPreferenceStore.Speeds;
    [ObservableProperty] public partial SpeechVoice? Voice { get; set; }
    [ObservableProperty] public partial double Speed { get; set; } = 1;
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial string PlayLabel { get; set; } = "Play";
    [ObservableProperty] public partial string Error { get; set; } = string.Empty;
    public bool HasVoice => Voices.Count > 0;
    public bool CanConfigure => !IsActive;
    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(CanConfigure));

    partial void OnVoiceChanged(SpeechVoice? value) => SavePreferences();
    partial void OnSpeedChanged(double value) => SavePreferences();
    private void SavePreferences()
    {
        if (!initialized || Voice is null) return;
        try { preferences.Save(Voice.Id, Speed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Error = "Voice and speed could not be saved."; }
    }

    public async Task PlayAsync(IReadOnlyList<StorySentence> sentences, StorySentence? selected)
    {
        if (IsActive)
        {
            paused = !paused;
            if (paused) service.Pause(); else service.Resume();
            PlayLabel = paused ? "Play" : "Pause";
            return;
        }
        if (Voice is null) return;
        Error = string.Empty;
        try { preferences.Save(Voice.Id, Speed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = "Voice and speed could not be saved."; }
        var current = session = new CancellationTokenSource();
        IsActive = true;
        paused = false;
        PlayLabel = "Pause";
        offset = 0;
        bool started = selected is null;
        try
        {
            foreach (var sentence in sentences)
            {
                started |= ReferenceEquals(sentence, selected);
                if (started && !string.IsNullOrWhiteSpace(sentence.SpanishText))
                    await service.SpeakAsync(sentence.SpanishText, Voice.Id, Speed, current.Token);
                current.Token.ThrowIfCancellationRequested();
                offset += sentence.SpanishText.Length;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (ReferenceEquals(session, current)) Error = "Read aloud failed. Check your Windows speech voice and try again."; }
        finally
        {
            if (ReferenceEquals(session, current)) Stop();
            current.Dispose();
        }
    }
    public void Stop()
    {
        session?.Cancel();
        session = null;
        service.Stop();
        paused = false;
        IsActive = false;
        PlayLabel = "Play";
        WordChanged?.Invoke(null);
    }
}
