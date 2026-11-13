using Microsoft.UI.Xaml.Controls;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage(SettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
