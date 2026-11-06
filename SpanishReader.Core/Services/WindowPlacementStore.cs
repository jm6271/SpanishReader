using System.Text.Json;
using System.Text.Json.Serialization;
using SpanishReader.Core.Models;

namespace SpanishReader.Core.Services;

/// <summary>Persists placement independently of WinUI and Windows package identity.</summary>
public sealed class WindowPlacementStore(string filePath)
{
    public WindowPlacement? Load()
    {
        try
        {
            var placement = JsonSerializer.Deserialize(File.ReadAllText(filePath),
                WindowPlacementJsonContext.Default.WindowPlacement);
            return placement is { IsValid: true } ? placement : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    public void Save(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!placement.IsValid)
            throw new ArgumentOutOfRangeException(nameof(placement));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        // Write beside the destination, then replace it so an interrupted write
        // leaves the previous complete settings available on the next launch.
        File.WriteAllText(filePath + ".tmp", JsonSerializer.Serialize(placement,
            WindowPlacementJsonContext.Default.WindowPlacement));
        File.Move(filePath + ".tmp", filePath, overwrite: true);
    }
}

// Explicit generated metadata supports the app's trimmed Release publish.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(WindowPlacement))]
internal partial class WindowPlacementJsonContext : JsonSerializerContext;
