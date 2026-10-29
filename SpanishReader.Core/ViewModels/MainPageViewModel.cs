using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using SpanishReader.Core.Models;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.ViewModels;

public partial class MainPageViewModel(IStoryRepository repository, INavigationService navigationService) : ObservableObject
{
    private bool _reloadRequested;
    // Keep the complete snapshot so changing criteria can restore hidden stories.
    private readonly List<Story> _allStories = [];

    [ObservableProperty] public partial LibrarySortOrder SelectedSortOrder { get; set; } = LibrarySortOrder.NewestFirst;
    [ObservableProperty] public partial CEFR? SelectedLevel { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    partial void OnSelectedSortOrderChanged(LibrarySortOrder value) => RefreshVisibleStories();
    partial void OnSelectedLevelChanged(CEFR? value) => RefreshVisibleStories();
    partial void OnSearchTextChanged(string value) => RefreshVisibleStories();
    public ObservableCollection<Story> Stories { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
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
    public bool IsEmpty => !IsLoading && ErrorMessage.Length == 0 && _allStories.Count == 0;
    public bool HasNoResults => !IsLoading && ErrorMessage.Length == 0 && _allStories.Count > 0 && Stories.Count == 0;

    private void RefreshVisibleStories()
    {
        var query = SearchText.Trim();
        var matches = _allStories.Where(story =>
            (SelectedLevel is null || story.Level == SelectedLevel) &&
            (query.Length == 0 || story.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             story.EnglishTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             story.Author.Contains(query, StringComparison.OrdinalIgnoreCase)));
        var ordered = SelectedSortOrder switch
        {
            LibrarySortOrder.OldestFirst => matches.OrderBy(s => s.DateAdded).ThenBy(s => s.Id),
            LibrarySortOrder.CefrAscending => matches.OrderBy(s => s.Level).ThenByDescending(s => s.DateAdded).ThenByDescending(s => s.Id),
            LibrarySortOrder.CefrDescending => matches.OrderByDescending(s => s.Level).ThenByDescending(s => s.DateAdded).ThenByDescending(s => s.Id),
            _ => matches.OrderByDescending(s => s.DateAdded).ThenByDescending(s => s.Id)
        };
        Stories.Clear();
        foreach (var story in ordered) Stories.Add(story);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoResults));
    }

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
                    _allStories.Clear();
                    _allStories.AddRange(stories);
                    RefreshVisibleStories();
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
            _allStories.RemoveAll(item => item.Id == story.Id);
            RefreshVisibleStories();
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
