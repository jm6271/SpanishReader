using Microsoft.Data.Sqlite;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;

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
        Assert.Equal(1L, command.ExecuteScalar());
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
            StoryText = "  —¡Hola, señor!\r\n\r\n    ¿Cómo estás?\nFin.  "
        };
        var before = DateTime.UtcNow;
        var saved = await new SqliteStoryRepository(DatabasePath).AddAsync(input);
        var loaded = Assert.Single(await new SqliteStoryRepository(DatabasePath).GetAllAsync());
        Assert.True(saved.Id > 0);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(input.Title.Trim(), loaded.Title);
        Assert.Equal(input.EnglishTitle.Trim(), loaded.EnglishTitle);
        Assert.Equal(input.Author.Trim(), loaded.Author);
        Assert.Equal(input.StoryText, loaded.StoryText);
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
            command.CommandText = "PRAGMA user_version = 2;";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteStoryRepository(DatabasePath).GetAllAsync());
        using var check = Open();
        using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, query.ExecuteScalar());
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
            Title = "Título", StoryText = "Texto", Level = (CEFR)99
        }));
        Assert.False(File.Exists(DatabasePath));
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static Story ValidStory() => new() { Title = "Historia", StoryText = "Érase una vez." };

    public void Dispose()
    {
        // Only this test instance's generated directory is ever removed.
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
