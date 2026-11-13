using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
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

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanDelete) return;
        var answer = await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete language model?",
            Content = "The downloaded model will be removed from this device.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        }.ShowAsync();
        if (answer == ContentDialogResult.Primary)
            await ViewModel.DeleteCommand.ExecuteAsync(null);
    }
}
