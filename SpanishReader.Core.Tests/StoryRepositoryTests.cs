using Microsoft.Data.Sqlite;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Core.Tests;

public sealed class StoryRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SpanishReader.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "nested", "SpanishReader.db");

    [Fact]
    public async Task FirstReadCreatesAnEmptyVersionedDatabase()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(await repository.GetAllAsync());
        Assert.True(File.Exists(DatabasePath));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(4L, command.ExecuteScalar());
    }

    [Theory]
    [InlineData(CEFR.A1)]
    [InlineData(CEFR.A2)]
    [InlineData(CEFR.B1)]
    [InlineData(CEFR.B2)]
    [InlineData(CEFR.C1)]
    [InlineData(CEFR.C2)]
    public async Task RoundTripPreservesTextAndPersistsAcrossInstances(CEFR level)
    {
        var input = new Story
        {
            Title = "  El niño y la canción 'azul'  ",
            EnglishTitle = "  The boy's song  ",
            Level = level,
            Author = "  María O'Connor  ",
            Sentences =
            [
                new() { SpanishText = "  —¡Hola, señor!\r\n\r\n    ", EnglishText = "Hello, sir!" },
                new() { SpanishText = "¿Cómo estás?\n", EnglishText = "How are you?" },
                new() { SpanishText = "Fin.  " } // Missing translations are valid stored data.
            ]
        };
        var before = DateTime.UtcNow;
        var saved = await new SqliteStoryRepository(DatabasePath).AddAsync(input);
        var loaded = Assert.Single(await new SqliteStoryRepository(DatabasePath).GetAllAsync());
        Assert.True(saved.Id > 0);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(input.Title.Trim(), loaded.Title);
        Assert.Equal(input.EnglishTitle.Trim(), loaded.EnglishTitle);
        Assert.Equal(input.Author.Trim(), loaded.Author);
        Assert.Equal(input.Sentences.Select(s => s.SpanishText), loaded.Sentences.Select(s => s.SpanishText));
        Assert.Equal(input.Sentences.Select(s => s.EnglishText), loaded.Sentences.Select(s => s.EnglishText));
        Assert.Equal(new[] { 0, 1, 2 }, loaded.Sentences.Select(s => s.Position));
        Assert.All(loaded.Sentences, sentence =>
        {
            Assert.True(sentence.Id > 0);
            Assert.Equal(saved.Id, sentence.StoryId);
        });
        Assert.All(input.Sentences, sentence => Assert.Equal(0, sentence.Id));
        Assert.Equal(level, loaded.Level);
        Assert.Equal(saved.DateAdded, loaded.DateAdded);
        Assert.Equal(DateTimeKind.Utc, loaded.DateAdded.Kind);
        Assert.InRange(loaded.DateAdded, before, DateTime.UtcNow);
        Assert.Equal(0, input.Id); // Persistence must not modify the form's input.
    }

    [Fact]
    public async Task OptionalMetadataAndDeliberateDuplicatesAreSupported()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var story = ValidStory();
        var first = await repository.AddAsync(story);
        var second = await repository.AddAsync(story);
        var loaded = await repository.GetAllAsync();
        Assert.Equal(2, loaded.Count);
        Assert.NotEqual(first.Id, second.Id);
        Assert.All(loaded, item =>
        {
            Assert.Empty(item.EnglishTitle);
            Assert.Empty(item.Author);
        });
    }

    [Fact]
    public async Task OrdersNewestFirstAndBreaksTimestampTiesById()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var first = await repository.AddAsync(ValidStory());
        var second = await repository.AddAsync(ValidStory());
        var third = await repository.AddAsync(ValidStory());
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE Stories SET DateAdded = '2026-01-01T00:00:00.0000000Z';
                UPDATE Stories SET DateAdded = '2025-01-01T00:00:00.0000000Z' WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$id", second.Id);
            command.ExecuteNonQuery();
        }
        Assert.Equal(new[] { third.Id, first.Id, second.Id }, (await repository.GetAllAsync()).Select(s => s.Id));
    }

    [Fact]
    public async Task ConcurrentFirstUseDoesNotLoseStories()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.AddAsync(ValidStory())));
        Assert.Equal(8, (await repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task RejectsNewerSchemasWithoutChangingThem()
    {
        await new SqliteStoryRepository(DatabasePath).GetAllAsync();
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version = 5;";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteStoryRepository(DatabasePath).GetAllAsync());
        using var check = Open();
        using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version;";
        Assert.Equal(5L, query.ExecuteScalar());
    }

    [Fact]
    public async Task FailedInitializationCanBeRetried()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "nested"), "Directory conflict");
        var repository = new SqliteStoryRepository(DatabasePath);
        await Assert.ThrowsAsync<IOException>(() => repository.GetAllAsync());
        File.Delete(Path.Combine(_directory, "nested"));
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task RejectsInvalidStoriesBeforeCreatingDatabase()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.AddAsync(new Story()));
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.AddAsync(new Story
        {
            Title = "Título", Sentences = [new() { SpanishText = "Texto" }], Level = (CEFR)99
        }));
        Assert.False(File.Exists(DatabasePath));
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    [Fact]
    public async Task VersionOneUpgradeResetsLibraryExactlyOnce()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE Stories (
                    Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, EnglishTitle TEXT NOT NULL DEFAULT '',
                    Level TEXT NOT NULL, Author TEXT NOT NULL DEFAULT '', StoryText TEXT NOT NULL, DateAdded TEXT NOT NULL
                );
                INSERT INTO Stories (Title, Level, StoryText, DateAdded)
                VALUES ('Antigua', 'A1', 'Historia antigua.', '2026-01-01T00:00:00Z');
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }

        var repository = new SqliteStoryRepository(DatabasePath);
        Assert.Empty(await repository.GetAllAsync());
        var saved = await repository.AddAsync(ValidStory());
        Assert.Equal(saved.Id, Assert.Single(await new SqliteStoryRepository(DatabasePath).GetAllAsync()).Id);
        using var check = Open();
        using var query = check.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Stories') WHERE name = 'StoryText';";
        Assert.Equal(0L, query.ExecuteScalar());
        query.CommandText = "PRAGMA user_version;";
        Assert.Equal(4L, query.ExecuteScalar());
    }

    [Fact]
    public async Task FailedUpgradePreservesOldStoryAndSchemaVersion()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            // A conflicting table simulates schema creation failing after the old
            // Stories table is dropped. Both the drop and version must roll back.
            command.CommandText = """
                CREATE TABLE Stories (Id INTEGER PRIMARY KEY, StoryText TEXT NOT NULL);
                INSERT INTO Stories (StoryText) VALUES ('Conservar si falla.');
                CREATE TABLE StorySentences (ConflictingColumn TEXT);
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<SqliteException>(() => new SqliteStoryRepository(DatabasePath).GetAllAsync());
        using var check = Open();
        using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version;";
        Assert.Equal(1L, query.ExecuteScalar());
        query.CommandText = "SELECT StoryText FROM Stories;";
        Assert.Equal("Conservar si falla.", query.ExecuteScalar());
    }

    [Fact]
    public async Task FailedSentenceInsertRollsBackEntireStory()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        await repository.GetAllAsync();
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER RejectSecondSentence BEFORE INSERT ON StorySentences
                WHEN NEW.Position = 1 BEGIN SELECT RAISE(ABORT, 'Simulated storage failure'); END;
                """;
            command.ExecuteNonQuery();
        }
        var story = ValidStory();
        story.Sentences.Add(new() { SpanishText = "Otra frase." });
        await Assert.ThrowsAsync<SqliteException>(() => repository.AddAsync(story));
        Assert.Empty(await repository.GetAllAsync());
        using var check = Open();
        using var query = check.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM StorySentences;";
        Assert.Equal(0L, query.ExecuteScalar());
    }

    [Fact]
    public async Task SnapshotIsIndependentOfCallerAndReturnedSentences()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var story = ValidStory();
        var original = story.Sentences[0];
        original.EnglishText = "Once upon a time.";
        original.Position = 99;
        var pending = repository.AddAsync(story);
        original.SpanishText = "Changed";
        original.EnglishText = "Changed";
        story.Sentences.Clear();
        var saved = await pending;
        Assert.Equal("Érase una vez.", Assert.Single(saved.Sentences).SpanishText);
        Assert.Equal(0, saved.Sentences[0].Position);
        saved.Sentences[0].EnglishText = "Changed after save";
        var loaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Once upon a time.", Assert.Single(loaded.Sentences).EnglishText);
        Assert.Equal(0, original.Id);
        Assert.Equal(99, original.Position);
    }

    [Fact]
    public async Task SentencesCannotBeOrphanedAndCascadeWithParent()
    {
        var saved = await new SqliteStoryRepository(DatabasePath).AddAsync(ValidStory());
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO StorySentences (StoryId, Position, SpanishText) VALUES (-1, 0, 'Huérfana.');";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        command.CommandText = "DELETE FROM Stories WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", saved.Id);
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COUNT(*) FROM StorySentences;";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task TranslationUpdatePersistsOnlyEnglishForTheMatchingSentence()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var original = ValidStory();
        original.Sentences[0].SpanishText = "  Érase una vez.\r\n\r\n ";
        original.Sentences.Add(new() { SpanishText = "Fin.", EnglishText = "The end." });
        var saved = await repository.AddAsync(original);
        var other = await repository.AddAsync(ValidStory());
        await repository.UpdateSentenceTranslationAsync(saved.Id, saved.Sentences[0].Id, "  Once upon a time.  ");
        var reloaded = await new SqliteStoryRepository(DatabasePath).GetAllAsync();
        var updated = Assert.Single(reloaded, item => item.Id == saved.Id);
        Assert.Equal("Once upon a time.", updated.Sentences[0].EnglishText);
        Assert.Equal("The end.", updated.Sentences[1].EnglishText);
        Assert.Equal(saved.Sentences.Select(s => s.SpanishText), updated.Sentences.Select(s => s.SpanishText));
        Assert.Equal(saved.Sentences.Select(s => s.Position), updated.Sentences.Select(s => s.Position));
        Assert.Equal(saved.Title, updated.Title);
        Assert.Equal(saved.DateAdded, updated.DateAdded);
        Assert.Empty(Assert.Single(reloaded, item => item.Id == other.Id).Sentences[0].EnglishText);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpdateSentenceTranslationAsync(other.Id, saved.Sentences[0].Id, "Wrong story"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpdateSentenceTranslationAsync(saved.Id, long.MaxValue, "Missing sentence"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.UpdateSentenceTranslationAsync(saved.Id, saved.Sentences[0].Id, " \r\n "));
        Assert.Equal("Once upon a time.", (await repository.GetAllAsync()).Single(item => item.Id == saved.Id).Sentences[0].EnglishText);
    }

    [Fact]
    public async Task MetadataUpdateKeepsIdentityDateAndLatestTranslation()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var saved = await repository.AddAsync(ValidStory());
        var draft = StoryEditing.Snapshot(saved);
        await repository.UpdateSentenceTranslationAsync(saved.Id, saved.Sentences[0].Id, "Latest English");
        draft.Title = "  Editada  ";
        draft.EnglishTitle = "  Edited  ";
        draft.Author = "  María  ";
        draft.Level = CEFR.C1;
        draft.DateAdded = DateTime.MinValue;
        var updated = await repository.UpdateAsync(draft);
        Assert.Equal(saved.Id, updated.Id);
        Assert.Equal(saved.DateAdded, updated.DateAdded);
        Assert.Equal(saved.Sentences[0].Id, updated.Sentences[0].Id);
        Assert.Equal("Latest English", updated.Sentences[0].EnglishText);
        var loaded = Assert.Single(await new SqliteStoryRepository(DatabasePath).GetAllAsync());
        Assert.Equal("Editada", loaded.Title);
        Assert.Equal("Edited", loaded.EnglishTitle);
        Assert.Equal("María", loaded.Author);
        Assert.Equal(CEFR.C1, loaded.Level);
        Assert.Equal(saved.DateAdded, loaded.DateAdded);
        Assert.Equal(DateTime.MinValue, draft.DateAdded);
        Assert.Empty(draft.Sentences[0].EnglishText);
    }

    [Fact]
    public async Task TextUpdateReplacesIdsPreservesLatestMatchingTranslationsAndDoesNotMutateDraft()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var input = ValidStory();
        input.Sentences =
        [
            new() { SpanishText = "Hola. ", EnglishText = "First" },
            new() { SpanishText = "Hola. ", EnglishText = "Second" },
            new() { SpanishText = "Vieja.", EnglishText = "Old" }
        ];
        var saved = await repository.AddAsync(input);
        var draft = StoryEditing.Snapshot(saved);
        draft.Title = "Nueva";
        draft.Sentences =
        [
            new() { SpanishText = "\nHola.\n\n", EnglishText = "Stale" },
            new() { SpanishText = "Nueva. ", EnglishText = "New" },
            new() { SpanishText = "Hola.", EnglishText = "Stale" }
        ];
        await repository.UpdateSentenceTranslationAsync(saved.Id, saved.Sentences[0].Id, "Latest first");
        var pending = repository.UpdateAsync(draft);
        draft.Sentences[1].SpanishText = "Mutated after dispatch";
        var updated = await pending;
        Assert.Equal(new[] { "Latest first", "New", "Second" }, updated.Sentences.Select(s => s.EnglishText));
        Assert.Equal(new[] { 0, 1, 2 }, updated.Sentences.Select(s => s.Position));
        Assert.Equal("Nueva. ", updated.Sentences[1].SpanishText);
        Assert.All(updated.Sentences, sentence => Assert.True(sentence.Id > saved.Sentences.Max(s => s.Id)));
        Assert.All(draft.Sentences, sentence => Assert.Equal(0, sentence.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpdateSentenceTranslationAsync(saved.Id, saved.Sentences[0].Id, "Late stale result"));
        var loaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(saved.DateAdded, loaded.DateAdded);
        Assert.Equal(updated.Sentences.Select(s => s.SpanishText), loaded.Sentences.Select(s => s.SpanishText));
    }

    [Fact]
    public async Task FailedTextUpdateRollsBackMetadataAndAllSentences()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var saved = await repository.AddAsync(ValidStory());
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER RejectReplacement BEFORE INSERT ON StorySentences
                WHEN NEW.Position = 1 BEGIN SELECT RAISE(ABORT, 'Simulated failure'); END;
                """;
            command.ExecuteNonQuery();
        }
        var draft = StoryEditing.Snapshot(saved);
        draft.Title = "Should roll back";
        draft.Sentences = [new() { SpanishText = "Nueva." }, new() { SpanishText = "Otra." }];
        await Assert.ThrowsAsync<SqliteException>(() => repository.UpdateAsync(draft));
        var loaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(saved.Title, loaded.Title);
        Assert.Equal(saved.DateAdded, loaded.DateAdded);
        Assert.Equal(saved.Sentences[0].Id, Assert.Single(loaded.Sentences).Id);
        Assert.Equal(saved.Sentences[0].SpanishText, loaded.Sentences[0].SpanishText);
    }

    [Fact]
    public async Task DeleteCascadesAndFutureImportsNeverReuseStoryOrSentenceIds()
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var first = await repository.AddAsync(ValidStory());
        await repository.DeleteAsync(first.Id);
        Assert.Empty(await repository.GetAllAsync());
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM StorySentences;";
            Assert.Equal(0L, command.ExecuteScalar());
        }
        var next = await new SqliteStoryRepository(DatabasePath).AddAsync(ValidStory());
        Assert.True(next.Id > first.Id);
        Assert.True(next.Sentences[0].Id > first.Sentences[0].Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAsync(first.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(first));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.DeleteAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.UpdateAsync(ValidStory()));
        var invalid = StoryEditing.Snapshot(next);
        invalid.Title = " ";
        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpdateAsync(invalid));
        Assert.Equal(next.Title, Assert.Single(await repository.GetAllAsync()).Title);
    }

    [Fact]
    public async Task VersionTwoMigrationPreservesAllDataAndForeignKeys()
    {
        await CreateVersionTwoDatabase();
        var repository = new SqliteStoryRepository(DatabasePath);
        var story = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(42, story.Id);
        Assert.Equal("Título", story.Title);
        Assert.Equal("Title", story.EnglishTitle);
        Assert.Equal("María", story.Author);
        Assert.Equal(CEFR.B2, story.Level);
        Assert.Equal(DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime(), story.DateAdded);
        Assert.Equal(81, Assert.Single(story.Sentences).Id);
        Assert.Equal("  Hola.\n\n", story.Sentences[0].SpanishText);
        Assert.Equal("Hello.", story.Sentences[0].EnglishText);
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check;";
            Assert.Null(command.ExecuteScalar());
        }
        await repository.DeleteAsync(story.Id);
        var next = await repository.AddAsync(ValidStory());
        Assert.True(next.Id > 42);
        Assert.True(next.Sentences[0].Id > 81);
        Assert.Equal(next.Id, Assert.Single(await new SqliteStoryRepository(DatabasePath).GetAllAsync()).Id);
    }

    [Fact]
    public async Task FailedVersionTwoMigrationPreservesOriginalSchemaAndData()
    {
        await CreateVersionTwoDatabase();
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE StorySentences_v3 (Conflict TEXT);";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<SqliteException>(() => new SqliteStoryRepository(DatabasePath).GetAllAsync());
        using var check = Open();
        using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, query.ExecuteScalar());
        query.CommandText = "SELECT SpanishText FROM StorySentences WHERE Id = 81;";
        Assert.Equal("  Hola.\n\n", query.ExecuteScalar());
        query.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'Stories_v3';";
        Assert.Equal(0L, query.ExecuteScalar());
    }

    private Task CreateVersionTwoDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Stories (
                Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, EnglishTitle TEXT NOT NULL DEFAULT '',
                Level TEXT NOT NULL, Author TEXT NOT NULL DEFAULT '', DateAdded TEXT NOT NULL
            );
            CREATE TABLE StorySentences (
                Id INTEGER PRIMARY KEY, StoryId INTEGER NOT NULL REFERENCES Stories(Id) ON DELETE CASCADE,
                Position INTEGER NOT NULL, SpanishText TEXT NOT NULL, EnglishText TEXT NOT NULL DEFAULT '',
                UNIQUE (StoryId, Position)
            );
            INSERT INTO Stories VALUES (42, 'Título', 'Title', 'B2', 'María', '2026-01-01T00:00:00.0000000Z');
            INSERT INTO StorySentences VALUES (81, 42, 0, $spanish, 'Hello.');
            PRAGMA user_version = 2;
            """;
        command.Parameters.AddWithValue("$spanish", "  Hola.\n\n");
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateReaderTranslationCannotOverwriteEditedOrReimportedContent(bool delete)
    {
        var repository = new SqliteStoryRepository(DatabasePath);
        var saved = await repository.AddAsync(ValidStory());
        var translation = new PendingTranslation();
        var reader = new ReadingPageViewModel(new NoNavigation(), translation, repository);
        reader.PrepareForDisplay(saved);
        var pending = reader.SelectSentenceCommand.ExecuteAsync(saved.Sentences[0]);
        reader.Deactivate();
        Story replacement;
        if (delete)
        {
            await repository.DeleteAsync(saved.Id);
            replacement = await repository.AddAsync(ValidStory());
        }
        else
        {
            var draft = StoryEditing.Snapshot(saved);
            draft.Sentences = [new() { SpanishText = "Texto cambiado.", EnglishText = "Changed text." }];
            replacement = await repository.UpdateAsync(draft);
        }
        reader.PrepareForDisplay(replacement);
        translation.Pending.SetResult("Old English");
        await pending;
        Assert.Empty(reader.SelectedEnglishText);
        var loaded = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(replacement.Sentences[0].EnglishText, loaded.Sentences[0].EnglishText);
        // Reopening uses replacement IDs, so no completed work from the old
        // sentence can override its stored English or skip a missing translation.
        reader.PrepareForDisplay(loaded);
        await reader.SelectSentenceCommand.ExecuteAsync(loaded.Sentences[0]);
        Assert.Equal(delete ? 2 : 1, translation.Calls);
        Assert.Equal(delete ? "Old English" : "Changed text.", reader.SelectedEnglishText);
    }

    private sealed class PendingTranslation : ITranslationService
    {
        public TaskCompletionSource<string> Pending { get; } = new();
        public int Calls { get; private set; }
        public Task<string> TranslateAsync(string spanish, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Pending.Task;
        }
    }

    private sealed class NoNavigation : INavigationService
    {
        public void Navigate<T>(object? parameter = null) where T : class { }
    }

    private static Story ValidStory() => new()
    {
        Title = "Historia", Sentences = [new() { SpanishText = "Érase una vez." }]
    };

    public void Dispose()
    {
        // Only this test instance's generated directory is ever removed.
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
