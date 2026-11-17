using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.ViewModels;

public partial class ReadingPageViewModel(
    INavigationService navigationService, ITranslationService translationService,
    IStoryRepository repository, ILLMService llm, ModelDownloadService models) : ObservableObject
{
    private Story? _story;
    private StorySentence? _selectedSentence;
    private TranslationWork? _selectedWork;
    private long _selectionVersion;
    private bool _active;
    private bool _retryRetranslation;
    private readonly Dictionary<(long StoryId, long SentenceId), TranslationWork> _llmWork = [];

    // Keep pending work and unsaved English across page visits. Reopening a story
    // while its native translation is still running must not queue a duplicate.
    private readonly Dictionary<(long StoryId, long SentenceId), TranslationWork> _work = [];

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string EnglishTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedSpanishText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedEnglishText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetranslate))]
    [NotifyCanExecuteChangedFor(nameof(RetranslateCommand))]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(CanRetranslate))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetranslateCommand))]
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
    public bool CanRetranslate => _active && HasSelection && !IsBusy && models.IsInstalled;
    public IReadOnlyList<StorySentence> Sentences => _story?.Sentences ?? [];

    public void PrepareForDisplay(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        _selectionVersion++;
        _active = true;
        _story = story;
        _selectedSentence = null;
        _selectedWork = null;
        _retryRetranslation = false;
        Title = story.Title;
        EnglishTitle = story.EnglishTitle;
        SelectedSpanishText = SelectedEnglishText = ErrorMessage = SaveWarningMessage = string.Empty;
        HasSelection = IsBusy = false;
        OnPropertyChanged(nameof(CanRetranslate));
        RetranslateCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(CanRetranslate));
        RetranslateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SelectSentenceAsync(StorySentence? sentence)
    {
        if (!_active || _story is null || sentence is null || !_story.Sentences.Contains(sentence)) return;
        var story = _story;
        var version = ++_selectionVersion;
        _selectedSentence = sentence;
        _selectedWork = null;
        _retryRetranslation = false;
        HasSelection = true;
        SelectedSpanishText = sentence.SpanishText.Trim();
        SelectedEnglishText = sentence.EnglishText.Trim();
        ErrorMessage = SaveWarningMessage = string.Empty;
        IsBusy = false;

        var key = (story.Id, sentence.Id);
        if (_llmWork.TryGetValue(key, out var llmWork))
        {
            if (llmWork.EnglishText.Length > 0)
            {
                SelectedEnglishText = llmWork.EnglishText;
                sentence.EnglishText = llmWork.EnglishText;
            }
            if (llmWork.Pending is { IsCompleted: false })
            {
                IsBusy = true;
                await llmWork.Pending;
                if (!_active || version != _selectionVersion) return;
                SelectedEnglishText = sentence.EnglishText;
                IsBusy = false;
            }
            if (!llmWork.IsSaved && llmWork.EnglishText.Length > 0)
            {
                SaveWarningMessage = "The translation is available for this session, but could not be saved. Please try again.";
                _retryRetranslation = true;
            }
            if (!string.IsNullOrWhiteSpace(sentence.EnglishText)) return;
        }
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

    [RelayCommand(CanExecute = nameof(CanRetranslate), AllowConcurrentExecutions = true)]
    private async Task RetranslateAsync()
    {
        if (!CanRetranslate || _story is null || _selectedSentence is null) return;
        var story = _story;
        var sentence = _selectedSentence;
        var version = _selectionVersion;
        var key = (story.Id, sentence.Id);
        var work = new TranslationWork();
        _llmWork[key] = work;
        _retryRetranslation = true;
        ErrorMessage = SaveWarningMessage = string.Empty;
        IsBusy = true;
        work.Pending = RetranslateAndSaveAsync(story, sentence, work, version);
        var result = await work.Pending;
        if (!_active || version != _selectionVersion) return;
        SelectedEnglishText = sentence.EnglishText;
        ErrorMessage = result.Error;
        SaveWarningMessage = result.SaveWarning;
        IsBusy = false;
    }

    private async Task<TranslationResult> RetranslateAndSaveAsync(Story story, StorySentence sentence, TranslationWork work, long version)
    {
        try
        {
            var english = ContextualTranslationPrompt.CleanResponse(await llm.ChatAsync(
                ContextualTranslationPrompt.System, ContextualTranslationPrompt.Create(story, sentence)));
            if (string.IsNullOrWhiteSpace(english))
                return new("The sentence could not be retranslated. Please try again.", string.Empty);
            work.EnglishText = english.Trim();
            sentence.EnglishText = work.EnglishText;
            if (_active && version == _selectionVersion && ReferenceEquals(_selectedSentence, sentence))
                SelectedEnglishText = work.EnglishText;
        }
        catch (Exception)
        {
            return new("The sentence could not be retranslated. Please try again.", string.Empty);
        }
        return await SaveRetranslationAsync(story.Id, sentence, work);
    }

    private async Task<TranslationResult> SaveRetranslationAsync(long storyId, StorySentence sentence, TranslationWork work)
    {
        try
        {
            await repository.UpdateSentenceTranslationAsync(storyId, sentence.Id, work.EnglishText);
            work.IsSaved = true;
            return new(string.Empty, string.Empty);
        }
        catch (Exception)
        {
            return new(string.Empty, "The translation is available for this session, but could not be saved. Please try again.");
        }
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
    private async Task RetryAsync()
    {
        if (_retryRetranslation && _story is { } story && _selectedSentence is { } sentence)
        {
            if (ErrorMessage.Length > 0)
            {
                await RetranslateAsync();
                return;
            }
            if (_llmWork.TryGetValue((story.Id, sentence.Id), out var work) && work.EnglishText.Length > 0)
            {
                var version = _selectionVersion;
                IsBusy = true;
                var result = await SaveRetranslationAsync(story.Id, sentence, work);
                if (!_active || version != _selectionVersion) return;
                SaveWarningMessage = result.SaveWarning;
                IsBusy = false;
                return;
            }
        }
        await SelectSentenceAsync(_selectedSentence);
    }

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
