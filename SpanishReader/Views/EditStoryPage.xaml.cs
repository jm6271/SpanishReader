using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Views;

public sealed partial class EditStoryPage : Page, INavigationAware
{
    private bool _dialogOpen;
    public EditStoryPageViewModel ViewModel { get; }

    public EditStoryPage(EditStoryPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not Story story) throw new ArgumentException("A story is required.", nameof(parameter));
        ViewModel.PrepareForDisplay(story);
    }

    private async void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEdit || _dialogOpen) return;
        if (ViewModel.HasChanges)
        {
            _dialogOpen = true;
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Discard changes?",
                    Content = $"Your unsaved changes to “{ViewModel.Title}” will be lost.",
                    PrimaryButtonText = "Discard changes",
                    CloseButtonText = "Keep editing",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            finally
            {
                _dialogOpen = false;
            }
        }
        ViewModel.BackCommand.Execute(null);
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        VisualStateManager.GoToState(this, ActualWidth < 900 ? "Narrow" : "Wide", false);
}
