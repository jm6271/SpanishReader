using LLama.Native;

namespace SpanishReader.Core.Services;

/// <summary>Persists native diagnostics even when inference terminates the process.</summary>
internal static class LlamaDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpanishReader", "Diagnostics", "llama.log");

    // Keep the delegate rooted for the entire process: native logging may happen
    // on background threads after the managed initialization call has returned.
    internal static readonly NativeLogConfig.LLamaLogCallback Callback =
        (level, message) => Write($"native {level}: {message}");

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                // Open/close each write so a fatal native failure cannot leave the
                // last diagnostic messages buffered in a managed StreamWriter.
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message.TrimEnd()}\n");
            }
        }
        catch (Exception)
        {
            // An I/O failure must never escape a reverse P/Invoke callback.
        }
    }
}
