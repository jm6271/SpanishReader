using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Core.Tests;

public sealed class EditingTests
{
    [Fact]
    public void EditorSnapshotAndDiscardDetectionDoNotMutateLibraryObjects()
    {
        var story = Original();
        var vm = Editor(new Repository(), new Navigation());
        vm.PrepareForDisplay(story);
        Assert.Equal("  Hola.\r\n\r\nFin.  ", vm.StoryText);
        Assert.False(vm.HasChanges);
        story.Sentences[0].SpanishText = "External change";
        Assert.Equal("  Hola.\r\n\r\nFin.  ", vm.StoryText);
        vm.Title = "Cambio";
        vm.StoryText = "Otro texto.";
        Assert.True(vm.HasChanges);
        Assert.Equal("Original", story.Title);
        vm.PrepareForDisplay(Original()); // Reopening discards the cancelled draft.
        Assert.False(vm.HasChanges);
        Assert.Equal("Original", vm.Title);
    }

    [Fact]
    public async Task TextBoxLineEndingNormalizationDoesNotCreateDirtyEditsOrRewriteStoredText()
    {
        var story = Original();
        var repository = new Repository();
        var translation = new Translation();
        var vm = Editor(repository, new Navigation(), translation);
        vm.PrepareForDisplay(story);
        vm.StoryText = vm.StoryText.ReplaceLineEndings("\r");
        Assert.False(vm.HasChanges);
        vm.Author = "New author";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(translation.Calls);
        Assert.Equal(story.Sentences.Select(s => s.SpanishText), repository.Saved!.Sentences.Select(s => s.SpanishText));
    }

    [Fact]
    public async Task MetadataSavePreservesTranslationsAndReportsUpdate()
    {
        var repository = new Repository();
        var navigation = new Navigation();
        var translation = new Translation();
        var vm = Editor(repository, navigation, translation);
        vm.PrepareForDisplay(Original());
        vm.Title = "Editada";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(translation.Calls);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal("Hello.", repository.Saved!.Sentences[0].EnglishText);
        Assert.Equal("The end.", repository.Saved.Sentences[1].EnglishText);
        var change = Assert.IsType<LibraryChange>(navigation.Parameter);
        Assert.Equal(LibraryChangeKind.Updated, change.Kind);
        Assert.False(vm.HasChanges);
        Assert.False(vm.SaveCommand.CanExecute(null));
        var library = new MainPageViewModel(repository, navigation);
        library.PrepareForDisplay(change);
        Assert.Contains("Updated “Editada”", library.SuccessMessage);
    }

    [Theory]
    [InlineData("", "Hola.", CEFR.A1)]
    [InlineData("Título", "  ", CEFR.A1)]
    [InlineData("Título", "Hola.", (CEFR)99)]
    public async Task InvalidEditsNeverTranslateOrSave(string title, string text, CEFR level)
    {
        var repository = new Repository();
        var translation = new Translation();
        var vm = Editor(repository, new Navigation(), translation);
        vm.PrepareForDisplay(Original());
        vm.Title = title;
        vm.StoryText = text;
        vm.Level = level;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Empty(translation.Calls);
        Assert.True(vm.TitleError.Length + vm.StoryTextError.Length + vm.LevelError.Length > 0);
    }

    [Fact]
    public async Task RepeatedSentencesReuseTranslationsInOccurrenceOrderAndOnlyChangesTranslate()
    {
        var original = Original();
        original.Sentences =
        [
            new() { SpanishText = "Hola. ", EnglishText = "First hello" },
            new() { SpanishText = "Hola.", EnglishText = "Second hello" }
        ];
        var repository = new Repository();
        var translation = new Translation();
        var vm = Editor(repository, new Navigation(), translation);
        vm.PrepareForDisplay(original);
        vm.StoryText = "\nHola.\n\nHola. Nueva. Hola.";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Nueva.", "Hola." }, translation.Calls);
        Assert.Equal(new[] { "First hello", "Second hello", "English: Nueva.", "English: Hola." },
            repository.Saved!.Sentences.Select(s => s.EnglishText));
        Assert.Equal(vm.StoryText, string.Concat(repository.Saved.Sentences.Select(s => s.SpanishText)));
    }

    [Fact]
    public async Task TranslationFailureStillSavesSpanishAndReportsMissingCount()
    {
        var repository = new Repository();
        var navigation = new Navigation();
        var vm = Editor(repository, navigation, new Translation
        {
            Translate = text => text == "Nueva." ? Task.FromException<string>(new IOException()) : Task.FromResult("New end")
        });
        vm.PrepareForDisplay(Original());
        vm.StoryText = "Nueva. Otra.";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("", repository.Saved!.Sentences[0].EnglishText);
        Assert.Equal("New end", repository.Saved.Sentences[1].EnglishText);
        var library = new MainPageViewModel(repository, navigation);
        library.PrepareForDisplay(navigation.Parameter);
        Assert.Contains("Updated", library.ImportWarningMessage);
        Assert.Contains("1 sentence has no English translation", library.ImportWarningMessage);
    }

