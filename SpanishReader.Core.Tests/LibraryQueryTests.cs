using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Core.Tests;

public class LibraryQueryTests
{
    private static Story Story(long id, CEFR level = CEFR.A1, int day = 1) =>
        new() { Id = id, Title = $"Título {id}", Level = level, DateAdded = new DateTime(2026, 1, day) };

    [Theory]
    [InlineData(LibrarySortOrder.NewestFirst, "4,3,2,1")]
    [InlineData(LibrarySortOrder.OldestFirst, "1,2,3,4")]
    [InlineData(LibrarySortOrder.CefrAscending, "3,2,4,1")]
    [InlineData(LibrarySortOrder.CefrDescending, "4,1,3,2")]
    public async Task SortsWithDeterministicTies(LibrarySortOrder sort, string expected)
    {
        var repository = new Repository { Items = [Story(1, CEFR.C2), Story(3, CEFR.A1, 2), Story(2), Story(4, CEFR.C2, 2)] };
        var vm = new MainPageViewModel(repository, new Navigation());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedSortOrder = sort;
        Assert.Equal(expected, string.Join(",", vm.Stories.Select(s => s.Id)));
        Assert.Equal(1, repository.Loads);
    }

    [Fact]
    public async Task EveryLevelAndAllLevelsRestoreOriginalObjectsAndCollection()
    {
        var repository = new Repository { Items = Enum.GetValues<CEFR>().Select((level, i) => Story(i + 1, level)).ToArray() };
        var vm = new MainPageViewModel(repository, new Navigation());
        var collection = vm.Stories;
        await vm.LoadCommand.ExecuteAsync(null);
        foreach (var level in Enum.GetValues<CEFR>())
        {
            vm.SelectedLevel = level;
            Assert.Same(repository.Items.Single(s => s.Level == level), Assert.Single(vm.Stories));
        }
        vm.SelectedLevel = null;
        Assert.Equal(6, vm.Stories.Count);
        Assert.Same(collection, vm.Stories);
        Assert.Equal(1, repository.Loads);
    }

    [Fact]
    public async Task MetadataSearchCombinesWithLevelAndClearsWithoutReloading()
    {
        var a = Story(1); a.Title = "Árbol";
        var b = Story(2, CEFR.B1); b.EnglishTitle = "Forest";
        var c = Story(3); c.Author = "FOREST";
        var vm = new MainPageViewModel(new Repository { Items = [a, b, c] }, new Navigation());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SearchText = "  forest  ";
        Assert.Equal(new long[] { 3, 2 }, vm.Stories.Select(s => s.Id));
        vm.SelectedLevel = CEFR.A1;
        Assert.Same(c, Assert.Single(vm.Stories));
        vm.SearchText = "árBOL";
        Assert.Same(a, Assert.Single(vm.Stories));
        vm.SearchText = "arbol";
        Assert.True(vm.HasNoResults);
        Assert.False(vm.IsEmpty);
        vm.SearchText = " \t ";
        Assert.Equal(2, vm.Stories.Count);
        vm.SelectedLevel = null;
        Assert.Equal(3, vm.Stories.Count);
    }

    [Fact]
    public async Task PendingLoadUsesLatestCriteriaAndSuppressesEmptyFeedback()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<Story>>();
        var vm = new MainPageViewModel(new Repository { Pending = pending.Task }, new Navigation());
        var load = vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedLevel = CEFR.B1;
        vm.SearchText = "Título";
        vm.SelectedSortOrder = LibrarySortOrder.OldestFirst;
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasNoResults);
        pending.SetResult([Story(1), Story(2, CEFR.B1), Story(3, CEFR.B1, 2)]);
        await load;
        Assert.Equal(new long[] { 2, 3 }, vm.Stories.Select(s => s.Id));
    }

    [Fact]
    public async Task RefreshEditDeleteAndRetryKeepCriteriaAndCorrectEmptyStates()
    {
        var story = Story(1, CEFR.B1);
        var repository = new Repository { Items = [story] };
        var vm = new MainPageViewModel(repository, new Navigation()) { SelectedLevel = CEFR.B1, SearchText = "Título", SelectedSortOrder = LibrarySortOrder.OldestFirst };
        await vm.LoadCommand.ExecuteAsync(null);
        // Returning from an edit can move a story outside the active filter.
        story.Level = CEFR.C2;
        vm.PrepareForDisplay(new LibraryChange(story, LibraryChangeKind.Updated));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasNoResults);
        var imported = Story(2, CEFR.B1);
        repository.Items = [story, imported];
        vm.PrepareForDisplay(imported);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Same(imported, Assert.Single(vm.Stories));
        await vm.DeleteStoryCommand.ExecuteAsync(imported);
        Assert.True(vm.HasNoResults);
        repository.Failure = new IOException();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasNoResults);
        Assert.False(vm.IsEmpty);
        repository.Failure = null;
        repository.Items = [];
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasNoResults);
        Assert.Equal(CEFR.B1, vm.SelectedLevel);
        Assert.Equal("Título", vm.SearchText);
        Assert.Equal(LibrarySortOrder.OldestFirst, vm.SelectedSortOrder);
    }

    private sealed class Repository : IStoryRepository
    {
        public IReadOnlyList<Story> Items { get; set; } = [];
        public Task<IReadOnlyList<Story>>? Pending { get; set; }
        public Exception? Failure { get; set; }
        public int Loads { get; private set; }
        public Task<IReadOnlyList<Story>> GetAllAsync()
        {
            Loads++;
            return Failure is not null ? Task.FromException<IReadOnlyList<Story>>(Failure) : Pending ?? Task.FromResult(Items);
        }
        public Task DeleteAsync(long id) { Items = Items.Where(s => s.Id != id).ToArray(); return Task.CompletedTask; }
        public Task<Story> AddAsync(Story story) => throw new NotSupportedException();
        public Task<Story> UpdateAsync(Story story) => throw new NotSupportedException();
        public Task UpdateSentenceTranslationAsync(long storyId, long sentenceId, string text) => throw new NotSupportedException();
    }
    private sealed class Navigation : INavigationService
    {
        public void Navigate<T>(object? parameter = null) where T : class { }
    }
}

