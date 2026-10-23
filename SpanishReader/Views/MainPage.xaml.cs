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
            ViewModel.PrepareForDisplay(parameter as Story);
        }

        private void OnStoryClicked(object sender, ItemClickEventArgs e) =>
            ViewModel.OpenStoryCommand.Execute(e.ClickedItem as Story);

        // Use the same breakpoint as the reader; the CommandBar handles
        // moving commands to overflow when the remaining width is limited.
        private void OnPageSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
            Microsoft.UI.Xaml.VisualStateManager.GoToState(this, ActualWidth < 900 ? "Narrow" : "Wide", false);
    }
}
