using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;

namespace SpanishReader.Core.ViewModels;

public partial class ReadingPageViewModel(
    INavigationService navigationService, ITranslationService translationService,
    IStoryRepository repository) : ObservableObject
{
    private Story? _story;
    private StorySentence? _selectedSentence;
    private TranslationWork? _selectedWork;
    private long _selectionVersion;
    private bool _active;

    // Keep pending work and unsaved English across page visits. Reopening a story
    // while its native translation is still running must not queue a duplicate.
    private readonly Dictionary<(long StoryId, long SentenceId), TranslationWork> _work = [];

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string EnglishTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedSpanishText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedEnglishText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasSelection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial string SaveWarningMessage { get; set; } = string.Empty;

    public bool CanRetry => !IsBusy && (ErrorMessage.Length > 0 || SaveWarningMessage.Length > 0);
    public IReadOnlyList<StorySentence> Sentences => _story?.Sentences ?? [];

    public void PrepareForDisplay(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        _selectionVersion++;
        _active = true;
        _story = story;
        _selectedSentence = null;
        _selectedWork = null;
        Title = story.Title;
        EnglishTitle = story.EnglishTitle;
        SelectedSpanishText = SelectedEnglishText = ErrorMessage = SaveWarningMessage = string.Empty;
        HasSelection = IsBusy = false;
        OnPropertyChanged(nameof(Sentences));
    }

    public void Deactivate()
    {
        // Native inference cannot be interrupted once started. Let it finish and
        // save for its original sentence, but invalidate all pending UI updates.
        _active = false;
        _selectionVersion++;
        _selectedWork = null;
        IsBusy = false;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SelectSentenceAsync(StorySentence? sentence)
    {
        if (!_active || _story is null || sentence is null || !_story.Sentences.Contains(sentence)) return;
        var story = _story;
        var version = ++_selectionVersion;
        _selectedSentence = sentence;
        _selectedWork = null;
        HasSelection = true;
        SelectedSpanishText = sentence.SpanishText.Trim();
        SelectedEnglishText = sentence.EnglishText.Trim();
        ErrorMessage = SaveWarningMessage = string.Empty;
        IsBusy = false;

        var key = (story.Id, sentence.Id);
        if (!_work.TryGetValue(key, out var work))
        {
            if (!string.IsNullOrWhiteSpace(sentence.EnglishText)) return;
            work = new TranslationWork();
            _work.Add(key, work);
        }

        _selectedWork = work;
        SelectedEnglishText = work.EnglishText;
        IsBusy = true;
        // Reuse an in-flight operation; a retry after a save failure retains its
        // generated English, so only persistence runs again.
        if (work.Pending is null || work.Pending.IsCompleted)
            work.Pending = TranslateAndSaveAsync(story.Id, sentence, work);
        var result = await work.Pending;

        if (!_active || version != _selectionVersion) return;
        SelectedEnglishText = work.EnglishText;
        if (work.EnglishText.Length > 0) sentence.EnglishText = work.EnglishText;
        ErrorMessage = result.Error;
        SaveWarningMessage = result.SaveWarning;
        IsBusy = false;
    }

    private async Task<TranslationResult> TranslateAndSaveAsync(long storyId, StorySentence sentence, TranslationWork work)
    {
        if (work.EnglishText.Length == 0)
        {
            try
            {
                var english = await translationService.TranslateAsync(sentence.SpanishText.Trim());
                if (string.IsNullOrWhiteSpace(english))
                    return new("The sentence could not be translated. Please try again.", string.Empty);
                work.EnglishText = english.Trim();
                sentence.EnglishText = work.EnglishText;
                // Show the result while SQLite is still saving, provided the user
                // is still viewing this operation's sentence.
                if (_active && ReferenceEquals(_selectedWork, work))
                    SelectedEnglishText = work.EnglishText;
            }
            catch (Exception)
            {
                return new("The sentence could not be translated. Please try again.", string.Empty);
            }
        }

        if (!work.IsSaved)
        {
            try
            {
                await repository.UpdateSentenceTranslationAsync(storyId, sentence.Id, work.EnglishText);
                work.IsSaved = true;
            }
            catch (Exception)
            {
                return new(string.Empty, "The translation is available for this session, but could not be saved. Please try again.");
            }
        }
        return new(string.Empty, string.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanRetry), AllowConcurrentExecutions = true)]
    private Task RetryAsync() => SelectSentenceAsync(_selectedSentence);

    [RelayCommand]
    private void Back()
    {
        Deactivate();
        navigationService.Navigate<MainPageViewModel>();
    }

    private sealed class TranslationWork
    {
        public string EnglishText { get; set; } = string.Empty;
        public bool IsSaved { get; set; }
        public Task<TranslationResult>? Pending { get; set; }
    }

    private sealed record TranslationResult(string Error, string SaveWarning);
}
