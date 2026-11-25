using System.Text.Json;
using System.Text.Json.Serialization;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.Services;

public sealed record ReadAloudPreferences(
    ReadAloudEngine Engine = ReadAloudEngine.Windows,
    string? WindowsVoice = null,
    string? KokoroVoice = null,
    double Speed = 1);

public sealed class ReadAloudPreferenceStore(string path)
{
    public ReadAloudPreferences Load()
    {
        try
        {
            var preferences = JsonSerializer.Deserialize(File.ReadAllText(path),
                ReadAloudPreferenceJsonContext.Default.StoredReadAloudPreferences);
            if (preferences is null) return new();

            var windowsVoice = preferences.WindowsVoice;
            var kokoroVoice = preferences.KokoroVoice;
            if (!string.IsNullOrEmpty(preferences.Voice))
            {
                if (KokoroSpeechSynthesizer.SpanishVoices.Any(voice => voice.Id == preferences.Voice))
                    kokoroVoice ??= preferences.Voice;
                else
                    windowsVoice ??= preferences.Voice;
            }
            return new(preferences.Engine == "kokoro" ? ReadAloudEngine.Kokoro : ReadAloudEngine.Windows,
                windowsVoice, kokoroVoice, Speeds.Contains(preferences.Speed) ? preferences.Speed : 1);
        }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
        catch (JsonException) { return new(); }
    }
    public static double[] Speeds { get; } = [0.5, 0.75, 1, 1.25, 1.5, 2];
    public void Save(ReadAloudPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var stored = new StoredReadAloudPreferences(
            preferences.Engine == ReadAloudEngine.Kokoro ? "kokoro" : "windows",
            preferences.WindowsVoice, preferences.KokoroVoice, preferences.Speed);
        var json = JsonSerializer.Serialize(stored, ReadAloudPreferenceJsonContext.Default.StoredReadAloudPreferences);
        // Replace the previous settings only after the new JSON has been written.
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, true);
    }
}

internal sealed record StoredReadAloudPreferences(
    string? Engine, string? WindowsVoice, string? KokoroVoice, double Speed, string? Voice = null);

// Generated metadata keeps preference serialization compatible with trimmed builds.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StoredReadAloudPreferences))]
internal partial class ReadAloudPreferenceJsonContext : JsonSerializerContext;
