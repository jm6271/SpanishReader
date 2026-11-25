using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;
using SpanishReader.Core.ViewModels;
namespace SpanishReader.Core.Tests;

public class ReadAloudTests
{
    [Fact]
    public async Task RequiresAnInstalledModelAndKeepsLegacySpeed()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            File.WriteAllText(path, """{"voice":"old-windows-voice","speed":0.75}""");
            var speech = new Speech { IsAvailable = false, CompleteImmediately = true };
            using var vm = new ReadAloudViewModel(new Speech(), speech, new(path));
            vm.SelectedEngineIndex = 1;
            StorySentence[] sentences = [new() { SpanishText = "Hola." }];
            Assert.Equal(speech.DefaultVoiceId, vm.Voice!.Id);
            Assert.Equal(0.75, vm.Speed);
            Assert.False(vm.CanPlay);
            await vm.PlayAsync(sentences, null);
            Assert.Empty(speech.Calls);

            speech.SetAvailability(true);
            Assert.True(vm.CanPlay);
            await vm.PlayAsync(sentences, null);
            Assert.Single(speech.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SelectedSentenceHighlightsAtPlaybackStartAndStaysHighlightedWhenPaused()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            var speech = new Speech();
            using var vm = new ReadAloudViewModel(speech, new Speech(), new(path));
            StorySentence[] sentences = [new() { SpanishText = "Uno. " }, new() { SpanishText = "Dos." }];
            var play = vm.PlayAsync(sentences, sentences[1]);
            Assert.Equal("Dos.", Assert.Single(speech.Calls));
            Assert.False(vm.ShowPlaybackLoading);
            Assert.Null(vm.SpeakingSentence);
            var previousPlayback = speech.PlaybackStarted!;
            previousPlayback();
            Assert.Same(sentences[1], vm.SpeakingSentence);
            await vm.PlayAsync(sentences, null);
            Assert.True(speech.Paused);
            Assert.Same(sentences[1], vm.SpeakingSentence);
            await vm.PlayAsync(sentences, null);
            Assert.False(speech.Paused);
            vm.Stop();
            await play;
            Assert.False(vm.IsActive);
            Assert.Null(vm.SpeakingSentence);
            var nextPlay = vm.PlayAsync(sentences, sentences[0]);
            previousPlayback();
            Assert.Null(vm.SpeakingSentence);
            speech.PlaybackStarted!();
            Assert.Same(sentences[0], vm.SpeakingSentence);
            vm.Stop();
            await nextPlay;
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadsEverySentenceAndResetsAfterCompletion()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            var speech = new Speech { CompleteImmediately = true };
            using var vm = new ReadAloudViewModel(speech, new Speech(), new(path));
            StorySentence[] sentences = [new() { SpanishText = "¡Sí! " }, new() { SpanishText = "Otra." }];
            var highlights = new List<StorySentence?>();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.SpeakingSentence)) highlights.Add(vm.SpeakingSentence);
            };
            await vm.PlayAsync(sentences, null);
            Assert.Equal(new[] { "¡Sí! ", "Otra." }, speech.Calls);
            Assert.Equal([sentences[0], null, sentences[1], null], highlights);
            Assert.False(vm.IsActive);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PreparesOnlyTheNextSentenceWhileTheCurrentSentencePlays()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            var speech = new Speech();
            using var vm = new ReadAloudViewModel(speech, new Speech(), new(path));
            StorySentence[] sentences =
            [
                new() { SpanishText = "Uno." },
                new() { SpanishText = " " },
                new() { SpanishText = "Dos." },
                new() { SpanishText = "Tres." }
            ];
            var play = vm.PlayAsync(sentences, null);
            speech.PlaybackStarted!();
            Assert.Same(sentences[0], vm.SpeakingSentence);
            Assert.Equal(["Uno.", "Dos."], speech.Calls);
            Assert.Equal(["Uno."], speech.Played);

            speech.CompleteImmediately = true;
            speech.FinishPlayback();
            await play.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["Uno.", "Dos.", "Tres."], speech.Played);
            Assert.Null(vm.SpeakingSentence);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task StopCancelsTheNextSentenceBeforeItCanPlay()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            var speech = new Speech { BlockSynthesisFor = "Dos." };
            using var vm = new ReadAloudViewModel(speech, new Speech(), new(path));
            StorySentence[] sentences = [new() { SpanishText = "Uno." }, new() { SpanishText = "Dos." }];
            var play = vm.PlayAsync(sentences, null);
            Assert.Equal(["Uno.", "Dos."], speech.Calls);

            vm.Stop();
            await play.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(speech.PrefetchCancellation.IsCancellationRequested);
            Assert.Equal(["Uno."], speech.Played);
            Assert.Empty(vm.Error);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task FailuresResetThePlayerAndAllowRetry(int engineIndex)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            var speech = new Speech { Fail = true };
            using var vm = new ReadAloudViewModel(speech, speech, new(path));
            vm.SelectedEngineIndex = engineIndex;
            StorySentence[] sentences = [new() { SpanishText = "Hola." }];
            await vm.PlayAsync(sentences, null);
            Assert.False(vm.IsActive);
            Assert.False(vm.ShowPlaybackLoading);
            Assert.NotEmpty(vm.Error);
            speech.Fail = false;
            speech.CompleteImmediately = true;
            await vm.PlayAsync(sentences, null);
            Assert.Empty(vm.Error);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidPreferencesFallBackAndChangesPersistImmediately()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            File.WriteAllText(path, "{ invalid JSON");
            using var vm = new ReadAloudViewModel(new Speech(), new Speech(), new(path));
            Assert.Equal("es", vm.Voice!.Id);
            Assert.Equal(1d, vm.Speed);
            vm.Speed = 0.75;
            Assert.Equal(new ReadAloudPreferences(Speed: 0.75), new ReadAloudPreferenceStore(path).Load());
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("windows", json.RootElement.GetProperty("engine").GetString());
            Assert.Equal(0.75, json.RootElement.GetProperty("speed").GetDouble());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"voice\":null,\"speed\":1}")]
    [InlineData("{\"voice\":\"es\",\"speed\":99}")]
    public void InvalidJsonPreferencesUseDefaults(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, json);
            var preferences = new ReadAloudPreferenceStore(path).Load();
            Assert.Equal(ReadAloudEngine.Windows, preferences.Engine);
            Assert.Equal(1, preferences.Speed);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("old-windows-voice", "old-windows-voice", null)]
    [InlineData("em_alex", null, "em_alex")]
    public void OldPreferencesKeepVoiceAndSpeedAndDefaultToWindows(string oldVoice, string? windowsVoice, string? kokoroVoice)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, $$"""{"engine":"unknown","voice":"{{oldVoice}}","speed":1.25}""");
            Assert.Equal(new ReadAloudPreferences(ReadAloudEngine.Windows, windowsVoice, kokoroVoice, 1.25),
                new ReadAloudPreferenceStore(path).Load());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RoutesPlaybackAndRestoresEachEngineVoiceAcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var windows = new Speech { CompleteImmediately = true, Voices = [new("win", "Win"), new("win2", "Win 2")] };
            var kokoro = new Speech { CompleteImmediately = true, Voices = [new("ko", "Ko"), new("ko2", "Ko 2")] };
            var store = new ReadAloudPreferenceStore(path);
            StorySentence[] sentences = [new() { SpanishText = "Hola." }];
            using (var vm = new ReadAloudViewModel(windows, kokoro, store))
            {
                Assert.Equal(ReadAloudEngine.Windows, vm.Engine);
                vm.Voice = vm.Voices[1];
                await vm.PlayAsync(sentences, null);
                Assert.Single(windows.Played);
                Assert.Empty(kokoro.Played);
                vm.SelectedEngineIndex = 1;
                vm.Voice = vm.Voices[1];
                vm.Speed = 1.25;
                await vm.PlayAsync(sentences, null);
                Assert.Single(kokoro.Played);
                Assert.Single(windows.Played);
            }
            using var restarted = new ReadAloudViewModel(windows, kokoro, store);
            Assert.Equal(ReadAloudEngine.Kokoro, restarted.Engine);
            Assert.Equal("ko2", restarted.Voice!.Id);
            restarted.SelectedEngineIndex = 0;
            Assert.Equal("win2", restarted.Voice!.Id);
            Assert.Equal(1.25, restarted.Speed);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SwitchingEnginesCancelsPlaybackAndIgnoresLateCallbacks()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var windows = new Speech { BlockSynthesisFor = "Dos." };
            var kokoro = new Speech { IsAvailable = false };
            using var vm = new ReadAloudViewModel(windows, kokoro, new(path));
            StorySentence[] sentences = [new() { SpanishText = "Uno." }, new() { SpanishText = "Dos." }];
            var playing = vm.PlayAsync(sentences, null);
            var started = windows.PlaybackStarted!;
            started();
            Assert.Same(sentences[0], vm.SpeakingSentence);

            vm.SelectedEngineIndex = 1;
            started();
            await playing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(windows.PrefetchCancellation.IsCancellationRequested);
            Assert.Null(vm.SpeakingSentence);
            Assert.False(vm.IsActive);
            Assert.False(vm.CanPlay);
            await vm.PlayAsync(sentences, null);
            Assert.Empty(kokoro.Calls);
            Assert.Single(windows.Played);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task KokoroShowsLoadingUntilAudioStartsAndDoesNotShowItForPrefetch()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var kokoro = new Speech { BlockSynthesisFor = "Dos." };
            using var vm = new ReadAloudViewModel(new Speech(), kokoro, new(path));
            vm.SelectedEngineIndex = 1;
            var playing = vm.PlayAsync([new() { SpanishText = "Uno." }, new() { SpanishText = "Dos." }], null);
            Assert.True(vm.ShowPlaybackLoading);
            await vm.PlayAsync([], null);
            Assert.False(vm.ShowPlaybackLoading);
            await vm.PlayAsync([], null);
            Assert.True(vm.ShowPlaybackLoading);
            kokoro.PlaybackStarted!();
            Assert.False(vm.ShowPlaybackLoading);
            vm.Stop();
            await playing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(vm.ShowPlaybackLoading);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task StoppingKokoroWhilePreparingAudioClearsLoading()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var kokoro = new Speech { BlockSynthesisFor = "Uno." };
            using var vm = new ReadAloudViewModel(new Speech(), kokoro, new(path));
            vm.SelectedEngineIndex = 1;
            var playing = vm.PlayAsync([new() { SpanishText = "Uno." }], null);
            Assert.True(vm.ShowPlaybackLoading);
            vm.Stop();
            Assert.False(vm.ShowPlaybackLoading);
            await playing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task WindowsWithoutSpanishVoicesDoesNotUseKokoro()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var windows = new Speech { Voices = [] };
        var kokoro = new Speech { CompleteImmediately = true };
        using var vm = new ReadAloudViewModel(windows, kokoro, new(path));

        Assert.False(vm.CanPlay);
        Assert.Contains("Windows Settings", vm.UnavailableMessage);
        await vm.PlayAsync([new() { SpanishText = "Hola." }], null);
        Assert.Empty(windows.Calls);
        Assert.Empty(kokoro.Calls);
    }

    [Fact]
    public void FailedSaveKeepsTheSelectedEngineForTheSession()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var vm = new ReadAloudViewModel(new Speech(), new Speech(), new(Path.Combine(path, "speech.json")));
            vm.SelectedEngineIndex = 1;
            Assert.Equal(ReadAloudEngine.Kokoro, vm.Engine);
            Assert.NotEmpty(vm.PreferenceError);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReaderPreferenceWarningClearsAfterWritesRecover()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "speech.json");
            Directory.CreateDirectory(path + ".tmp");
            var windows = new Speech { Voices = [new("es-1", "First"), new("es-2", "Second")] };
            var store = new ReadAloudPreferenceStore(path);
            using var vm = new ReadAloudViewModel(windows, new Speech(), store);
            var voice = windows.Voices[1];

            vm.Voice = voice;
            Assert.Equal(voice, vm.Voice);
            Assert.NotEmpty(vm.PreferenceError);

            vm.Speed = 1.25;
            Assert.Equal(1.25, vm.Speed);
            Assert.NotEmpty(vm.PreferenceError);

            Directory.Delete(path + ".tmp");
            vm.Speed = 1.5;
            Assert.Empty(vm.PreferenceError);
            Assert.Equal(voice.Id, store.Load().WindowsVoice);
            Assert.Equal(1.5, store.Load().Speed);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class Speech : IReadAloudService
    {
        public IReadOnlyList<SpeechVoice> Voices { get; init; } = [new("es", "Spanish")];
        public string? DefaultVoiceId => Voices.FirstOrDefault()?.Id;
        public bool IsAvailable { get; set; } = true;
        public event Action? AvailabilityChanged;
        public Action? PlaybackStarted { get; private set; }
        public List<string> Calls { get; } = [];
        public List<string> Played { get; } = [];
        public string? BlockSynthesisFor { get; init; }
        public CancellationToken PrefetchCancellation { get; private set; }
        private TaskCompletionSource? playback;
        public bool Paused;
        public bool CompleteImmediately;
        public bool Fail;
        public void SetAvailability(bool available)
        {
            IsAvailable = available;
            AvailabilityChanged?.Invoke();
        }
        public async Task<PreparedReadAloudSentence> PrepareAsync(string text, string voice, double speed,
            CancellationToken token)
        {
            Calls.Add(text);
            if (text == BlockSynthesisFor)
            {
                PrefetchCancellation = token;
                await Task.Delay(Timeout.Infinite, token);
            }
            return (started, cancellation) => PlayAsync(text, started, cancellation);
        }
        private Task PlayAsync(string text, Action playbackStarted, CancellationToken token)
        {
            Played.Add(text);
            if (Fail) throw new IOException();
            PlaybackStarted = playbackStarted;
            if (!CompleteImmediately)
            {
                playback = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return playback.Task.WaitAsync(token);
            }
            playbackStarted();
            return Task.CompletedTask;
        }
        public void FinishPlayback() => playback!.SetResult();
        public void Pause() => Paused = true;
        public void Resume() => Paused = false;
        public void Stop() { }
    }
}
