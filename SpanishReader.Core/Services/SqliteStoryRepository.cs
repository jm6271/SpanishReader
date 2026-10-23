using System.Globalization;
using Microsoft.Data.Sqlite;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;

namespace SpanishReader.Core.Services;

public sealed class SqliteStoryRepository : IStoryRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly object _gate = new();
    private bool _initialized;

    public SqliteStoryRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("An absolute database path is required.", nameof(databasePath));

        _databasePath = databasePath;
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
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Stories (Title, EnglishTitle, Level, Author, DateAdded)
                VALUES ($title, $englishTitle, $level, $author, $date)
                RETURNING Id;
                """;
            command.Parameters.AddWithValue("$title", saved.Title);
            command.Parameters.AddWithValue("$englishTitle", saved.EnglishTitle);
            command.Parameters.AddWithValue("$level", saved.Level.ToString());
            command.Parameters.AddWithValue("$author", saved.Author);
            command.Parameters.AddWithValue("$date", saved.DateAdded.ToString("O", CultureInfo.InvariantCulture));
            saved.Id = (long)command.ExecuteScalar()!;

            command.CommandText = """
                INSERT INTO StorySentences (StoryId, Position, SpanishText, EnglishText)
                VALUES ($storyId, $position, $spanish, $english) RETURNING Id;
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$storyId", saved.Id);
            var positionParameter = command.Parameters.Add("$position", SqliteType.Integer);
            var spanishParameter = command.Parameters.Add("$spanish", SqliteType.Text);
            var englishParameter = command.Parameters.Add("$english", SqliteType.Text);
            foreach (var sentence in saved.Sentences)
            {
                sentence.StoryId = saved.Id;
                positionParameter.Value = sentence.Position;
                spanishParameter.Value = sentence.SpanishText;
                englishParameter.Value = sentence.EnglishText;
                sentence.Id = (long)command.ExecuteScalar()!;
            }
            // A failed sentence insert rolls back the parent and all earlier sentences.
            transaction.Commit();
            return saved;
        });
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
        if (version > 2)
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
        transaction.Commit();
    }
}
