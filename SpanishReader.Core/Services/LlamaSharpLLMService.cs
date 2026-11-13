using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.Services;

/// <summary>Owns a lazily loaded GGUF model and runs independent chat requests.</summary>
public sealed class LlamaSharpLLMService : ILLMService, IDisposable
{
    private string? _modelPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LLamaWeights? _weights;
    private ModelParams? _modelParams;
    private bool _disposed;

    public LlamaSharpLLMService(string? modelPath = null)
    {
        if (modelPath is null) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!Path.IsPathFullyQualified(modelPath))
            throw new ArgumentException("An absolute model path is required.", nameof(modelPath));
        if (!string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A .gguf model file is required.", nameof(modelPath));
        _modelPath = modelPath;
    }

    public string? ModelPath => _modelPath;

    /// <summary>Waits for inference before switching models or releasing a file for deletion.</summary>
    public async Task SetModelPathAsync(string? modelPath)
    {
        if (modelPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
            if (!Path.IsPathFullyQualified(modelPath) || !string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An absolute .gguf model path is required.", nameof(modelPath));
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_modelPath == modelPath) return;
            _weights?.Dispose();
            _weights = null;
            _modelParams = null;
            _modelPath = modelPath;
        }
        finally { _gate.Release(); }
    }

    public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        // Serialize native initialization and inference so disposal cannot free
        // the weights while a request is running.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_modelPath is null)
                throw new FileNotFoundException("No GGUF model is installed.");
            if (_weights is null)
            {
                if (!File.Exists(_modelPath))
                    throw new FileNotFoundException("The GGUF model file is missing.", _modelPath);

                // LLamaSharp tries CUDA, Vulkan, then CPU. Its Windows ARM64
                // package provides CPU binaries only.
                NativeLibraryConfig.All.WithCuda(true).WithVulkan(true).WithAutoFallback(true);
                _modelParams = new ModelParams(_modelPath)
                {
                    ContextSize = 4096,
                    GpuLayerCount = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                        System.Runtime.InteropServices.Architecture.Arm64 ? 0 : 99
                };
                _weights = await LLamaWeights.LoadFromFileAsync(_modelParams, cancellationToken).ConfigureAwait(false);
            }

            // A fresh executor prevents one call's prompts from entering the next.
            var executor = new StatelessExecutor(_weights, _modelParams!)
            {
                ApplyTemplate = true,
                SystemMessage = systemPrompt
            };
            var response = new StringBuilder();
            var options = new InferenceParams { MaxTokens = 512 };
            await foreach (var chunk in executor.InferAsync(userPrompt, options, cancellationToken).ConfigureAwait(false))
                response.Append(chunk);
            return response.ToString().Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            _weights?.Dispose();
            _weights = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
