using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.Services;

/// <summary>Owns a lazily loaded GGUF model and runs independent chat requests.</summary>
public sealed class LlamaSharpLLMService : ILLMService, IDisposable
{
    private const int ContextTokens = 2048;
    private const int MaxResponseTokens = 512;
    private static readonly TimeSpan ModelIdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly Lock NativeInitializationLock = new();
    private static bool _nativeConfigured;

    private string? _modelPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LLamaWeights? _weights;
    private ModelParams? _modelParams;
    private Timer? _idleTimer;
    private long _idleGeneration;
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
            ReleaseModel();
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
            CancelIdleUnload();
            if (_modelPath is null)
                throw new FileNotFoundException("No GGUF model is installed.");
            if (_weights is null)
            {
                if (!File.Exists(_modelPath))
                    throw new FileNotFoundException("The GGUF model file is missing.", _modelPath);

                LlamaDiagnostics.Write("Loading model.");
                EnsureNativeConfigured();
                _modelParams = new ModelParams(_modelPath)
                {
                    ContextSize = ContextTokens,
                    GpuLayerCount = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                        System.Runtime.InteropServices.Architecture.Arm64 ? 0 : 99,
                    BatchSize = 128,
                    UBatchSize = 128
                };
                _weights = await LLamaWeights.LoadFromFileAsync(_modelParams, cancellationToken).ConfigureAwait(false);
                LlamaDiagnostics.Write("Model loaded.");
            }

            // Keep the prompt and response within the configured context budget.
            // This is a size guard, not a recovery mechanism for native crashes.
            LlamaDiagnostics.Write("Formatting and tokenizing prompt.");
            var template = new LLamaTemplate(_weights);
            template.Add("system", systemPrompt).Add("user", userPrompt);
            var formattedPrompt = Encoding.UTF8.GetString(template.Apply());
            var promptTokens = _weights.Tokenize(formattedPrompt, true, true, Encoding.UTF8).Length;
            LlamaDiagnostics.Write($"Prompt tokens={promptTokens}; context={ContextTokens}; response limit={MaxResponseTokens}.");
            if (promptTokens > ContextTokens - MaxResponseTokens - 32)
                throw new ArgumentException("The prompt is too long for the language model context.", nameof(userPrompt));

            LlamaDiagnostics.Write("Creating stateless executor.");
            var executor = new StatelessExecutor(_weights, _modelParams!)
            {
                ApplyTemplate = true,
                SystemMessage = systemPrompt
            };
            var response = new StringBuilder();
            var options = new InferenceParams { MaxTokens = MaxResponseTokens };
            LlamaDiagnostics.Write("Starting inference.");
            await foreach (var chunk in executor.InferAsync(userPrompt, options, cancellationToken).ConfigureAwait(false))
            {
                if (response.Length == 0) LlamaDiagnostics.Write("Received first output chunk.");
                response.Append(chunk);
            }
            LlamaDiagnostics.Write("Inference completed.");
            return response.ToString().Trim();
        }
        catch (Exception ex)
        {
            LlamaDiagnostics.Write($"Managed failure: {ex}");
            throw;
        }
        finally
        {
            try
            {
                if (!_disposed && _weights is not null)
                {
                    var generation = _idleGeneration;
                    _idleTimer = new Timer(_ => UnloadIfIdle(generation), null,
                        ModelIdleTimeout, Timeout.InfiniteTimeSpan);
                }
            }
            finally { _gate.Release(); }
        }
    }

    private static void EnsureNativeConfigured()
    {
        // The backend is process-wide and LLamaSharp locks its configuration
        // once the native library loads. Reloading weights reuses that backend.
        lock (NativeInitializationLock)
        {
            if (_nativeConfigured)
                return;

            var config = NativeLibraryConfig.LLama;
            if (!config.LibraryHasLoaded)
            {
                config.WithCuda(false).WithVulkan(true).WithAutoFallback(true)
                    .WithLogCallback(LlamaDiagnostics.Callback);
            }
            NativeLogConfig.llama_log_set(LlamaDiagnostics.Callback);
            _nativeConfigured = true;
        }
    }

    private void UnloadIfIdle(long generation)
    {
        _gate.Wait();
        try
        {
            // A callback already queued when a timer is disposed can still run.
            // Check the generation under the same gate used for inference.
            if (_disposed || generation != _idleGeneration)
                return;

            ReleaseModel();
            LlamaDiagnostics.Write("Model unloaded after five minutes of inactivity.");
        }
        catch (Exception ex)
        {
            LlamaDiagnostics.Write($"Failed to unload idle model: {ex}");
        }
        finally { _gate.Release(); }
    }

    // All timer and model state changes are protected by _gate.
    private void CancelIdleUnload()
    {
        _idleGeneration++;
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    private void ReleaseModel()
    {
        CancelIdleUnload();
        var weights = _weights;
        _weights = null;
        _modelParams = null;
        weights?.Dispose();
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            ReleaseModel();
        }
        finally
        {
            _gate.Release();
        }
    }
}
