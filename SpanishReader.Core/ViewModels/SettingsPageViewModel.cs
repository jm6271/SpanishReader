using CommunityToolkit.Mvvm.Input;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.ViewModels;

public partial class SettingsPageViewModel(INavigationService navigationService)
{
    [RelayCommand]
    private void Back() => navigationService.Navigate<MainPageViewModel>();
}
