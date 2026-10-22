using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.ViewModels;

public partial class ImportPageViewModel(
    IStoryRepository repository, INavigationService navigationService, ITranslationService translationService) : ObservableObject
{
    public IReadOnlyList<CEFR> Levels { get; } = Enum.GetValues<CEFR>();

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string EnglishTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial CEFR Level { get; set; } = CEFR.A1;
    [ObservableProperty] public partial string Author { get; set; } = string.Empty;
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

    // Once a submitted field is corrected, remove its stale error immediately.
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

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        // Also guard programmatic re-entry, so a second submission cannot insert twice.
        if (IsSaving) return;
        var story = new Story
        {
            Title = Title, EnglishTitle = EnglishTitle, Level = Level, Author = Author,
            Sentences = SpanishSentenceSplitter.Split(StoryText).Select((text, position) => new StorySentence
            {
                Position = position, SpanishText = text
            }).ToList()
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
            // The native engine serializes inference. Await each sentence to keep
            // ordering predictable without filling the thread pool with queued work.
            foreach (var sentence in story.Sentences)
            {
                try
                {
                    var english = await translationService.TranslateAsync(sentence.SpanishText.Trim());
                    sentence.EnglishText = string.IsNullOrWhiteSpace(english) ? string.Empty : english.Trim();
                }
                catch (Exception)
                {
                    // Translation is best-effort. Preserve the Spanish sentence and
                    // continue; an empty translation can be filled in by the reader later.
                    sentence.EnglishText = string.Empty;
                }
            }
            saved = await repository.AddAsync(story);
        }
        catch (Exception)
        {
            ErrorMessage = "The story could not be saved. Your draft is still here. Please try again.";
            return;
        }
        finally
        {
            IsSaving = false;
        }

        // A committed insert is never retried because navigation or the next load failed.
        Title = EnglishTitle = Author = StoryText = string.Empty;
        Level = CEFR.A1;
        navigationService.Navigate<MainPageViewModel>(saved);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Back() => navigationService.Navigate<MainPageViewModel>();
}
