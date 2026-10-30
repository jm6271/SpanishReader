using System.Globalization;
using Microsoft.Data.Sqlite;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;

namespace SpanishReader.Core.Services;

public sealed class SqliteStoryRepository : IStoryRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly string? _bundledStoriesDirectory;
    private readonly object _gate = new();
    private bool _initialized;

    public SqliteStoryRepository(string databasePath, string? bundledStoriesDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("An absolute database path is required.", nameof(databasePath));

        _databasePath = databasePath;
        if (bundledStoriesDirectory is not null && !Path.IsPathFullyQualified(bundledStoriesDirectory))
            throw new ArgumentException("An absolute bundled stories directory is required.", nameof(bundledStoriesDirectory));
        _bundledStoriesDirectory = bundledStoriesDirectory;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false, DefaultTimeout = 5, ForeignKeys = true
        }.ToString();
    }

    public Task<Story> AddAsync(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (StoryValidation.Validate(story).Count != 0)
            throw new ArgumentException("The story is missing required information.", nameof(story));

        // Deep-copy before dispatching work; the caller may retain editable sentences.
        // Collection order is authoritative; persistence assigns contiguous positions.
        var saved = new Story
        {
            Title = story.Title.Trim(),
            EnglishTitle = story.EnglishTitle.Trim(),
            Author = story.Author.Trim(),
            Level = story.Level,
            Sentences = story.Sentences.Select((sentence, position) => new StorySentence
            {
                Position = position,
                SpanishText = sentence.SpanishText,
                EnglishText = sentence.EnglishText
            }).ToList(),
            DateAdded = DateTime.UtcNow
        };

        return ExecuteAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            InsertStory(connection, transaction, saved);
            // A failed sentence insert rolls back the parent and all earlier sentences.
            transaction.Commit();
            return saved;
        });
    }

    public Task<Story> UpdateAsync(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(story.Id);
        if (StoryValidation.Validate(story).Count != 0)
            throw new ArgumentException("The story is missing required information.", nameof(story));

        var saved = StoryEditing.Snapshot(story);
        saved.Title = saved.Title.Trim();
        saved.EnglishTitle = saved.EnglishTitle.Trim();
        saved.Author = saved.Author.Trim();
        return ExecuteAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT DateAdded FROM Stories WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", saved.Id);
            var date = command.ExecuteScalar() as string
                ?? throw new InvalidOperationException("The story no longer exists.");
            saved.DateAdded = DateTime.Parse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            command.CommandText = """
                SELECT Id, Position, SpanishText, EnglishText FROM StorySentences
                WHERE StoryId = $id ORDER BY Position;
                """;
            var current = new List<StorySentence>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    current.Add(new StorySentence
                    {
                        Id = reader.GetInt64(0), StoryId = saved.Id, Position = reader.GetInt32(1),
                        SpanishText = reader.GetString(2), EnglishText = reader.GetString(3)
                    });

            command.CommandText = """
                UPDATE Stories SET Title = $title, EnglishTitle = $englishTitle,
                    Level = $level, Author = $author WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$title", saved.Title);
            command.Parameters.AddWithValue("$englishTitle", saved.EnglishTitle);
            command.Parameters.AddWithValue("$level", saved.Level.ToString());
            command.Parameters.AddWithValue("$author", saved.Author);
            command.ExecuteNonQuery();

            if (current.Select(s => s.SpanishText).SequenceEqual(saved.Sentences.Select(s => s.SpanishText)))
            {
                // Metadata edits must not overwrite translations completed since
                // the editor opened, nor invalidate the reader's sentence IDs.
                saved.Sentences = current;
            }
            else
            {
                foreach (var (sentence, original) in StoryEditing.MatchSentences(saved.Sentences, current))
                    if (original is not null) sentence.EnglishText = original.EnglishText;

                command.Parameters.Clear();
                command.Parameters.AddWithValue("$id", saved.Id);
                command.CommandText = "DELETE FROM StorySentences WHERE StoryId = $id;";
                command.ExecuteNonQuery();
                // Replacement rows receive never-reused IDs. A native translation
                // still running for an old sentence can no longer write to this text.
                InsertSentences(connection, transaction, saved);
            }
            transaction.Commit();
            return saved;
        });
    }

    public Task DeleteAsync(long storyId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storyId);
        return ExecuteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Stories WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", storyId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The story no longer exists.");
            return true;
        });
    }

    private static void InsertStory(SqliteConnection connection, SqliteTransaction transaction, Story story)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Stories (Title, EnglishTitle, Level, Author, DateAdded)
            VALUES ($title, $englishTitle, $level, $author, $date) RETURNING Id;
            """;
        command.Parameters.AddWithValue("$title", story.Title.Trim());
        command.Parameters.AddWithValue("$englishTitle", story.EnglishTitle.Trim());
        command.Parameters.AddWithValue("$level", story.Level.ToString());
        command.Parameters.AddWithValue("$author", story.Author.Trim());
        command.Parameters.AddWithValue("$date", story.DateAdded.ToString("O", CultureInfo.InvariantCulture));
        story.Id = (long)command.ExecuteScalar()!;
        InsertSentences(connection, transaction, story);
    }

    private static void InsertSentences(SqliteConnection connection, SqliteTransaction transaction, Story story)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO StorySentences (StoryId, Position, SpanishText, EnglishText)
            VALUES ($storyId, $position, $spanish, $english) RETURNING Id;
            """;
        command.Parameters.AddWithValue("$storyId", story.Id);
        var positionParameter = command.Parameters.Add("$position", SqliteType.Integer);
        var spanishParameter = command.Parameters.Add("$spanish", SqliteType.Text);
        var englishParameter = command.Parameters.Add("$english", SqliteType.Text);
        for (var position = 0; position < story.Sentences.Count; position++)
        {
            var sentence = story.Sentences[position];
            sentence.StoryId = story.Id;
            sentence.Position = position;
            positionParameter.Value = position;
            spanishParameter.Value = sentence.SpanishText;
            englishParameter.Value = sentence.EnglishText;
            sentence.Id = (long)command.ExecuteScalar()!;
        }
    }

    public Task<IReadOnlyList<Story>> GetAllAsync() => ExecuteAsync<IReadOnlyList<Story>>(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.Title, s.EnglishTitle, s.Level, s.Author, s.DateAdded,
                   t.Id, t.Position, t.SpanishText, t.EnglishText
            FROM Stories s LEFT JOIN StorySentences t ON t.StoryId = s.Id
            ORDER BY s.DateAdded DESC, s.Id DESC, t.Position;
            """;
        using var reader = command.ExecuteReader();
        var stories = new List<Story>();
        Story? story = null;
        while (reader.Read())
        {
            // One joined read provides a consistent snapshot without an N+1 query
            // for each story. Stories stay newest-first and sentences stay in order.
            if (story is null || story.Id != reader.GetInt64(0))
            {
                var levelName = reader.GetString(3);
                if (!Enum.TryParse<CEFR>(levelName, out var level) || !Enum.IsDefined(level))
                    throw new InvalidDataException("The database contains an unsupported CEFR level.");

                story = new Story
                {
                    Id = reader.GetInt64(0),
                    Title = reader.GetString(1),
                    EnglishTitle = reader.GetString(2),
                    Level = level,
                    Author = reader.GetString(4),
                    DateAdded = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                };
                stories.Add(story);
            }
            if (!reader.IsDBNull(6))
                story.Sentences.Add(new StorySentence
                {
                    Id = reader.GetInt64(6), StoryId = story.Id, Position = reader.GetInt32(7),
                    SpanishText = reader.GetString(8), EnglishText = reader.GetString(9)
                });
        }
        return stories;
    });

    public Task UpdateSentenceTranslationAsync(long storyId, long sentenceId, string englishText)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storyId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sentenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(englishText);
        return ExecuteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            // Scope by both IDs: a sentence from another story must never be updated.
            command.CommandText = """
                UPDATE StorySentences SET EnglishText = $english
                WHERE StoryId = $storyId AND Id = $sentenceId;
                """;
            command.Parameters.AddWithValue("$english", englishText.Trim());
            command.Parameters.AddWithValue("$storyId", storyId);
            command.Parameters.AddWithValue("$sentenceId", sentenceId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The sentence no longer exists in this story.");
            return true;
        });
    }

    private Task<T> ExecuteAsync<T>(Func<SqliteConnection, T> operation) => Task.Run(() =>
    {
        // The provider's async ADO methods still block. Keep I/O off the UI thread,
        // and serialize this repository's operations, including first-use setup.
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            if (!_initialized)
            {
                Initialize(connection);
                ImportBundledStories(connection);
                _initialized = true;
            }
            return operation(connection);
        }
    });

    private static void Initialize(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var version = (long)command.ExecuteScalar()!;
        if (version > 4)
            throw new InvalidDataException("This database requires a newer version of SpanishReader.");

        if (version == 1)
        {
            // This upgrade deliberately starts an empty library, as selected for
            // the sentence-based model. The reset and new schema commit together.
            command.CommandText = "DROP TABLE Stories;";
            command.ExecuteNonQuery();
        }

        if (version < 2)
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Stories (
                    Id INTEGER PRIMARY KEY,
                    Title TEXT NOT NULL,
                    EnglishTitle TEXT NOT NULL DEFAULT '',
                    Level TEXT NOT NULL CHECK (Level IN ('A1', 'A2', 'B1', 'B2', 'C1', 'C2')),
                    Author TEXT NOT NULL DEFAULT '',
                    DateAdded TEXT NOT NULL
                );
                CREATE TABLE StorySentences (
                    Id INTEGER PRIMARY KEY,
                    StoryId INTEGER NOT NULL REFERENCES Stories(Id) ON DELETE CASCADE,
                    Position INTEGER NOT NULL CHECK (Position >= 0),
                    SpanishText TEXT NOT NULL CHECK (length(trim(SpanishText)) > 0),
                    EnglishText TEXT NOT NULL DEFAULT '',
                    UNIQUE (StoryId, Position)
                );
                PRAGMA user_version = 2;
                """;
            command.ExecuteNonQuery();
        }
        if (version < 3)
        {
            // Copy before dropping either old table. Foreign keys remain enabled,
            // and the transaction preserves all version-2 data if migration fails.
            // AUTOINCREMENT protects pending reader work even after delete/import.
            command.CommandText = """
                CREATE TABLE Stories_v3 (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    EnglishTitle TEXT NOT NULL DEFAULT '',
                    Level TEXT NOT NULL CHECK (Level IN ('A1', 'A2', 'B1', 'B2', 'C1', 'C2')),
                    Author TEXT NOT NULL DEFAULT '',
                    DateAdded TEXT NOT NULL
                );
                CREATE TABLE StorySentences_v3 (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StoryId INTEGER NOT NULL REFERENCES Stories_v3(Id) ON DELETE CASCADE,
                    Position INTEGER NOT NULL CHECK (Position >= 0),
                    SpanishText TEXT NOT NULL CHECK (length(trim(SpanishText)) > 0),
                    EnglishText TEXT NOT NULL DEFAULT '',
                    UNIQUE (StoryId, Position)
                );
                INSERT INTO Stories_v3 SELECT * FROM Stories;
                INSERT INTO StorySentences_v3 SELECT * FROM StorySentences;
                DROP TABLE StorySentences;
                DROP TABLE Stories;
                ALTER TABLE Stories_v3 RENAME TO Stories;
                ALTER TABLE StorySentences_v3 RENAME TO StorySentences;
                PRAGMA user_version = 3;
                """;
            command.ExecuteNonQuery();
        }
        if (version < 4)
        {
            // This ledger deliberately has no foreign key to Stories: deleting a
            // story must not remove the record that it was already offered.
            command.CommandText = """
                CREATE TABLE ImportedBundledStories (BundleId TEXT PRIMARY KEY NOT NULL);
                PRAGMA user_version = 4;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private void ImportBundledStories(SqliteConnection connection)
    {
        if (_bundledStoriesDirectory is null) return;
        // A configured but missing directory is a deployment error. Do not mark
        // initialization complete; a later load can retry after the files are fixed.
        var bundles = Directory.GetFiles(_bundledStoriesDirectory, "*.json")
            .Order(StringComparer.Ordinal).Select(path => BundledStoryContent.Deserialize(File.ReadAllText(path))).ToList();
        if (bundles.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != bundles.Count)
            throw new InvalidDataException("Bundled stories contain duplicate IDs.");
        foreach (var bundle in bundles) BundledStoryContent.Validate(bundle, requireTranslations: true);

        // All content and ledger entries commit together. The write transaction
        // also serializes two app instances initializing the same database.
        using var transaction = connection.BeginTransaction();
        foreach (var bundle in bundles)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO ImportedBundledStories (BundleId) VALUES ($id);";
            command.Parameters.AddWithValue("$id", bundle.Id);
            if (command.ExecuteNonQuery() == 0) continue;
            var story = bundle.ToStory();
            story.DateAdded = DateTime.UtcNow;
            InsertStory(connection, transaction, story);
        }
        transaction.Commit();
    }
}
