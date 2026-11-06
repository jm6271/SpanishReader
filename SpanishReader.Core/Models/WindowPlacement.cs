namespace SpanishReader.Core.Models;

/// <summary>
/// Normal window bounds in Windows workspace coordinates, plus the state to reopen in.
/// Keep these coordinates paired with Get/SetWindowPlacement, not screen-position APIs.
/// </summary>
public sealed record WindowPlacement(int Left, int Top, int Right, int Bottom, bool IsMaximized)
{
    public bool IsValid => (long)Right - Left is > 0 and <= int.MaxValue
        && (long)Bottom - Top is > 0 and <= int.MaxValue;
}
