using Microsoft.UI.Xaml;

namespace SpanishReader.Views;

public static class UiHelpers
{
    public static Microsoft.UI.Xaml.Controls.Symbol PlaybackSymbol(string label) =>
        label == "Pause" ? Microsoft.UI.Xaml.Controls.Symbol.Pause : Microsoft.UI.Xaml.Controls.Symbol.Play;

    public static Visibility WhenPlayerFeedback(bool hasVoice, string error) => When(!hasVoice || HasText(error));

    public static Visibility When(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility WhenNot(bool value) => When(!value);
    public static bool HasText(string value) => !string.IsNullOrEmpty(value);
    public static Visibility WhenHasText(string value) => When(HasText(value));
}
