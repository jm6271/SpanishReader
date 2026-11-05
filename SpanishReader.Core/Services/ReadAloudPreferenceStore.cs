using System.Text.Json;
using System.Text.Json.Serialization;
namespace SpanishReader.Core.Services;

public sealed class ReadAloudPreferenceStore(string path)
{
    public (string Voice, double Speed) Load()
    {
        try
        {
            var preferences = JsonSerializer.Deserialize(File.ReadAllText(path),
                ReadAloudPreferenceJsonContext.Default.ReadAloudPreferences);
            return preferences is { Voice: not null } && Speeds.Contains(preferences.Speed)
                ? (preferences.Voice, preferences.Speed) : (string.Empty, 1);
        }
        catch (IOException) { return (string.Empty, 1); }
        catch (UnauthorizedAccessException) { return (string.Empty, 1); }
        catch (JsonException) { return (string.Empty, 1); }
    }
    public static double[] Speeds { get; } = [0.5, 0.75, 1, 1.25, 1.5, 2];
    public void Save(string voice, double speed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.Serialize(new ReadAloudPreferences(voice, speed),
            ReadAloudPreferenceJsonContext.Default.ReadAloudPreferences);
        // Replace the previous settings only after the new JSON has been written.
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, true);
    }
}

internal sealed record ReadAloudPreferences(string Voice, double Speed);

// Generated metadata keeps preference serialization compatible with trimmed builds.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ReadAloudPreferences))]
internal partial class ReadAloudPreferenceJsonContext : JsonSerializerContext;
