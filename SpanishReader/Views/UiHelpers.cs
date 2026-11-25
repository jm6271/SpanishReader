using Microsoft.UI.Xaml;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Views;

public static class UiHelpers
{
    public static Microsoft.UI.Xaml.Controls.Symbol PlaybackSymbol(string label) =>
        label == "Pause" ? Microsoft.UI.Xaml.Controls.Symbol.Pause : Microsoft.UI.Xaml.Controls.Symbol.Play;

    public static Visibility WhenPlayerFeedback(bool canPlay, string error) => When(!canPlay || HasText(error));
    public static Visibility WhenWindowsSpeech(ReadAloudEngine engine) => When(engine == ReadAloudEngine.Windows);
    public static Visibility WhenKokoroSpeech(ReadAloudEngine engine) => When(engine == ReadAloudEngine.Kokoro);

    public static Visibility When(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility WhenNot(bool value) => When(!value);
    public static bool Not(bool value) => !value;
    public static bool HasText(string value) => !string.IsNullOrEmpty(value);
    public static Visibility WhenHasText(string value) => When(HasText(value));
}
