using SpanishReader.Core.Services;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.ViewModels;
using Xunit.Abstractions;

namespace SpanishReader.Core.Tests;

public class TranslationTests(ITestOutputHelper output)
{
    private static string ConfigPath => Path.Combine(
        AppContext.BaseDirectory, "TranslationModels", "esen", "config.yml");

    [Fact]
    public async Task BundledModelTranslatesSpanishAndHandlesConcurrentRequests()
    {
        using var service = new BergamotTranslationService(ConfigPath);

        // Repeated and concurrent requests share one engine. Assert English meaning
        // rather than punctuation, which can change when the pinned model is updated.
        var translations = await Task.WhenAll(
            service.TranslateAsync("Hola, mundo."),
            service.TranslateAsync("El gato está durmiendo."),
            service.TranslateAsync("Hola, mundo."));

        foreach (var translation in translations)
            output.WriteLine(translation);

        Assert.Contains("hello", translations[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("world", translations[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cat", translations[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sleep", translations[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(translations[0], translations[2]);

        service.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.TranslateAsync("Hola."));
    }

    [Fact]
    public async Task ImportPersistsBundledEngineTranslationsAcrossRepositoryInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpanishReader.Tests", Guid.NewGuid().ToString("N"));
        var database = Path.Combine(directory, "Stories.db");
        try
        {
            using var translation = new BergamotTranslationService(ConfigPath);
            var navigation = new ImportNavigation();
            var import = new ImportPageViewModel(new SqliteStoryRepository(database), navigation, translation)
            {
                Title = "Prueba", StoryText = "  Hola, mundo.\n\nEl gato está durmiendo.  "
            };
            var text = import.StoryText;
            await import.SaveCommand.ExecuteAsync(null);
            Assert.Empty(import.ErrorMessage);
            Assert.NotNull(navigation.Saved);
            var loaded = Assert.Single(await new SqliteStoryRepository(database).GetAllAsync());
            Assert.Equal(navigation.Saved.Id, loaded.Id);
            Assert.Equal(text, string.Concat(loaded.Sentences.Select(sentence => sentence.SpanishText)));
            Assert.Equal(2, loaded.Sentences.Count);
            Assert.Contains("hello", loaded.Sentences[0].EnglishText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("world", loaded.Sentences[0].EnglishText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cat", loaded.Sentences[1].EnglishText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sleep", loaded.Sentences[1].EnglishText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ImportNavigation : INavigationService
    {
        public Story? Saved { get; private set; }
        public void Navigate<T>(object? parameter = null) where T : class => Saved = Assert.IsType<Story>(parameter);
    }

    [Fact]
    public async Task CancelledRequestDoesNotLoadMissingModel()
    {
        using var service = new BergamotTranslationService(Path.Combine(
            Path.GetTempPath(), Guid.NewGuid().ToString(), "config.yml"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TranslateAsync("Hola.", cancellation.Token));
    }

    [Fact]
    public async Task MissingConfigurationReportsItsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "config.yml");
        using var service = new BergamotTranslationService(path);

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => service.TranslateAsync("Hola."));
        Assert.Equal(path, error.FileName);
    }
}
