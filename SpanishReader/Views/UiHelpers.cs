using Microsoft.UI.Xaml;

namespace SpanishReader.Views;

public static class UiHelpers
{
    public static Visibility When(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static bool HasText(string value) => !string.IsNullOrEmpty(value);
    public static Visibility WhenHasText(string value) => When(HasText(value));
}
