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
    private bool _retryBergamot;
    private readonly Dictionary<(long StoryId, long SentenceId), TranslationWork> _llmWork = [];

    // Keep pending work and unsaved English across page visits. Reopening a story
    // while its native translation is still running must not queue a duplicate.
    private readonly Dictionary<(long StoryId, long SentenceId), TranslationWork> _work = [];

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string EnglishTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedSpanishText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedEnglishText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    public partial string EditedEnglishText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetranslate))]
    [NotifyPropertyChangedFor(nameof(IsRetranslateAvailable))]
    [NotifyPropertyChangedFor(nameof(CanRetranslateWithBergamot))]
    [NotifyPropertyChangedFor(nameof(CanSaveEdit))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetranslateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetranslateWithBergamotCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool IsEditingTranslation { get; set; }
    [ObservableProperty] public partial string EditSaveError { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetranslate))]
    [NotifyPropertyChangedFor(nameof(IsRetranslateAvailable))]
    [NotifyPropertyChangedFor(nameof(CanRetranslateWithBergamot))]
    [NotifyCanExecuteChangedFor(nameof(RetranslateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetranslateWithBergamotCommand))]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(CanRetranslate))]
    [NotifyPropertyChangedFor(nameof(IsRetranslateAvailable))]
    [NotifyPropertyChangedFor(nameof(CanRetranslateWithBergamot))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetranslateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetranslateWithBergamotCommand))]
    [NotifyPropertyChangedFor(nameof(CanSaveEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial string SaveWarningMessage { get; set; } = string.Empty;

    public bool CanRetry => !IsBusy && !IsEditingTranslation && (ErrorMessage.Length > 0 || SaveWarningMessage.Length > 0);
    // Availability controls visibility; a running request only disables the command.
    public bool IsRetranslateAvailable => _active && HasSelection && !IsEditingTranslation && models.IsInstalled;
    public bool CanRetranslate => IsRetranslateAvailable && !IsBusy;
    public bool CanRetranslateWithBergamot => _active && HasSelection && !IsBusy && !IsEditingTranslation;
    public bool CanSaveEdit => _active && IsEditingTranslation && !IsBusy && !string.IsNullOrWhiteSpace(EditedEnglishText);
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
        _retryBergamot = false;
        Title = story.Title;
        EnglishTitle = story.EnglishTitle;
        SelectedSpanishText = SelectedEnglishText = ErrorMessage = SaveWarningMessage = string.Empty;
        EditedEnglishText = EditSaveError = string.Empty;
        IsEditingTranslation = false;
        HasSelection = IsBusy = false;
        OnPropertyChanged(nameof(CanRetranslate));
        OnPropertyChanged(nameof(IsRetranslateAvailable));
        OnPropertyChanged(nameof(CanRetranslateWithBergamot));
        RetranslateCommand.NotifyCanExecuteChanged();
        RetranslateWithBergamotCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Sentences));
    }

    public void Deactivate()
    {
        // Native inference cannot be interrupted once started. Let it finish and
        // save for its original sentence, but invalidate all pending UI updates.
        _active = false;
        _selectionVersion++;
        _selectedWork = null;
        IsEditingTranslation = false;
        IsBusy = false;
        OnPropertyChanged(nameof(CanRetranslate));
        OnPropertyChanged(nameof(IsRetranslateAvailable));
        OnPropertyChanged(nameof(CanRetranslateWithBergamot));
        RetranslateCommand.NotifyCanExecuteChanged();
        RetranslateWithBergamotCommand.NotifyCanExecuteChanged();
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
        _retryBergamot = false;
        IsEditingTranslation = false;
        EditSaveError = string.Empty;
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

    [RelayCommand]
    private void BeginEdit()
    {
        if (!_active || !HasSelection || IsBusy || _selectedSentence is null) return;
        EditedEnglishText = SelectedEnglishText;
        EditSaveError = string.Empty;
        IsEditingTranslation = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (IsBusy) return;
        IsEditingTranslation = false;
        EditSaveError = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSaveEdit), AllowConcurrentExecutions = true)]
    private async Task SaveEditAsync()
    {
        if (!CanSaveEdit || _story is null || _selectedSentence is null) return;
        var storyId = _story.Id;
        var sentence = _selectedSentence;
        var version = _selectionVersion;
        var english = EditedEnglishText.Trim();
        EditSaveError = string.Empty;
        IsBusy = true;
        try
        {
            // Publish the edit only after SQLite accepts it. A failed save keeps
            // both the original displayed translation and the user's draft.
            await repository.UpdateSentenceTranslationAsync(storyId, sentence.Id, english);
            sentence.EnglishText = english;
            _work.Remove((storyId, sentence.Id));
            _llmWork.Remove((storyId, sentence.Id));
            if (_active && version == _selectionVersion)
            {
                SelectedEnglishText = english;
                IsEditingTranslation = false;
                ErrorMessage = SaveWarningMessage = string.Empty;
                _retryRetranslation = false;
                _retryBergamot = false;
            }
        }
        catch (Exception)
        {
            if (_active && version == _selectionVersion)
                EditSaveError = "The edited translation could not be saved. Please try again.";
        }
        finally
        {
            if (_active && version == _selectionVersion) IsBusy = false;
        }
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
        _retryBergamot = false;
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

    [RelayCommand(CanExecute = nameof(CanRetranslateWithBergamot), AllowConcurrentExecutions = true)]
    private async Task RetranslateWithBergamotAsync()
    {
        if (!CanRetranslateWithBergamot || _story is null || _selectedSentence is null) return;
        var storyId = _story.Id;
        var sentence = _selectedSentence;
        var version = _selectionVersion;
        var key = (storyId, sentence.Id);
        // A fresh request always invokes Bergamot, even when English already
        // exists. Keep its work for a save-only retry if SQLite rejects it.
        var work = new TranslationWork();
        _work[key] = work;
        _llmWork.Remove(key);
        _selectedWork = work;
        _retryRetranslation = false;
        _retryBergamot = true;
        ErrorMessage = SaveWarningMessage = string.Empty;
        IsBusy = true;
        work.Pending = TranslateAndSaveAsync(storyId, sentence, work);
        var result = await work.Pending;
        if (!_active || version != _selectionVersion) return;
        SelectedEnglishText = work.EnglishText.Length > 0 ? work.EnglishText : sentence.EnglishText;
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
        catch (OperationCanceledException)
        {
            return new(string.Empty, string.Empty);
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
        if (_retryBergamot && ErrorMessage.Length > 0)
        {
            await RetranslateWithBergamotAsync();
            return;
        }
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
