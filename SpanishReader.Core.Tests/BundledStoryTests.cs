using Microsoft.Data.Sqlite;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.Tests;

public sealed class BundledStoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SpanishReader.Tests", Guid.NewGuid().ToString("N"));
    private string Bundles => Path.Combine(_directory, "content");
    private string Database => Path.Combine(_directory, "stories.db");
    private SqliteStoryRepository Repository() => new(Database, Bundles);

    [Fact]
    public async Task ShippedSampleIsCompleteLosslessAndImportsFromDeploymentDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "BundledStories");
        var source = BundledStoryContent.ParseMarkdown(await File.ReadAllTextAsync(
            Path.Combine(directory, "a2-la-casa-al-final-del-camino.md")));
        var prepared = BundledStoryContent.Deserialize(await File.ReadAllTextAsync(
            Path.Combine(directory, "a2-la-casa-al-final-del-camino.json")));
        BundledStoryContent.Validate(prepared, requireTranslations: true);
        Assert.Equal(source.Id, prepared.Id);
        Assert.Equal(source.Sentences.Select(s => s.SpanishText), prepared.Sentences.Select(s => s.SpanishText));
        var repository = new SqliteStoryRepository(Database, directory);
        var imported = await repository.GetAllAsync();
        var sample = Assert.Single(imported, s => s.Title == source.Title);
        Assert.Equal(prepared.Sentences.Select(s => s.EnglishText), sample.Sentences.Select(s => s.EnglishText));
    }

    [Fact]
    public async Task MarkdownConversionPreservesParagraphsAndReviewedTranslations()
    {
        const string body = "  ¡Hola!\r\n\r\nEl gato duerme.\r\n";
        var source = "---\r\nid: a1-gato\r\ntitle: 'El gato: Luna'\r\nlevel: A1\r\nauthor: María\r\n---\r\n\r\n" + body;
        var story = BundledStoryContent.ParseMarkdown(source);
        Assert.Equal(body, string.Concat(story.Sentences.Select(s => s.SpanishText)));
        var translator = new Translator();
        await BundledStoryContent.TranslateAsync(story, translator);
        Assert.Equal(3, translator.Calls.Count); // Title and both sentences.
        story.Sentences[0].EnglishText = "Reviewed greeting.";
        var previous = BundledStoryContent.Deserialize(BundledStoryContent.Serialize(story));
        translator.Calls.Clear();
        var rerun = BundledStoryContent.ParseMarkdown(source);
        await BundledStoryContent.TranslateAsync(rerun, translator, previous);
        Assert.Empty(translator.Calls);
        Assert.Equal("Reviewed greeting.", rerun.Sentences[0].EnglishText);
        Assert.Equal(body, string.Concat(rerun.Sentences.Select(s => s.SpanishText)));

        rerun = BundledStoryContent.ParseMarkdown(source.Replace("duerme", "come"));
        await BundledStoryContent.TranslateAsync(rerun, translator, previous);
        Assert.Equal("El gato come.", Assert.Single(translator.Calls));
        translator.Calls.Clear();
        await BundledStoryContent.TranslateAsync(rerun, translator, previous, regenerate: true);
        Assert.Equal(2, translator.Calls.Count); // A supplied English title is retained.
    }

    [Theory]
    [InlineData("---\nid: gato\ntitle: Gato\nlevel: A8\n---\nHola.")]
    [InlineData("---\nid: gato\ntitle: Gato\nlevel: A1\nlevel: A2\n---\nHola.")]
    [InlineData("---\nid: gato\ntitle: Gato\nlevel: A1\n---\n  ")]
    [InlineData("---\ntitle: Gato\nlevel: A1\n---\nHola.")]
    [InlineData("---\nid: gato\ntitle: Gato\nlevel: A1\nHola.")]
    public void InvalidMarkdownIsRejected(string markdown) =>
        Assert.Throws<InvalidDataException>(() => BundledStoryContent.ParseMarkdown(markdown));

    [Fact]
    public async Task RepeatedSentencesReuseEnglishInOccurrenceOrder()
    {
        var previous = Content("repeated");
        previous.Sentences = [new() { SpanishText = "Hola. ", EnglishText = "First" },
            new() { SpanishText = "Hola.", EnglishText = "Second" }];
        var current = Content("repeated");
        current.Sentences = [new() { SpanishText = "Hola.\n\n" }, new() { SpanishText = "Hola." }];
        var translator = new Translator();
        await BundledStoryContent.TranslateAsync(current, translator, previous);
        Assert.Empty(translator.Calls);
        Assert.Equal(new[] { "First", "Second" }, current.Sentences.Select(s => s.EnglishText));
    }

    [Fact]
    public async Task ImportsOncePreservesEditsAndDeletionAndAddsNewBundleOnUpgrade()
    {
        Write(Content("first"));
        var repository = Repository();
        var first = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Hola.\n\n", first.Sentences[0].SpanishText);
        first.Title = "User edit";
        await repository.UpdateAsync(first);
        Assert.Equal("User edit", Assert.Single(await Repository().GetAllAsync()).Title);
        await repository.DeleteAsync(first.Id);
        Assert.Empty(await Repository().GetAllAsync());
        Write(Content("new-release"));
        var added = Assert.Single(await Repository().GetAllAsync());
        Assert.Equal("new-release", added.Title);
        Assert.Equal(added.Id, Assert.Single(await Repository().GetAllAsync()).Id);
    }

    [Fact]
    public async Task ExistingVersionThreeLibraryIsRetainedWhenSeeding()
    {
        Directory.CreateDirectory(_directory);
        var existing = await new SqliteStoryRepository(Database).AddAsync(Content("user").ToStory());
        // Version 4 only adds the independent import ledger. Simulate a v3 install.
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE ImportedBundledStories; PRAGMA user_version = 3;";
            command.ExecuteNonQuery();
        }
        Write(Content("bundle"));
        var loaded = await Repository().GetAllAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, s => s.Id == existing.Id && s.Title == "user");
    }

    [Fact]
    public async Task FailedSeedRollsBackStoriesAndLedgerAndCanRetry()
    {
        Directory.CreateDirectory(_directory);
        await new SqliteStoryRepository(Database).GetAllAsync();
        Write(Content("first"));
        Write(Content("second"));
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER FailSeed BEFORE INSERT ON Stories WHEN NEW.Title = 'second'
                BEGIN SELECT RAISE(ABORT, 'seed failure'); END;
                """;
            command.ExecuteNonQuery();
        }
        var repository = Repository();
        await Assert.ThrowsAsync<SqliteException>(() => repository.GetAllAsync());
        Assert.Empty(await new SqliteStoryRepository(Database).GetAllAsync());
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM ImportedBundledStories;";
            Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = "DROP TRIGGER FailSeed;";
            command.ExecuteNonQuery();
        }
        Assert.Equal(2, (await repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task InvalidOrDuplicateBundlesDoNotPartiallyImport()
    {
        Write(Content("first"));
        var invalid = Path.Combine(Bundles, "invalid.json");
        File.WriteAllText(invalid, "{\"id\":\"broken\"}");
        var repository = Repository();
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => repository.GetAllAsync());
        Assert.Empty(await new SqliteStoryRepository(Database).GetAllAsync());
        File.WriteAllText(invalid, BundledStoryContent.Serialize(Content("first")));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetAllAsync());
        File.Delete(invalid);
        var missing = Content("missing");
        missing.Sentences[0].EnglishText = "";
        Write(missing);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetAllAsync());
        Assert.Empty(await new SqliteStoryRepository(Database).GetAllAsync());
    }

    [Fact]
    public void JsonContractKeepsStrictValidationWithoutReflection()
    {
        // The test host disables JSON reflection, just as trimmed publishing does.
        Assert.False(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault);
        var json = BundledStoryContent.Serialize(Content("schema"));
        Assert.Contains("\"level\": \"A1\"", json);
        Assert.Contains("Hola.", json);
        Assert.Equal("schema", BundledStoryContent.Deserialize(json).Id);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            BundledStoryContent.Deserialize(json.Replace("\"level\": \"A1\"", "\"level\": 0")));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            BundledStoryContent.Deserialize(json.Replace("\"id\": \"schema\",", "")));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            BundledStoryContent.Deserialize(json.Replace("\"id\":", "\"unexpected\": true, \"id\":")));
    }

    [Fact]
    public void DoubleQuotedMarkdownMetadataUsesGeneratedStringMetadata()
    {
        var story = BundledStoryContent.ParseMarkdown("""
            ---
            id: quoted
            title: "Un gato: \"Luna\""
            level: A1
            author: "Mar\u00eda"
            ---
            Hola.
            """);
        Assert.Equal("Un gato: \"Luna\"", story.Title);
        Assert.Equal("María", story.Author);
    }

    private static BundledStory Content(string id) => new()
    {
        Id = id, Title = id, EnglishTitle = "English title", Level = CEFR.A1,
        Sentences = [new() { SpanishText = "Hola.\n\n", EnglishText = "Hello." }]
    };

    private void Write(BundledStory content)
    {
        Directory.CreateDirectory(Bundles);
        File.WriteAllText(Path.Combine(Bundles, content.Id + ".json"), BundledStoryContent.Serialize(content));
    }

    private sealed class Translator : ITranslationService
    {
        public List<string> Calls { get; } = [];
        public Task<string> TranslateAsync(string spanish, CancellationToken cancellationToken = default)
        {
            Calls.Add(spanish);
            return Task.FromResult("English: " + spanish);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
