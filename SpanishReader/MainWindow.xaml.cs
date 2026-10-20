using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SpanishReader.Core.ViewModels;
using SpanishReader.Services;
using SpanishReader.Views;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SpanishReader
{
    /// <summary>
    /// The application window. This hosts a Frame that displays pages. Add your
    /// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
    /// can use Page features such as navigation events and the Loaded lifecycle.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            AppWindow.SetIcon("Assets/AppIcon.ico");

            var navigationService = ((App)Application.Current).Services.GetRequiredService<NavigationService>();
            navigationService.SetFrame(RootFrame);
            navigationService.Register<MainPageViewModel, MainPage>();
            navigationService.Register<ImportPageViewModel, ImportPage>();

            navigationService.Navigate<MainPageViewModel>();
        }
    }
}
