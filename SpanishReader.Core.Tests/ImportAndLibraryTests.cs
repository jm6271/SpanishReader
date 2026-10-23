using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Core.Tests;

public class ImportAndLibraryTests
{
    [Theory]
    [InlineData("", "Texto", 0, true, false, false)]
    [InlineData(" \t\r\n", "Texto", 0, true, false, false)]
    [InlineData("Título", " \r\n\t", 0, false, true, false)]
    [InlineData("Título", "Texto", 99, false, false, true)]
    public async Task InvalidSubmissionShowsFieldErrorsAndDoesNotSave(
        string title, string text, int level, bool titleError, bool textError, bool levelError)
    {
        var repository = new FakeRepository();
        var navigation = new FakeNavigation();
        var translation = new FakeTranslation();
        var vm = new ImportPageViewModel(repository, navigation, translation) { Title = title, StoryText = text, Level = (CEFR)level };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(titleError, vm.TitleError.Length > 0);
        Assert.Equal(textError, vm.StoryTextError.Length > 0);
        Assert.Equal(levelError, vm.LevelError.Length > 0);
        Assert.Equal(0, repository.AddCalls);
        Assert.Empty(translation.Calls);
        Assert.Null(navigation.Target);
    }

    [Fact]
    public async Task SuccessfulSaveClearsDraftAndNavigatesWithSavedStory()
    {
        var repository = new FakeRepository();
        var navigation = new FakeNavigation();
        var vm = ValidImport(repository, navigation);
        vm.Level = CEFR.C2;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(typeof(MainPageViewModel), navigation.Target);
        Assert.Same(repository.Saved, navigation.Parameter);
        Assert.Empty(vm.Title);
        Assert.Empty(vm.EnglishTitle);
        Assert.Empty(vm.Author);
        Assert.Empty(vm.StoryText);
        Assert.Equal(CEFR.A1, vm.Level);
        Assert.False(vm.IsSaving);
        Assert.True(vm.CanEdit);
    }

