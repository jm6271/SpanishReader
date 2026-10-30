using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Services;
using SpanishReader.Core.ViewModels;
using SpanishReader.Services;
using SpanishReader.Views;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SpanishReader
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public IServiceProvider Services { get; }

        /// <summary>
        /// The main application window. Use <c>App.Window</c> from any class that needs
        /// the window reference (for dialogs, pickers, interop, etc.).
        /// </summary>
        public static Window Window { get; private set; } = null!;

        /// <summary>
        /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
        /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
        /// <see cref="Windows.System.DispatcherQueue"/>.
        /// </summary>
        public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

        /// <summary>
        /// The native window handle (HWND). Use for file pickers,
        /// <c>DataTransferManager</c>, and any WinRT interop that requires
        /// <c>InitializeWithWindow</c>.
        /// </summary>
        public static nint WindowHandle =>
            WinRT.Interop.WindowNative.GetWindowHandle(Window);

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            InitializeComponent();

            var services = new ServiceCollection();
            services.AddSingleton<IStoryRepository>(_ => new SqliteStoryRepository(
                AppDataPath.GetDatabasePath(), Path.Combine(AppContext.BaseDirectory, "BundledStories")));
            services.AddSingleton<ITranslationService>(_ => new BergamotTranslationService(
                Path.Combine(AppContext.BaseDirectory, "TranslationModels", "esen", "config.yml")));
            services.AddSingleton<MainPageViewModel>();
            services.AddSingleton<MainPage>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<ImportPage>();
            services.AddSingleton<ImportPageViewModel>();
            services.AddSingleton<EditStoryPage>();
            services.AddSingleton<EditStoryPageViewModel>();
            services.AddSingleton<ReadingPage>();
            services.AddSingleton<ReadingPageViewModel>();

            services.AddSingleton<NavigationService>();
            services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());

            Services = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Window = Services.GetRequiredService<MainWindow>();
            Window.Closed += OnWindowClosed;
            Window.Activate();
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            Window.Closed -= OnWindowClosed;
            (Services as IDisposable)?.Dispose();
        }
    }
}
