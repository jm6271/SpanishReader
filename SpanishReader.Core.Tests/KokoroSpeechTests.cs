using KokoroSharp.Processing;
using NAudio.Wave;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.Tests;

public class KokoroSpeechTests
{
    [Theory]
    [InlineData("PROHIBIDO EL PASO", "prohibido el paso")]
    [InlineData("ACTUALIZACIÓN DISPONIBLE", "actualización disponible")]
    [InlineData("ERROR: 47: PROBLEMA DESCONOCIDO", "error: 47: problema desconocido")]
    [InlineData("NO HAY SUFICIENTE TINTA AMARILLA.", "no hay suficiente tinta amarilla.")]
    [InlineData("¡ATENCIO\u0301N!", "¡atención!")]
    [InlineData("PRESENTE SU DNI, PDF U ONU.", "presente su DNI, PDF u ONU.")]
    [InlineData("Madrid y EE.UU. USAN H2O.", "Madrid y EE.UU. usan H2O.")]
    public void NormalizesOnlyProseBeforeSpanishTokenization(string text, string expected)
    {
        var input = KokoroSpeechSynthesizer.NormalizeSpeechText(text);

        Assert.Equal(expected, input);
        var expectedTokens = Tokenizer.Tokenize(expected, "es");
        Assert.NotEmpty(expectedTokens);
        Assert.Equal(expectedTokens, Assert.Single(KokoroSpeechSynthesizer.Tokenize(text, default)));
    }

    [Fact]
    public void PreparedWavPadsBothClipBoundaries()
    {
        using var wave = new MemoryStream(KokoroSpeechSynthesizer.EncodeWavClip([0.5f, -0.5f, 0.25f]));
        using var reader = new WaveFileReader(wave);
        using var pcm = new BinaryReader(reader);

        Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
        Assert.Equal(24_000, reader.WaveFormat.SampleRate);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal((6_000L * 2 + 3) * sizeof(short), reader.Length);
        Assert.All(pcm.ReadBytes(6_000 * sizeof(short)), value => Assert.Equal(0, value));
        Assert.Equal(16_383, pcm.ReadInt16());
        Assert.Equal(-16_383, pcm.ReadInt16());
        Assert.Equal(8_191, pcm.ReadInt16());
        Assert.All(pcm.ReadBytes(6_000 * sizeof(short)), value => Assert.Equal(0, value));
        Assert.Equal(reader.Length, reader.Position);
    }
}
