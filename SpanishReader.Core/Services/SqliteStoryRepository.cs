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
            DataSource = databasePath, Pooling = false, DefaultTimeout = 5
        }.ToString();
    }

    public Task<Story> AddAsync(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (StoryValidation.Validate(story).Count != 0)
            throw new ArgumentException("The story is missing required information.", nameof(story));

        // Snapshot before dispatching work; the caller may retain an editable draft.
        var saved = new Story
        {
            Title = story.Title.Trim(),
            EnglishTitle = story.EnglishTitle.Trim(),
            Author = story.Author.Trim(),
            Level = story.Level,
            StoryText = story.StoryText,
            DateAdded = DateTime.UtcNow
        };

        return ExecuteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Stories (Title, EnglishTitle, Level, Author, StoryText, DateAdded)
                VALUES ($title, $englishTitle, $level, $author, $text, $date)
                RETURNING Id;
                """;
            command.Parameters.AddWithValue("$title", saved.Title);
            command.Parameters.AddWithValue("$englishTitle", saved.EnglishTitle);
            command.Parameters.AddWithValue("$level", saved.Level.ToString());
            command.Parameters.AddWithValue("$author", saved.Author);
            command.Parameters.AddWithValue("$text", saved.StoryText);
            command.Parameters.AddWithValue("$date", saved.DateAdded.ToString("O", CultureInfo.InvariantCulture));
            saved.Id = (long)command.ExecuteScalar()!;
            return saved;
        });
    }

    public Task<IReadOnlyList<Story>> GetAllAsync() => ExecuteAsync<IReadOnlyList<Story>>(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, EnglishTitle, Level, Author, StoryText, DateAdded
            FROM Stories ORDER BY DateAdded DESC, Id DESC;
            """;
        using var reader = command.ExecuteReader();
        var stories = new List<Story>();
        while (reader.Read())
        {
            var levelName = reader.GetString(3);
            if (!Enum.TryParse<CEFR>(levelName, out var level) || !Enum.IsDefined(level))
                throw new InvalidDataException("The database contains an unsupported CEFR level.");

            stories.Add(new Story
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                EnglishTitle = reader.GetString(2),
                Level = level,
                Author = reader.GetString(4),
                StoryText = reader.GetString(5),
                DateAdded = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }
        return stories;
    });

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
        if (version > 1)
            throw new InvalidDataException("This database requires a newer version of SpanishReader.");

        if (version == 0)
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Stories (
                    Id INTEGER PRIMARY KEY,
                    Title TEXT NOT NULL,
                    EnglishTitle TEXT NOT NULL DEFAULT '',
                    Level TEXT NOT NULL CHECK (Level IN ('A1', 'A2', 'B1', 'B2', 'C1', 'C2')),
                    Author TEXT NOT NULL DEFAULT '',
                    StoryText TEXT NOT NULL,
                    DateAdded TEXT NOT NULL
                );
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
