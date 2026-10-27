using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.ViewModels;

public partial class EditStoryPageViewModel(
    IStoryRepository repository, INavigationService navigationService, ITranslationService translationService) : ObservableObject
{
    private Story? _original;
    private bool _committed;
    public IReadOnlyList<CEFR> Levels { get; } = Enum.GetValues<CEFR>();

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string EnglishTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string Author { get; set; } = string.Empty;
    [ObservableProperty] public partial CEFR Level { get; set; } = CEFR.A1;
    [ObservableProperty] public partial string StoryText { get; set; } = string.Empty;
    [ObservableProperty] public partial string TitleError { get; set; } = string.Empty;
    [ObservableProperty] public partial string LevelError { get; set; } = string.Empty;
    [ObservableProperty] public partial string StoryTextError { get; set; } = string.Empty;
    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial bool IsSaving { get; set; }

    public bool CanEdit => !IsSaving;
    public bool CanSave => CanEdit && _original is not null && !_committed;
    // WinUI TextBox normalizes line endings while displaying text. Treat that
    // representation change as unchanged, and retain the stored slices on save.
    private bool HasTextChanges => _original is not null &&
        StoryText.ReplaceLineEndings("\n") != string.Concat(_original.Sentences.Select(s => s.SpanishText)).ReplaceLineEndings("\n");
    public bool HasChanges => _original is not null && !_committed &&
        (Title != _original.Title || EnglishTitle != _original.EnglishTitle || Author != _original.Author ||
         Level != _original.Level || HasTextChanges);

    public void PrepareForDisplay(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(story.Id);
        _original = StoryEditing.Snapshot(story);
        _committed = false;
        Title = _original.Title;
        EnglishTitle = _original.EnglishTitle;
        Author = _original.Author;
        Level = _original.Level;
        StoryText = string.Concat(_original.Sentences.Select(s => s.SpanishText));
        TitleError = LevelError = StoryTextError = ErrorMessage = string.Empty;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnTitleChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) TitleError = string.Empty;
    }

    partial void OnStoryTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) StoryTextError = string.Empty;
    }

    partial void OnLevelChanged(CEFR value)
    {
        if (Enum.IsDefined(value)) LevelError = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave) return;
        var story = new Story
        {
            Id = _original!.Id, DateAdded = _original.DateAdded,
            Title = Title, EnglishTitle = EnglishTitle, Author = Author, Level = Level,
            Sentences = HasTextChanges
                ? SpanishSentenceSplitter.Split(StoryText).Select(text => new StorySentence { SpanishText = text }).ToList()
                : StoryEditing.Snapshot(_original).Sentences
        };
        var errors = StoryValidation.Validate(story);
        TitleError = errors.GetValueOrDefault(nameof(Story.Title), string.Empty);
        LevelError = errors.GetValueOrDefault(nameof(Story.Level), string.Empty);
        StoryTextError = errors.GetValueOrDefault(nameof(Story.Sentences), string.Empty);
        ErrorMessage = string.Empty;
        if (errors.Count != 0) return;

        IsSaving = true;
        Story saved;
        try
        {
            foreach (var (sentence, original) in StoryEditing.MatchSentences(story.Sentences, _original.Sentences))
            {
                if (original is not null)
                {
                    sentence.EnglishText = original.EnglishText;
                    continue;
                }
                try
                {
                    var english = await translationService.TranslateAsync(sentence.SpanishText.Trim());
                    sentence.EnglishText = english?.Trim() ?? string.Empty;
                }
                catch (Exception)
                {
                    // As with import, failed inference must never lose Spanish
                    // edits. The reader can supply a missing translation later.
                    sentence.EnglishText = string.Empty;
                }
            }
            saved = await repository.UpdateAsync(story);
            // Navigation errors must not allow an already committed update to be
            // submitted again or prompt to discard changes that are already saved.
            _committed = true;
        }
        catch (Exception)
        {
            ErrorMessage = "The story could not be saved. Your changes are still here. Please try again.";
            return;
        }
        finally
        {
            IsSaving = false;
        }
        navigationService.Navigate<MainPageViewModel>(new LibraryChange(saved, LibraryChangeKind.Updated));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Back() => navigationService.Navigate<MainPageViewModel>();
}
