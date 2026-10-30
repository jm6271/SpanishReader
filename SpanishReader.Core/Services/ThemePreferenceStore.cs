using SpanishReader.Core.Models;

namespace SpanishReader.Core.Services;

/// <summary>Stores the theme independently of the story library and Windows package identity.</summary>
public sealed class ThemePreferenceStore(string filePath)
{
    public ThemePreference Load()
    {
        try
        {
            return Enum.TryParse<ThemePreference>(File.ReadAllText(filePath).Trim(), out var preference)
                && Enum.IsDefined(preference) ? preference : ThemePreference.System;
        }
        catch (IOException)
        {
            // A first launch (or an unreadable settings file) should still open normally.
            return ThemePreference.System;
        }
        catch (UnauthorizedAccessException)
        {
            return ThemePreference.System;
        }
    }

    public void Save(ThemePreference preference)
    {
        if (!Enum.IsDefined(preference))
            throw new ArgumentOutOfRangeException(nameof(preference));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        // Replace only after writing succeeds so an interrupted write cannot truncate
        // the last saved choice. The temporary file lives on the same volume.
        string temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, preference.ToString());
        File.Move(temporaryPath, filePath, overwrite: true);
    }
}
