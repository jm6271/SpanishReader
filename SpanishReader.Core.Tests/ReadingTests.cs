using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Core.Tests;

public class ReadingTests
{
    [Fact]
    public void LibraryOpensTheClickedStoryAndReaderReturnsToLibrary()
    {
        var navigation = new FakeNavigation();
        var story = Story();
        var library = new MainPageViewModel(new FakeRepository(), navigation);
        library.OpenStoryCommand.Execute(story);
        Assert.Equal(typeof(ReadingPageViewModel), navigation.Target);
        Assert.Same(story, navigation.Parameter);
        var reader = new ReadingPageViewModel(navigation, new FakeTranslation(), new FakeRepository());
        reader.PrepareForDisplay(story);
        reader.BackCommand.Execute(null);
        Assert.Equal(typeof(MainPageViewModel), navigation.Target);
        Assert.Null(navigation.Parameter);
    }

    [Fact]
    public async Task StoredTranslationsDisplayImmediatelyWithoutEngineOrDatabaseWrites()
    {
        var translation = new FakeTranslation();
        var repository = new FakeRepository();
        var vm = Reader(translation, repository);
        var story = Story();
        vm.PrepareForDisplay(story);
        Assert.Equal(story.Title, vm.Title);
        Assert.Equal(story.EnglishTitle, vm.EnglishTitle);
        Assert.False(vm.HasSelection);
        Assert.Equal(story.Sentences, vm.Sentences);

        await vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[1]);
        Assert.Equal("Segunda.", vm.SelectedSpanishText);
        Assert.Equal("Second.", vm.SelectedEnglishText);
        Assert.False(vm.IsBusy);
        Assert.Empty(translation.Calls);
        Assert.Empty(repository.Updates);

        await vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        Assert.Equal("Primera.", vm.SelectedSpanishText);
        Assert.Equal("First.", vm.SelectedEnglishText);
        vm.PrepareForDisplay(new Story { Id = 9, Title = "Otra" });
        Assert.Equal("Otra", vm.Title);
        Assert.Empty(vm.EnglishTitle);
        Assert.Empty(vm.SelectedEnglishText);
        Assert.Empty(vm.SelectedSpanishText);
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public async Task MissingEnglishIsTranslatedTrimmedSavedAndReused()
    {
        var translation = new FakeTranslation { Translate = _ => Task.FromResult("  First.  ") };
        var repository = new FakeRepository();
        var vm = Reader(translation, repository);
        var story = Story(missingFirst: true);
        vm.PrepareForDisplay(story);
        await vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        Assert.Equal("Primera.", Assert.Single(translation.Calls));
        Assert.Equal((1L, 11L, "First."), Assert.Single(repository.Updates));
        Assert.Equal("First.", vm.SelectedEnglishText);
        Assert.Equal("First.", story.Sentences[0].EnglishText);
        Assert.False(vm.CanRetry);
        await vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[1]);
        await vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        Assert.Single(translation.Calls);
        Assert.Single(repository.Updates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedOrBlankTranslationCanBeRetried(bool throws)
    {
        var translation = new FakeTranslation
        {
            Translate = _ => throws ? Task.FromException<string>(new IOException("Missing model")) : Task.FromResult(" \n ")
        };
        var repository = new FakeRepository();
        var vm = Reader(translation, repository);
        var story = Story(missingFirst: true);
        vm.PrepareForDisplay(story);
        await vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        Assert.Equal("Primera.", vm.SelectedSpanishText);
        Assert.Empty(vm.SelectedEnglishText);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.True(vm.RetryCommand.CanExecute(null));
        Assert.Empty(repository.Updates);
        translation.Translate = _ => Task.FromResult("First.");
        await vm.RetryCommand.ExecuteAsync(null);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.CanRetry);
        Assert.Equal("First.", vm.SelectedEnglishText);
        Assert.Equal(2, translation.Calls.Count);
        Assert.Single(repository.Updates);
    }

    [Fact]
    public async Task SaveFailureKeepsEnglishAndRetryDoesNotTranslateAgain()
    {
        var translation = new FakeTranslation();
        var repository = new FakeRepository { Save = () => Task.FromException(new IOException("Locked")) };
        var vm = Reader(translation, repository);
        var story = Story(missingFirst: true);
        vm.PrepareForDisplay(story);
        await vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        Assert.Equal("English: Primera.", vm.SelectedEnglishText);
        Assert.NotEmpty(vm.SaveWarningMessage);
        Assert.Empty(vm.ErrorMessage);
        Assert.True(vm.CanRetry);
        repository.Save = () => Task.CompletedTask;
        await vm.RetryCommand.ExecuteAsync(null);
        Assert.Empty(vm.SaveWarningMessage);
        Assert.Single(translation.Calls);
        Assert.Equal(2, repository.Updates.Count);
    }

