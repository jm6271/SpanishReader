using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;
using SpanishReader.Core.ViewModels;
namespace SpanishReader.Core.Tests;

public class ReadAloudTests
{
    [Fact]
    public async Task SelectedSentenceOffsetsPauseAndStopArePreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            var speech = new Speech();
            var vm = new ReadAloudViewModel(speech, new(path));
            StorySentence[] sentences = [new() { SpanishText = "Uno. " }, new() { SpanishText = "Dos." }];
            SpokenWord? word = null;
            vm.WordChanged += value => word = value;
            var play = vm.PlayAsync(sentences, sentences[1]);
            Assert.Equal("Dos.", Assert.Single(speech.Calls));
            speech.Emit(new(0, 3));
            Assert.Equal(new SpokenWord(5, 3), word);
            await vm.PlayAsync(sentences, null);
            Assert.True(speech.Paused);
            await vm.PlayAsync(sentences, null);
            Assert.False(speech.Paused);
            vm.Stop();
            await play;
            Assert.False(vm.IsActive);
            Assert.Null(word);
            speech.Emit(new(0, 3));
            Assert.Null(word);
            Assert.Equal(("es", 1d), new ReadAloudPreferenceStore(path).Load());
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
            var vm = new ReadAloudViewModel(speech, new(path));
            await vm.PlayAsync([new() { SpanishText = "¡Sí! " }, new() { SpanishText = "Otra." }], null);
            Assert.Equal(new[] { "¡Sí! ", "Otra." }, speech.Calls);
            Assert.False(vm.IsActive);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FailuresResetThePlayerAndAllowRetry()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".speech");
        try
        {
            var speech = new Speech { Fail = true };
            var vm = new ReadAloudViewModel(speech, new(path));
            StorySentence[] sentences = [new() { SpanishText = "Hola." }];
            await vm.PlayAsync(sentences, null);
            Assert.False(vm.IsActive);
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
            var vm = new ReadAloudViewModel(new Speech(), new(path));
            Assert.Equal("es", vm.Voice!.Id);
            Assert.Equal(1d, vm.Speed);
            vm.Speed = 0.75;
            Assert.Equal(("es", 0.75), new ReadAloudPreferenceStore(path).Load());
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("es", json.RootElement.GetProperty("voice").GetString());
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
            Assert.Equal((string.Empty, 1d), new ReadAloudPreferenceStore(path).Load());
        }
        finally { File.Delete(path); }
    }

    private sealed class Speech : IReadAloudService
    {
        public IReadOnlyList<SpeechVoice> Voices => [new("es", "Spanish")];
        public string? DefaultVoiceId => "es";
        public event Action<SpokenWord?>? WordChanged;
        public List<string> Calls { get; } = [];
        public bool Paused;
        public bool CompleteImmediately;
        public bool Fail;
        public void Emit(SpokenWord word) => WordChanged?.Invoke(word);
        public Task SpeakAsync(string text, string voice, double speed, CancellationToken token)
        {
            Calls.Add(text);
            if (Fail) throw new IOException();
            return CompleteImmediately ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token);
        }
        public void Pause() => Paused = true;
        public void Resume() => Paused = false;
        public void Stop() { }
    }
}
