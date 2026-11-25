using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; }
    public ModelDownloadViewModel LanguageModel => ViewModel.LanguageModel;
    public ModelDownloadViewModel ReadAloudModel => ViewModel.ReadAloudModel;
    public ReadAloudViewModel ReadAloud => ViewModel.ReadAloud;

    public SettingsPage(SettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e) =>
        await ConfirmDeleteAsync(ViewModel.LanguageModel, "Delete language model?");

    private async void OnDeleteReadAloudClicked(object sender, RoutedEventArgs e) =>
        await ConfirmDeleteAsync(ViewModel.ReadAloudModel, "Delete read-aloud model?");

    private async Task ConfirmDeleteAsync(ModelDownloadViewModel model, string title)
    {
        if (!model.CanDelete) return;
        var answer = await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = "The downloaded model will be removed from this device.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        }.ShowAsync();
        if (answer == ContentDialogResult.Primary)
            await model.DeleteCommand.ExecuteAsync(null);
    }
}
