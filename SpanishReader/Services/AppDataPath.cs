using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Windows.Storage;

namespace SpanishReader.Services;

internal static class AppDataPath
{
    public static string GetDatabasePath()
    {
        // Querying the required buffer length detects identity without exceptions or
        // relying on a build configuration that may differ from the actual launch.
        uint length = 0;
        int result = GetCurrentPackageFullName(ref length, nint.Zero);
        string directory;
        if (result == 15700) // APPMODEL_ERROR_NO_PACKAGE
        {
            // GetForUnpackaged is not present in the referenced Windows App SDK 1.8.
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpanishReader", "SpanishReader");
        }
        else if (result == 122) // ERROR_INSUFFICIENT_BUFFER: a package identity exists.
        {
            using var data = ApplicationData.GetDefault();
            directory = data.LocalPath;
        }
        else
        {
            throw new Win32Exception(result, "Could not determine the app's data directory.");
        }

        return Path.Combine(directory, "SpanishReader.db");
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);
}
