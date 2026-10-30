using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;
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
        private readonly ThemePreferenceStore _themePreferenceStore;

        public MainWindow(ThemePreferenceStore themePreferenceStore)
        {
            _themePreferenceStore = themePreferenceStore;
            InitializeComponent();

            // Default keeps following Windows until the user explicitly toggles.
            // Apply to the common root so pages, dialogs and titlebar inherit together.
            RootGrid.RequestedTheme = _themePreferenceStore.Load() switch
            {
                ThemePreference.Light => ElementTheme.Light,
                ThemePreference.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            RootGrid.Loaded += (_, _) =>
            {
                RootGrid.XamlRoot.Changed += (_, _) => UpdateCaptionButtonLayout();
                UpdateCaptionButtonLayout();
            };
            AppWindow.Changed += (_, _) => UpdateCaptionButtonLayout();
            Activated += (_, args) => ThemeToggleButton.Opacity =
                args.WindowActivationState == WindowActivationState.Deactivated ? 0.5 : 1;

            // Native caption buttons otherwise follow the Windows theme, which can
            // differ from the app's theme (for example, a light app on a dark desktop).
            AppTitleBar.ActualThemeChanged += (_, _) => UpdateTitleBarTheme();
            UpdateTitleBarTheme();

            // An unpackaged app can be started with any working directory.
            AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

            var navigationService = ((App)Application.Current).Services.GetRequiredService<NavigationService>();
            navigationService.SetFrame(RootFrame);
            navigationService.Register<MainPageViewModel, MainPage>();
            navigationService.Register<ImportPageViewModel, ImportPage>();
            navigationService.Register<EditStoryPageViewModel, EditStoryPage>();
            navigationService.Register<ReadingPageViewModel, ReadingPage>();

            navigationService.Navigate<MainPageViewModel>();
        }

        private void UpdateCaptionButtonLayout()
        {
            if (RootGrid.XamlRoot is not { } xamlRoot)
                return;

            // AppWindow reports physical pixels; XAML uses effective pixels. Using
            // the native inset and height keeps all four buttons aligned at any DPI,
            // including after moving the window to another monitor or maximizing it.
            double scale = xamlRoot.RasterizationScale;
            double captionWidth = AppWindow.TitleBar.RightInset / scale;
            double captionHeight = AppWindow.TitleBar.Height / scale;
            if (captionWidth <= 0 || captionHeight <= 0)
                return;

            CaptionButtonInset.Width = captionWidth;
            TitleBarLayout.Height = captionHeight;
            ThemeToggleButton.Width = captionWidth / 3;
            ThemeToggleIcon.FontSize = 16 * captionHeight / 32;
        }

        private void UpdateTitleBarTheme()
        {
            bool isDark = AppTitleBar.ActualTheme == ElementTheme.Dark;
            string action = isDark ? "Switch to light mode" : "Switch to dark mode";
            ThemeToggleIcon.Glyph = isDark ? "\uE706" : "\uE708";
            AutomationProperties.SetName(ThemeToggleButton, action);
            ToolTipService.SetToolTip(ThemeToggleButton, action);

            AppWindow.TitleBar.PreferredTheme = AppTitleBar.ActualTheme == ElementTheme.Dark
                ? TitleBarTheme.Dark
                : TitleBarTheme.Light;

            // Let the themed XAML canvas show through the native caption buttons,
            // including when the window loses focus. Keep system hover/pressed colors.
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }

        private void ThemeToggleButton_Loaded(object sender, RoutedEventArgs e)
        {
            // SubtleButtonStyle animates its template's background brush, including
            // the alpha change from transparent to opaque. That interpolation can
            // briefly flash a lighter fill in dark mode. Native caption buttons
            // switch immediately, so disable only this presenter's brush transition;
            // keep the standard button template, focus visuals and pointer states.
            if (VisualTreeHelper.GetChildrenCount(ThemeToggleButton) > 0
                && VisualTreeHelper.GetChild(ThemeToggleButton, 0) is ContentPresenter presenter)
            {
                presenter.BackgroundTransition = null;
            }
        }

        private async void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var preference = RootGrid.ActualTheme == ElementTheme.Dark
                ? ThemePreference.Light : ThemePreference.Dark;
            RootGrid.RequestedTheme = preference == ThemePreference.Dark
                ? ElementTheme.Dark : ElementTheme.Light;

            try
            {
                _themePreferenceStore.Save(preference);
            }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
            {
                // Keep the requested theme usable even if storage is temporarily unavailable,
                // but do not imply that the choice will survive a restart.
                await new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    RequestedTheme = RootGrid.RequestedTheme,
                    Title = "Couldn’t save appearance",
                    Content = "The theme changed for this session, but your choice could not be saved. Please try again.",
                    CloseButtonText = "OK"
                }.ShowAsync();
            }
        }
    }
}
