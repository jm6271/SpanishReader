using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.Tests;

public sealed class ModelDownloadTests
{
    [Fact]
    public async Task Download_ResumesAndPublishesOnlyVerifiedFile()
    {
        byte[] bytes = "sample gguf bytes"u8.ToArray();
        string directory = Path.Combine(Path.GetTempPath(), $"model-test-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, ModelDownloadService.FileName);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(path + ".partial", bytes[..7]);
        try
        {
            using var client = new HttpClient(new Handler(request =>
            {
                Assert.Equal(7, request.Headers.Range?.Ranges.Single().From);
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(bytes[7..])
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(7, bytes.Length - 1, bytes.Length);
                return response;
            }));
            var service = new ModelDownloadService(client, path, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            await service.DownloadAsync();
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(service.PartialPath));
            await service.DeleteAsync();
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Download_BadHashNeverPublishesModel()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"model-test-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, ModelDownloadService.FileName);
        try
        {
            using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("bad"u8.ToArray())
            }));
            var service = new ModelDownloadService(client, path);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync());
            Assert.False(service.IsInstalled);
            Assert.False(File.Exists(service.PartialPath));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(125)]
    [InlineData(250)]
    public async Task Download_ReportsProgressAtQuarterSecondIntervals(int millisecondsPerBuffer)
    {
        byte[] bytes = new byte[4 * 1024 * 1024];
        string directory = Path.Combine(Path.GetTempPath(), $"model-test-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, ModelDownloadService.FileName);
        try
        {
            using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            }));
            // Advance only when the downloader samples time, once after each buffer.
            // Disk speed and CI scheduling must not determine the expected reports.
            var clock = new BufferTimeProvider(millisecondsPerBuffer);
            var service = new ModelDownloadService(client, path, Convert.ToHexStringLower(SHA256.HashData(bytes)), clock);
            var reports = new List<(long Received, long? Total)>();
            await service.DownloadAsync(new InlineProgress(value => reports.Add(value)));
            const int bufferSize = 81920;
            int bufferCount = (bytes.Length + bufferSize - 1) / bufferSize;
            var expected = new List<(long Received, long? Total)> { (0, bytes.Length) };
            for (int buffer = 1; buffer <= bufferCount; buffer++)
            {
                if (millisecondsPerBuffer > 0 && buffer % (250 / millisecondsPerBuffer) == 0)
                    expected.Add((Math.Min((long)buffer * bufferSize, bytes.Length), bytes.Length));
            }
            // Completion is always reported, including when no interval has elapsed.
            expected.Add((bytes.Length, bytes.Length));
            Assert.Equal(expected, reports);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class BufferTimeProvider(int millisecondsPerBuffer) : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp()
        {
            long timestamp = _timestamp;
            _timestamp += millisecondsPerBuffer;
            return timestamp;
        }
    }

    private sealed class InlineProgress(Action<(long Received, long? Total)> report) : IProgress<(long Received, long? Total)>
    {
        public void Report((long Received, long? Total) value) => report(value);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