    [Fact]
    public async Task SaveFailurePreservesDraftAndAllowsRetry()
    {
        var repository = new FakeRepository { AddFailure = new IOException("Storage unavailable") };
        var navigation = new FakeNavigation();
        var vm = ValidImport(repository, navigation);
        vm.EnglishTitle = "English";
        vm.Author = "Author";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal("Título", vm.Title);
        Assert.Equal("English", vm.EnglishTitle);
        Assert.Equal("Author", vm.Author);
        Assert.Equal("  Texto\n\nFin. ", vm.StoryText);
        Assert.Null(navigation.Target);
        Assert.True(vm.SaveCommand.CanExecute(null));
        repository.AddFailure = null;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, repository.AddCalls);
        Assert.Empty(vm.ErrorMessage);
        Assert.Equal(typeof(MainPageViewModel), navigation.Target);
    }

    [Fact]
    public async Task PendingSaveDisablesEditingBackAndDuplicateSubmissions()
    {
        var pending = new TaskCompletionSource<Story>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new FakeRepository { PendingAdd = pending.Task };
        var navigation = new FakeNavigation();
        var vm = ValidImport(repository, navigation);
        var saving = vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaving);
        Assert.False(vm.CanEdit);
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.False(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, repository.AddCalls);
        Assert.Null(navigation.Target);
        pending.SetResult(new Story { Id = 1, Title = "Título" });
        await saving;
        Assert.False(vm.IsSaving);
        Assert.Equal(typeof(MainPageViewModel), navigation.Target);
    }

    [Fact]
    public void BackPreservesSessionDraft()
    {
        var navigation = new FakeNavigation();
        var vm = ValidImport(new FakeRepository(), navigation);
        vm.BackCommand.Execute(null);
        Assert.Equal(typeof(MainPageViewModel), navigation.Target);
        Assert.Equal("Título", vm.Title);
        Assert.Equal("  Texto\n\nFin. ", vm.StoryText);
    }

    [Fact]
    public async Task ValidationErrorsClearAfterCorrection()
    {
        var vm = new ImportPageViewModel(new FakeRepository(), new FakeNavigation(), new FakeTranslation());
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.TitleError);
        vm.Title = "Título";
        vm.StoryText = "Texto";
        Assert.Empty(vm.TitleError);
        Assert.Empty(vm.StoryTextError);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(vm.TitleError);
        Assert.Empty(vm.StoryTextError);
    }

    [Fact]
    public async Task NavigationFailureAfterCommitDoesNotRestoreAnImportableDraft()
    {
        var repository = new FakeRepository();
        var vm = ValidImport(repository, new FakeNavigation { Failure = new InvalidOperationException("Navigation failed") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveCommand.ExecuteAsync(null));
        Assert.Equal(1, repository.AddCalls);
        Assert.Empty(vm.Title);
        Assert.Empty(vm.StoryText);
        Assert.Empty(vm.ErrorMessage);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, repository.AddCalls);
    }

    [Fact]
    public async Task LibraryRefreshKeepsCollectionInstanceAndReplacesItsContents()
    {
        var repository = new FakeRepository { Items = [new Story { Id = 1, Title = "Primera" }] };
        var vm = new MainPageViewModel(repository, new FakeNavigation());
        var collection = vm.Stories;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.IsEmpty);
        repository.Items = [new Story { Id = 2, Title = "Segunda" }];
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Same(collection, vm.Stories);
        Assert.Equal(2, Assert.Single(vm.Stories).Id);
        repository.Items = [];
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task LibraryLoadFailureCanBeRetriedWithoutResavingCommittedStory()
    {
        var repository = new FakeRepository { LoadFailure = new IOException("Read failed") };
        var vm = new MainPageViewModel(repository, new FakeNavigation());
        vm.PrepareForDisplay(new Story { Title = "Guardada" });
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Contains("Guardada", vm.SuccessMessage);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsLoading);
        repository.LoadFailure = null;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Empty(vm.ErrorMessage);
        Assert.True(vm.IsEmpty);
        Assert.Equal(0, repository.AddCalls);
        vm.PrepareForDisplay(null);
        Assert.Empty(vm.SuccessMessage);
    }

    [Fact]
    public async Task RefreshRequestedDuringAnEarlierReadLoadsAgain()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<Story>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new FakeRepository { PendingLoad = pending.Task };
        var vm = new MainPageViewModel(repository, new FakeNavigation());
        var firstLoad = vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsLoading);
        repository.Items = [new Story { Id = 2, Title = "New import" }];
        await vm.LoadCommand.ExecuteAsync(null);
        pending.SetResult([new Story { Id = 1, Title = "Older snapshot" }]);
        await firstLoad;
        Assert.Equal(2, Assert.Single(vm.Stories).Id);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task TranslatesEachTrimmedSentenceAndPreservesOriginalText()
    {
        var repository = new FakeRepository();
        var translation = new FakeTranslation();
        var vm = ValidImport(repository, new FakeNavigation(), translation);
        vm.StoryText = "  ¡Hola!\r\n\r\n  ¿Cómo estás?\nFin.  ";
        var text = vm.StoryText;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "¡Hola!", "¿Cómo estás?", "Fin." }, translation.Calls);
        var saved = Assert.IsType<Story>(repository.Saved);
        Assert.Equal(text, string.Concat(saved.Sentences.Select(sentence => sentence.SpanishText)));
        Assert.Equal(translation.Calls.Select(value => $"English: {value}"), saved.Sentences.Select(s => s.EnglishText));
        Assert.Equal(new[] { 0, 1, 2 }, saved.Sentences.Select(s => s.Position));
    }

    [Fact]
    public async Task TranslationFailureDoesNotSkipLaterSentencesOrPreventSave()
    {
        var repository = new FakeRepository();
        var navigation = new FakeNavigation();
        var translation = new FakeTranslation
        {
            Translate = text => text == "Segunda." ? Task.FromException<string>(new IOException("Engine failed"))
                : Task.FromResult($"English: {text}")
        };
        var vm = ValidImport(repository, navigation, translation);
        vm.StoryText = "Primera. Segunda. Tercera.";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(3, translation.Calls.Count);
        var saved = Assert.IsType<Story>(repository.Saved);
        Assert.Equal(new[] { "English: Primera.", "", "English: Tercera." }, saved.Sentences.Select(s => s.EnglishText));
        Assert.Empty(vm.ErrorMessage);
        Assert.Empty(vm.StoryText);
        Assert.Equal(typeof(MainPageViewModel), navigation.Target);
        var library = new MainPageViewModel(repository, navigation);
        library.PrepareForDisplay(saved);
        Assert.Empty(library.SuccessMessage);
        Assert.Contains("1 sentence has no English translation", library.ImportWarningMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllMissingTranslationsStillSaveAndReportTheirCount(bool throws)
    {
        var repository = new FakeRepository();
        var translation = new FakeTranslation
        {
            Translate = _ => throws ? Task.FromException<string>(new IOException("Unavailable")) : Task.FromResult(" \r\n ")
        };
        var vm = ValidImport(repository, new FakeNavigation(), translation);
        await vm.SaveCommand.ExecuteAsync(null);
        var saved = Assert.IsType<Story>(repository.Saved);
        Assert.Equal(2, translation.Calls.Count);
        Assert.All(saved.Sentences, sentence => Assert.Empty(sentence.EnglishText));
        var library = new MainPageViewModel(repository, new FakeNavigation());
        library.PrepareForDisplay(saved);
        Assert.Contains("2 sentences have no English translation", library.ImportWarningMessage);
        library.PrepareForDisplay(new Story { Title = "Completa", Sentences = [new() { SpanishText = "Hola.", EnglishText = "Hello." }] });
        Assert.Empty(library.ImportWarningMessage);
        Assert.Contains("Completa", library.SuccessMessage);
        library.PrepareForDisplay(null);
        Assert.Empty(library.ImportWarningMessage);
        Assert.Empty(library.SuccessMessage);
    }

    [Fact]
    public async Task PendingTranslationDisablesEditingAndOnlySavesAfterCompletion()
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new FakeRepository();
        var navigation = new FakeNavigation();
        var translation = new FakeTranslation { Translate = _ => pending.Task };
        var vm = ValidImport(repository, navigation, translation);
        var saving = vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaving);
        Assert.False(vm.CanEdit);
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal(0, repository.AddCalls);
        Assert.Single(translation.Calls);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Single(translation.Calls);
        Assert.Null(navigation.Target);
        pending.SetResult("  English translation  ");
        await saving;
        Assert.Equal(2, translation.Calls.Count);
        Assert.Equal(1, repository.AddCalls);
        Assert.All(repository.Saved!.Sentences, sentence => Assert.Equal("English translation", sentence.EnglishText));
        Assert.False(vm.IsSaving);
    }

    private static ImportPageViewModel ValidImport(FakeRepository repository, FakeNavigation navigation, FakeTranslation? translation = null) =>
        new(repository, navigation, translation ?? new FakeTranslation()) { Title = "Título", StoryText = "  Texto\n\nFin. " };

    private sealed class FakeTranslation : ITranslationService
    {
        public List<string> Calls { get; } = [];
        public Func<string, Task<string>> Translate { get; init; } = text => Task.FromResult($"English: {text}");

        public Task<string> TranslateAsync(string spanish, CancellationToken cancellationToken = default)
        {
            Calls.Add(spanish);
            return Translate(spanish);
        }
    }

    private sealed class FakeRepository : IStoryRepository
    {
        public Task UpdateSentenceTranslationAsync(long storyId, long sentenceId, string englishText) =>
            throw new NotSupportedException();

        public int AddCalls { get; private set; }
        public Story? Saved { get; private set; }
        public Exception? AddFailure { get; set; }
        public Exception? LoadFailure { get; set; }
        public Task<Story>? PendingAdd { get; set; }
        public Task<IReadOnlyList<Story>>? PendingLoad { get; set; }
        public IReadOnlyList<Story> Items { get; set; } = [];

        public Task<Story> AddAsync(Story story)
        {
            AddCalls++;
            if (AddFailure is not null) return Task.FromException<Story>(AddFailure);
            Saved = story;
            return PendingAdd ?? Task.FromResult(story);
        }

        public Task<IReadOnlyList<Story>> GetAllAsync()
        {
            if (PendingLoad is not null)
            {
                var pending = PendingLoad;
                PendingLoad = null;
                return pending;
            }
            return LoadFailure is null ? Task.FromResult(Items) : Task.FromException<IReadOnlyList<Story>>(LoadFailure);
        }
    }

    private sealed class FakeNavigation : INavigationService
    {
        public Type? Target { get; private set; }
        public object? Parameter { get; private set; }
        public Exception? Failure { get; set; }

        public void Navigate<T>(object? parameter = null) where T : class
        {
            if (Failure is not null) throw Failure;
            Target = typeof(T);
            Parameter = parameter;
        }
    }
}
