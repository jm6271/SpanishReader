using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using NAudio.Wave;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.Services;

public sealed partial class KokoroSpeechSynthesizer(string modelPath, string voicesDirectory) : IDisposable
{
    public const string ModelFileName = "kokoro.onnx";
    public const string ModelUrl =
        "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx";
    public const string ModelSha256 = "0cfd5e79aab70a3d8c1a57dc639835110ddb32c9f5ff4fdd1f4db202ea43bb05";
    public const string DefaultVoiceId = "ef_dora";
    public const int SampleRate = 24_000;
    private static readonly float[] ClipBoundarySilence = new float[SampleRate / 4];
    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal) { "DNI", "PDF", "ONU" };

    public static IReadOnlyList<SpeechVoice> SpanishVoices { get; } = Array.AsReadOnly<SpeechVoice>(
        [new(DefaultVoiceId, "Dora"), new("em_alex", "Alex"), new("em_santa", "Santa")]);

    private readonly object _gate = new();
    private readonly Dictionary<string, KokoroVoice> _voices = [];
    private KokoroModel? _model;
    private bool _disposed;

    public Task<IReadOnlyList<byte[]>> PrepareWavClipsAsync(string text, string voiceId, double speed,
        CancellationToken cancellationToken) => Task.Run<IReadOnlyList<byte[]>>(() =>
    {
        // KokoroSharp's synchronous ONNX call runs on a worker and shares model
        // lifetime with deletion. Check cancellation before and after inference.
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!SpanishVoices.Any(voice => voice.Id == voiceId))
                throw new ArgumentException("The requested Kokoro voice is unavailable.", nameof(voiceId));

            var audio = new List<byte[]>();
            foreach (var tokens in Tokenize(text, cancellationToken))
            {
                if (!_voices.TryGetValue(voiceId, out var voice))
                    _voices[voiceId] = voice = KokoroVoice.FromPath(Path.Combine(voicesDirectory, voiceId + ".npy"));

                _model ??= new KokoroModel(modelPath);
                var samples = _model.Infer(tokens, voice.Features, (float)speed);
                cancellationToken.ThrowIfCancellationRequested();
                if (samples.Length == 0)
                    throw new InvalidDataException("Kokoro returned no audio for the sentence.");
                audio.Add(EncodeWavClip(samples));
                cancellationToken.ThrowIfCancellationRequested();
            }
            return audio;
        }
    }, cancellationToken);

    internal static byte[] EncodeWavClip(float[] samples)
    {
        using var wave = new MemoryStream();
        using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(wave),
            new WaveFormat(SampleRate, 16, 1)))
        {
            // Each clip opens a fresh audio source. Give it time to start
            // before the first word and drain before MediaEnded.
            writer.WriteSamples(ClipBoundarySilence, 0, ClipBoundarySilence.Length);
            writer.WriteSamples(samples, 0, samples.Length);
            writer.WriteSamples(ClipBoundarySilence, 0, ClipBoundarySilence.Length);
        }
        return wave.ToArray();
    }

    internal static string NormalizeSpeechText(string text) =>
        SpeechWord().Replace(text.Trim().Normalize(NormalizationForm.FormC), match =>
        {
            var word = match.Value;
            // Misaki's Spanish phonemizer treats all-caps prose as abbreviations.
            // Keep recognized acronyms, dotted initialisms and letter-digit IDs.
            return word.All(char.IsUpper) && !Acronyms.Contains(word) ? word.ToLowerInvariant() : word;
        });

    internal static IEnumerable<int[]> Tokenize(string text, CancellationToken cancellationToken) =>
        TokenizeChunks(NormalizeSpeechText(text), cancellationToken);

    private static IEnumerable<int[]> TokenizeChunks(
        string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SpeakableText().IsMatch(text)) yield break;
        var tokens = Tokenizer.Tokenize(text.Trim(), "es");
        if (tokens.Length == 0) yield break;
        if (tokens.Length <= KokoroModel.maxTokens)
        {
            yield return tokens;
            yield break;
        }

        // Split at source boundaries before phonemizing each shorter chunk.
        // Favor a clause boundary near the middle over splitting a word.
        int middle = text.Length / 2;
        int split = text.LastIndexOfAny(['.', '!', '?', ',', ';', ':', '\n'], middle) + 1;
        if (split <= middle / 2)
            split = text.LastIndexOfAny([' ', '\t', '\r', '\n', '\u00a0'], middle) + 1;
        if (split <= middle / 2 || split >= text.Length)
        {
            int[] elements = StringInfo.ParseCombiningCharacters(text);
            if (elements.Length < 2)
                throw new InvalidDataException("A text element exceeds Kokoro's input limit.");
            split = elements[elements.Length / 2];
        }

        foreach (var part in TokenizeChunks(text[..split], cancellationToken)) yield return part;
        foreach (var part in TokenizeChunks(text[split..], cancellationToken)) yield return part;
    }

    public Task ReleaseModelAsync() => Task.Run(ReleaseModel);

    private void ReleaseModel()
    {
        lock (_gate)
        {
            _model?.Dispose();
            _model = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ReleaseModel();
        }
    }

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]")]
    private static partial Regex SpeakableText();

    // Dotted abbreviations can repeat initials for plurals, as in EE.UU.
    [GeneratedRegex(@"(?:(?<initial>\p{Lu})\k<initial>?\.)+(?<last>\p{Lu})\k<last>?(?:\.(?!\p{L}))?(?![\p{L}\p{M}\p{N}])|[\p{L}\p{M}\p{N}]+")]
    private static partial Regex SpeechWord();
}
