using Microsoft.UI.Xaml.Controls;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Views
{
    /// <summary>
    /// The main content page displayed inside the application window.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPageViewModel ViewModel { get; }

        public MainPage(MainPageViewModel viewModel)
        {
            ViewModel = viewModel;
            InitializeComponent();
            DataContext = ViewModel;
        }
    }
}