    [Fact]
    public async Task PendingTranslationAndSaveBlockDuplicateSubmissionsAndFailureRetainsDraft()
    {
        var pending = new TaskCompletionSource<string>();
        var repository = new Repository { UpdateFailure = new IOException() };
        var navigation = new Navigation();
        var vm = Editor(repository, navigation, new Translation { Translate = _ => pending.Task });
        vm.PrepareForDisplay(Original());
        vm.StoryText = "Nueva.";
        var saving = vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaving);
        Assert.False(vm.CanEdit);
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.False(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, repository.UpdateCalls);
        pending.SetResult("New.");
        await saving;
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal("Nueva.", vm.StoryText);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.True(vm.HasChanges);
        Assert.Null(navigation.Target);
        repository.UpdateFailure = null;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, repository.UpdateCalls);
        Assert.Empty(vm.ErrorMessage);
        Assert.Equal(typeof(MainPageViewModel), navigation.Target);
    }

    [Fact]
    public async Task CommittedUpdateCannotBeRepeatedAfterNavigationFailure()
    {
        var repository = new Repository();
        var vm = Editor(repository, new Navigation { Failure = new InvalidOperationException() });
        vm.PrepareForDisplay(Original());
        vm.Title = "Guardada";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveCommand.ExecuteAsync(null));
        Assert.False(vm.HasChanges);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public async Task DeleteWaitsForCommitPreventsReentryAndLeavesRowOnFailure()
    {
        var pending = new TaskCompletionSource();
        var story = Original();
        var repository = new Repository { Items = [story], Delete = _ => pending.Task };
        var vm = new MainPageViewModel(repository, new Navigation());
        await vm.LoadCommand.ExecuteAsync(null);
        var deleting = vm.DeleteStoryCommand.ExecuteAsync(story);
        Assert.True(vm.IsDeleting);
        Assert.Single(vm.Stories);
        Assert.False(vm.EditStoryCommand.CanExecute(story));
        Assert.False(vm.OpenStoryCommand.CanExecute(story));
        await vm.DeleteStoryCommand.ExecuteAsync(story);
        Assert.Equal(1, repository.DeleteCalls);
        pending.SetException(new IOException());
        await deleting;
        Assert.Single(vm.Stories);
        Assert.NotEmpty(vm.ActionErrorMessage);
        repository.Delete = _ => Task.CompletedTask;
        await vm.DeleteStoryCommand.ExecuteAsync(story);
        Assert.Empty(vm.Stories);
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.ActionErrorMessage);
        Assert.Contains("Deleted", vm.SuccessMessage);
    }

    [Fact]
    public async Task DeleteQueuesRefreshBehindAnOlderLibraryRead()
    {
        var story = Original();
        var pending = new TaskCompletionSource<IReadOnlyList<Story>>();
        var repository = new Repository { Items = [story], PendingLoad = pending.Task };
        var navigation = new Navigation();
        var vm = new MainPageViewModel(repository, navigation);
        vm.EditStoryCommand.Execute(story);
        Assert.Equal(typeof(EditStoryPageViewModel), navigation.Target);
        Assert.Same(story, navigation.Parameter);
        var loading = vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteStoryCommand.ExecuteAsync(story);
        pending.SetResult([story]);
        await loading;
        Assert.Empty(vm.Stories);
        Assert.True(vm.IsEmpty);
    }

    private static Story Original() => new()
    {
        Id = 7, Title = "Original", DateAdded = DateTime.UtcNow,
        Sentences =
        [
            new() { Id = 11, StoryId = 7, SpanishText = "  Hola.\r\n\r\n", EnglishText = "Hello." },
            new() { Id = 12, StoryId = 7, Position = 1, SpanishText = "Fin.  ", EnglishText = "The end." }
        ]
    };

    private static EditStoryPageViewModel Editor(Repository repository, Navigation navigation, Translation? translation = null) =>
        new(repository, navigation, translation ?? new Translation());

    private sealed class Translation : ITranslationService
    {
        public List<string> Calls { get; } = [];
        public Func<string, Task<string>> Translate { get; init; } = text => Task.FromResult($"English: {text}");
        public Task<string> TranslateAsync(string spanish, CancellationToken cancellationToken = default)
        {
            Calls.Add(spanish);
            return Translate(spanish);
        }
    }

    private sealed class Navigation : INavigationService
    {
        public Type? Target { get; private set; }
        public object? Parameter { get; private set; }
        public Exception? Failure { get; init; }
        public void Navigate<T>(object? parameter = null) where T : class
        {
            if (Failure is not null) throw Failure;
            Target = typeof(T);
            Parameter = parameter;
        }
    }

    private sealed class Repository : IStoryRepository
    {
        public Story? Saved { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public Exception? UpdateFailure { get; set; }
        public IReadOnlyList<Story> Items { get; set; } = [];
        public Task<IReadOnlyList<Story>>? PendingLoad { get; set; }
        public Func<long, Task> Delete { get; set; } = _ => Task.CompletedTask;
        public Task<Story> AddAsync(Story story) => throw new NotSupportedException();
        public Task UpdateSentenceTranslationAsync(long storyId, long sentenceId, string englishText) => throw new NotSupportedException();
        public Task<Story> UpdateAsync(Story story)
        {
            UpdateCalls++;
            if (UpdateFailure is not null) return Task.FromException<Story>(UpdateFailure);
            Saved = story;
            return Task.FromResult(story);
        }
        public async Task DeleteAsync(long storyId)
        {
            DeleteCalls++;
            await Delete(storyId);
            Items = Items.Where(s => s.Id != storyId).ToArray();
        }
        public Task<IReadOnlyList<Story>> GetAllAsync()
        {
            var pending = PendingLoad;
            PendingLoad = null;
            return pending ?? Task.FromResult(Items);
        }
    }
}