    [Fact]
    public async Task GeneratedEnglishIsVisibleWhileSavingAndDuplicateClicksShareWork()
    {
        var pendingTranslation = new TaskCompletionSource<string>();
        var pendingSave = new TaskCompletionSource();
        var translation = new FakeTranslation { Translate = _ => pendingTranslation.Task };
        var repository = new FakeRepository { Save = () => pendingSave.Task };
        var vm = Reader(translation, repository);
        var story = Story(missingFirst: true);
        vm.PrepareForDisplay(story);
        var firstClick = vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        Assert.True(vm.IsBusy);
        Assert.Empty(vm.SelectedEnglishText);
        var secondClick = vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        Assert.Single(translation.Calls);
        pendingTranslation.SetResult("First.");
        Assert.Equal("First.", vm.SelectedEnglishText);
        Assert.True(vm.IsBusy);
        Assert.Single(repository.Updates);
        pendingSave.SetResult();
        await Task.WhenAll(firstClick, secondClick);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LateTranslationDoesNotReplaceAnotherSelectionButStillSaves()
    {
        var pending = new TaskCompletionSource<string>();
        var translation = new FakeTranslation { Translate = _ => pending.Task };
        var repository = new FakeRepository();
        var vm = Reader(translation, repository);
        var story = Story(missingFirst: true);
        vm.PrepareForDisplay(story);
        var firstClick = vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        await vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[1]);
        pending.SetResult("First.");
        await firstClick;
        Assert.Equal("Segunda.", vm.SelectedSpanishText);
        Assert.Equal("Second.", vm.SelectedEnglishText);
        Assert.False(vm.IsBusy);
        Assert.Equal((1L, 11L, "First."), Assert.Single(repository.Updates));
    }

    [Fact]
    public async Task ReopeningDuringTranslationSharesWorkWithFreshStoryInstance()
    {
        var pending = new TaskCompletionSource<string>();
        var translation = new FakeTranslation { Translate = _ => pending.Task };
        var repository = new FakeRepository();
        var vm = Reader(translation, repository);
        var story = Story(missingFirst: true);
        vm.PrepareForDisplay(story);
        var oldClick = vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        vm.Deactivate();
        var fresh = Story(missingFirst: true);
        vm.PrepareForDisplay(fresh);
        var newClick = vm.SelectSentenceCommand.ExecuteAsync(fresh.Sentences[0]);
        pending.SetResult("First.");
        await Task.WhenAll(oldClick, newClick);
        Assert.Single(translation.Calls);
        Assert.Single(repository.Updates);
        Assert.Equal("First.", fresh.Sentences[0].EnglishText);
        Assert.Equal("First.", vm.SelectedEnglishText);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NavigationInvalidatesLateUiUpdates(bool opensAnotherStory)
    {
        var pending = new TaskCompletionSource<string>();
        var repository = new FakeRepository();
        var vm = Reader(new FakeTranslation { Translate = _ => pending.Task }, repository);
        var story = Story(missingFirst: true);
        vm.PrepareForDisplay(story);
        var click = vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        vm.Deactivate();
        if (opensAnotherStory) vm.PrepareForDisplay(new Story { Id = 2, Title = "Otra" });
        var before = vm.SelectedEnglishText;
        pending.SetResult("First.");
        await click;
        Assert.Equal(before, vm.SelectedEnglishText);
        Assert.False(vm.IsBusy);
        Assert.Equal((1L, 11L, "First."), Assert.Single(repository.Updates));
    }

    [Fact]
    public async Task LateFailureDoesNotClearNewerProgressAndForeignSentencesAreIgnored()
    {
        var first = new TaskCompletionSource<string>();
        var second = new TaskCompletionSource<string>();
        var translation = new FakeTranslation { Translate = text => text == "Primera." ? first.Task : second.Task };
        var vm = Reader(translation, new FakeRepository());
        var story = Story(missingFirst: true);
        story.Sentences[1].EnglishText = string.Empty;
        vm.PrepareForDisplay(story);
        await vm.SelectSentenceCommand.ExecuteAsync(new StorySentence { Id = 99, SpanishText = "Otra." });
        Assert.False(vm.HasSelection);
        var firstClick = vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[0]);
        var secondClick = vm.SelectSentenceCommand.ExecuteAsync(story.Sentences[1]);
        first.SetException(new IOException("Failed"));
        await firstClick;
        Assert.True(vm.IsBusy);
        Assert.Empty(vm.ErrorMessage);
        second.SetResult("Second.");
        await secondClick;
        Assert.False(vm.IsBusy);
        Assert.Equal("Second.", vm.SelectedEnglishText);
    }

    private static ReadingPageViewModel Reader(FakeTranslation translation, FakeRepository repository) =>
        new(new FakeNavigation(), translation, repository);

    private static Story Story(bool missingFirst = false) => new()
    {
        Id = 1, Title = "Historia", EnglishTitle = "Story",
        Sentences =
        [
            new() { Id = 11, StoryId = 1, Position = 0, SpanishText = "  Primera.\r\n\r\n", EnglishText = missingFirst ? "" : "First." },
            new() { Id = 12, StoryId = 1, Position = 1, SpanishText = "Segunda.  ", EnglishText = "Second." }
        ]
    };

    private sealed class FakeTranslation : ITranslationService
    {
        public List<string> Calls { get; } = [];
        public Func<string, Task<string>> Translate { get; set; } = text => Task.FromResult($"English: {text}");
        public Task<string> TranslateAsync(string spanish, CancellationToken cancellationToken = default)
        {
            Calls.Add(spanish);
            return Translate(spanish);
        }
    }

    private sealed class FakeRepository : IStoryRepository
    {
        public List<(long StoryId, long SentenceId, string English)> Updates { get; } = [];
        public Func<Task> Save { get; set; } = () => Task.CompletedTask;
        public Task UpdateSentenceTranslationAsync(long storyId, long sentenceId, string englishText)
        {
            Updates.Add((storyId, sentenceId, englishText));
            return Save();
        }
        public Task<Story> AddAsync(Story story) => throw new NotSupportedException();
        public Task<IReadOnlyList<Story>> GetAllAsync() => throw new NotSupportedException();
    }

    private sealed class FakeNavigation : INavigationService
    {
        public Type? Target { get; private set; }
        public object? Parameter { get; private set; }
        public void Navigate<T>(object? parameter = null) where T : class
        {
            Target = typeof(T);
            Parameter = parameter;
        }
    }
}
