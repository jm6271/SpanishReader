using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using SpanishReader.Core.Models;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.ViewModels;

public partial class MainPageViewModel(IStoryRepository repository, INavigationService navigationService) : ObservableObject
{
    private bool _reloadRequested;
    public ObservableCollection<Story> Stories { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty] public partial string SuccessMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string ImportWarningMessage { get; set; } = string.Empty;
    public bool IsEmpty => !IsLoading && ErrorMessage.Length == 0 && Stories.Count == 0;

    // Navigation delivers a saved story before this singleton page loads.
    public void PrepareForDisplay(Story? importedStory)
    {
        SuccessMessage = ImportWarningMessage = string.Empty;
        if (importedStory is null) return;
        var missing = importedStory.Sentences.Count(sentence => string.IsNullOrWhiteSpace(sentence.EnglishText));
        if (missing == 0)
            SuccessMessage = $"Imported “{importedStory.Title}”.";
        else
            ImportWarningMessage = $"Imported “{importedStory.Title}”. " +
                (missing == 1 ? "1 sentence has no English translation." : $"{missing} sentences have no English translation.");
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        // A return from import can arrive while an earlier library read is pending.
        // Queue another read rather than letting that stale result hide the new story.
        if (IsLoading)
        {
            _reloadRequested = true;
            return;
        }
        IsLoading = true;
        try
        {
            do
            {
                _reloadRequested = false;
                ErrorMessage = string.Empty;
                try
                {
                    var stories = await repository.GetAllAsync();
                    Stories.Clear();
                    foreach (var story in stories) Stories.Add(story);
                }
                catch (Exception)
                {
                    ErrorMessage = "The library could not be loaded. Please try again.";
                }
            } while (_reloadRequested);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private void ImportStory() => navigationService.Navigate<ImportPageViewModel>();
}
