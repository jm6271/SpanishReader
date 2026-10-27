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
    [ObservableProperty] public partial string ActionErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseLibrary))]
    [NotifyCanExecuteChangedFor(nameof(EditStoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteStoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenStoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportStoryCommand))]
    public partial bool IsDeleting { get; set; }

    public bool CanUseLibrary => !IsDeleting;
    public bool IsEmpty => !IsLoading && ErrorMessage.Length == 0 && Stories.Count == 0;

    // Navigation delivers a saved story before this singleton page loads.
    public void PrepareForDisplay(object? parameter)
    {
        SuccessMessage = ImportWarningMessage = ActionErrorMessage = string.Empty;
        // Accept a bare Story for existing navigation callers as an import.
        var change = parameter as LibraryChange;
        var story = change?.Story ?? parameter as Story;
        if (story is null) return;
        var verb = change?.Kind == LibraryChangeKind.Updated ? "Updated" : "Imported";
        var missing = story.Sentences.Count(sentence => string.IsNullOrWhiteSpace(sentence.EnglishText));
        if (missing == 0)
            SuccessMessage = $"{verb} “{story.Title}”.";
        else
            ImportWarningMessage = $"{verb} “{story.Title}”. " +
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

    [RelayCommand(CanExecute = nameof(CanUseLibrary))]
    private void ImportStory() => navigationService.Navigate<ImportPageViewModel>();

    [RelayCommand(CanExecute = nameof(CanUseLibrary))]
    private void OpenStory(Story? story)
    {
        if (story is not null) navigationService.Navigate<ReadingPageViewModel>(story);
    }

    [RelayCommand(CanExecute = nameof(CanUseLibrary))]
    private void EditStory(Story? story)
    {
        if (story is not null) navigationService.Navigate<EditStoryPageViewModel>(story);
    }

    // The WinUI page asks for confirmation before executing this command.
    [RelayCommand(CanExecute = nameof(CanUseLibrary))]
    private async Task DeleteStoryAsync(Story? story)
    {
        if (story is null || IsDeleting) return;
        IsDeleting = true;
        SuccessMessage = ImportWarningMessage = ActionErrorMessage = string.Empty;
        try
        {
            await repository.DeleteAsync(story.Id);
            Stories.Remove(story);
            OnPropertyChanged(nameof(IsEmpty));
            SuccessMessage = $"Deleted “{story.Title}”.";
            // Queue a final read even if an earlier snapshot is still loading.
            await LoadAsync();
        }
        catch (Exception)
        {
            ActionErrorMessage = $"“{story.Title}” could not be deleted. Please try Delete again.";
        }
        finally
        {
            IsDeleting = false;
        }
    }
}
