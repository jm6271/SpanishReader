using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Views;

namespace SpanishReader.Services;

public class NavigationService : INavigationService
{
    private Frame? _frame;
    private readonly Dictionary<Type, Type> _mappings = [];

    public void Navigate<T>(object? parameter = null) where T : class
    {
        if (_frame == null)
        {
            throw new InvalidOperationException("Frame is not set. Call SetFrame() before navigating");
        }

        var vmType = typeof(T);

        if (!_mappings.TryGetValue(vmType, out var pageType))
        {
            throw new InvalidOperationException($"No page registered for {vmType}");
        }

        var page = ((App)Application.Current).Services.GetRequiredService(pageType);
        if (page is INavigationAware navigationAwarePage)
        {
            navigationAwarePage.OnNavigatedTo(parameter);
        }
        _frame.Content = page;
    }

    public void SetFrame(object frame)
    {
        if (frame is not Frame f)
        {
            throw new ArgumentException("Frame must be of type Frame.", nameof(frame));
        }

        _frame = f;
    }

    public void Register<TViewModel, TPage>() where TPage : Page
    {
        _mappings[typeof(TViewModel)] = typeof(TPage);
    }
}
