namespace SpanishReader.Core.Abstractions;

public interface INavigationService
{
    public void Navigate<T>(object? parameter = null) where T : class;
}
