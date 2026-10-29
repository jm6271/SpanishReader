using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
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

            // Native caption buttons otherwise follow the Windows theme, which can
            // differ from the app's theme (for example, a light app on a dark desktop).
            AppTitleBar.ActualThemeChanged += (_, _) => UpdateTitleBarTheme();
            UpdateTitleBarTheme();

            AppWindow.SetIcon("Assets/AppIcon.ico");

            var navigationService = ((App)Application.Current).Services.GetRequiredService<NavigationService>();
            navigationService.SetFrame(RootFrame);
            navigationService.Register<MainPageViewModel, MainPage>();
            navigationService.Register<ImportPageViewModel, ImportPage>();
            navigationService.Register<EditStoryPageViewModel, EditStoryPage>();
            navigationService.Register<ReadingPageViewModel, ReadingPage>();

            navigationService.Navigate<MainPageViewModel>();
        }

        private void UpdateTitleBarTheme()
        {
            AppWindow.TitleBar.PreferredTheme = AppTitleBar.ActualTheme == ElementTheme.Dark
                ? TitleBarTheme.Dark
                : TitleBarTheme.Light;
        }
    }
}
