using SpanishReader.Core.Services;
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
