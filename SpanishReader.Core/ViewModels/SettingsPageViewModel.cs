using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.ViewModels;

public partial class SettingsPageViewModel(
    INavigationService navigationService, ModelDownloadViewModel languageModel,
    ModelDownloadViewModel readAloudModel, ReadAloudViewModel readAloud) : ObservableObject
{
    public ModelDownloadViewModel LanguageModel { get; } = languageModel;
    public ModelDownloadViewModel ReadAloudModel { get; } = readAloudModel;
    public ReadAloudViewModel ReadAloud { get; } = readAloud;

    [RelayCommand]
    private void Back() => navigationService.Navigate<MainPageViewModel>();
}
