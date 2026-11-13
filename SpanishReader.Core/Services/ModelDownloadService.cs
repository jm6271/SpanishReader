using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;

namespace SpanishReader.Core.Services;

/// <summary>Downloads the one supported model without exposing an incomplete GGUF file.</summary>
public sealed class ModelDownloadService(HttpClient client, string modelPath, string? expectedSha256 = null)
{
    public const string FileName = "Qwen3-1.7B-Q8_0.gguf";
    public const string Url = "https://huggingface.co/Qwen/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q8_0.gguf";
    public const string ExpectedSha256 = "061b54daade076b5d3362dac252678d17da8c68f07560be70818cace6590cb1a";

    private readonly SemaphoreSlim _gate = new(1, 1);
    public string ModelPath { get; } = Path.GetFullPath(modelPath);
    public string PartialPath => ModelPath + ".partial";
    public bool IsInstalled => File.Exists(ModelPath);

    public async Task DownloadAsync(IProgress<(long Received, long? Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsInstalled) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            long offset = File.Exists(PartialPath) ? new FileInfo(PartialPath).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            // A server may ignore Range. Restart rather than append a full response.
            bool resume = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent
                && response.Content.Headers.ContentRange?.From == offset;
            if (response.StatusCode == HttpStatusCode.PartialContent && !resume)
                throw new IOException("The server returned an invalid partial download.");
            if (!resume) offset = 0;
            long? total = response.Content.Headers.ContentRange?.Length
                ?? (response.Content.Headers.ContentLength is long length ? length + offset : null);
            await using (var destination = new FileStream(PartialPath, resume ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, true))
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                byte[] buffer = new byte[81920];
                long received = offset;
                progress?.Report((received, total));
                long lastReport = Stopwatch.GetTimestamp();
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    received += count;
                    // Progress<T> queues callbacks on the WinUI dispatcher. Reporting
                    // each small read can bury user input under thousands of updates.
                    long now = Stopwatch.GetTimestamp();
                    if (Stopwatch.GetElapsedTime(lastReport, now) >= TimeSpan.FromMilliseconds(250))
                    {
                        progress?.Report((received, total));
                        lastReport = now;
                    }
                }
                progress?.Report((received, total));
                if (total is long expected && received != expected)
                    throw new IOException("The model download ended before all bytes arrived.");
            }
            byte[] hash;
            await using (var file = File.OpenRead(PartialPath))
                hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(hash).Equals(expectedSha256 ?? ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(PartialPath);
                throw new InvalidDataException("The downloaded model failed its integrity check. Retry the download.");
            }
            File.Move(PartialPath, ModelPath);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(ModelPath)) File.Delete(ModelPath);
            if (File.Exists(PartialPath)) File.Delete(PartialPath);
        }
        finally { _gate.Release(); }
    }
}
