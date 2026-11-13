using SpanishReader.Core.Services;

namespace SpanishReader.Core.Tests;

public sealed class LLMServiceTests
{
    [Fact]
    public void Constructor_RequiresAnAbsoluteGgufPath()
    {
        Assert.Throws<ArgumentException>(() => new LlamaSharpLLMService("model.gguf"));
        Assert.Throws<ArgumentException>(() => new LlamaSharpLLMService(
            Path.Combine(Path.GetTempPath(), "model.bin")));
    }

    [Fact]
    public async Task ChatAsync_RejectsMissingModel()
    {
        using var service = new LlamaSharpLLMService(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.gguf"));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.ChatAsync("System prompt", "User prompt"));
    }

    [Fact]
    public async Task ChatAsync_RespectsCancellationBeforeLoading()
    {
        using var service = new LlamaSharpLLMService(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.gguf"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ChatAsync("System prompt", "User prompt", cancellation.Token));
    }
}
