using Microsoft.UI.Xaml.Controls;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Views
{
    /// <summary>
    /// The main content page displayed inside the application window.
    /// </summary>
    public sealed partial class MainPage : Page, INavigationAware
    {
        private bool _dialogOpen;
        public MainPageViewModel ViewModel { get; }

        public MainPage(MainPageViewModel viewModel)
        {
            ViewModel = viewModel;
            InitializeComponent();
            DataContext = ViewModel;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }

        public void OnNavigatedTo(object? parameter)
        {
            ViewModel.PrepareForDisplay(parameter);
        }

        private void OnStoryClicked(object sender, ItemClickEventArgs e) =>
            ViewModel.OpenStoryCommand.Execute(e.ClickedItem as Story);

        public static string StoryActionsName(string title) => $"Actions for {title}";

        private void OnEditStoryClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Story story } && ViewModel.EditStoryCommand.CanExecute(story))
                ViewModel.EditStoryCommand.Execute(story);
        }

        private async void OnDeleteStoryClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_dialogOpen || !ViewModel.CanUseLibrary || sender is not MenuFlyoutItem { Tag: Story story }) return;
            _dialogOpen = true;
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Delete story?",
                    Content = $"“{story.Title}” and all its translations will be permanently deleted from your library.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    await ViewModel.DeleteStoryCommand.ExecuteAsync(story);
            }
            finally
            {
                _dialogOpen = false;
            }
        }

        // Use the same breakpoint as the reader; the CommandBar handles
        // moving commands to overflow when the remaining width is limited.
        private void OnPageSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
            Microsoft.UI.Xaml.VisualStateManager.GoToState(this, ActualWidth < 900 ? "Narrow" : "Wide", false);
    }
}
