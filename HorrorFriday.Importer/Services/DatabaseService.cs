using Npgsql;
using System.Diagnostics;

namespace HorrorFriday.Importer.Services;

/// <summary>
/// Handles bulk-loading data into the database using PostgreSQL COPY.
/// COPY is PostgreSQL's fastest import method — it streams data directly
/// into tables without the overhead of parsing individual SQL statements.
/// One connection, one operation per table, done.
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public DatabaseService(string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
    }

    public async Task TestConnectionAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Bulk-imports all TSV files into the database.
    /// Order matters: genres and keywords first (they're referenced by movies),
    /// then movies, then the link tables.
    /// </summary>
    public async Task BulkImportAsync(string dataDir)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        Console.WriteLine("  Preparing database for bulk import...");
        await ExecuteAsync(conn, "SET session_replication_role = 'replica';");

        // Import genres and keywords directly (no duplicates issue)
        await CopyFileAsync(conn, Path.Combine(dataDir, "genres.tsv"),
            "COPY genres (id, name) FROM STDIN");

        await CopyFileAsync(conn, Path.Combine(dataDir, "keywords.tsv"),
            "COPY keywords (id, name) FROM STDIN");

        // For movies: use a staging table to handle duplicates
        Console.WriteLine("  Creating staging table...");
        await ExecuteAsync(conn, "DROP TABLE IF EXISTS movies_staging;");
        await ExecuteAsync(conn, @"
            CREATE TABLE movies_staging (
                id INTEGER, tmdb_id INTEGER, title VARCHAR(500), original_title VARCHAR(500),
                overview TEXT, release_year SMALLINT, release_date DATE, runtime_minutes SMALLINT,
                vote_average DECIMAL(5,3), vote_count INTEGER, popularity DECIMAL(10,3),
                status VARCHAR(50), original_language VARCHAR(10), is_adult BOOLEAN,
                tagline VARCHAR(500), poster_path VARCHAR(200), imdb_id VARCHAR(20),
                embedding vector(1536)
            );");

        await CopyFileAsync(conn, Path.Combine(dataDir, "movies.tsv"),
            "COPY movies_staging (id, tmdb_id, title, original_title, overview, release_year, release_date, " +
            "runtime_minutes, vote_average, vote_count, popularity, status, original_language, " +
            "is_adult, tagline, poster_path, imdb_id, embedding) FROM STDIN");

        Console.WriteLine("  Deduplicating and moving to final table...");
        await ExecuteAsync(conn, @"
            INSERT INTO movies (id, tmdb_id, title, original_title, overview, release_year, release_date,
                runtime_minutes, vote_average, vote_count, popularity, status, original_language,
                is_adult, tagline, poster_path, imdb_id, embedding)
            SELECT DISTINCT ON (tmdb_id) id, tmdb_id, title, original_title, overview, release_year, release_date,
                runtime_minutes, vote_average, vote_count, popularity, status, original_language,
                is_adult, tagline, poster_path, imdb_id, embedding
            FROM movies_staging
            ORDER BY tmdb_id, id
            ON CONFLICT (tmdb_id) DO NOTHING;
        ");

        await ExecuteAsync(conn, "DROP TABLE movies_staging;");

        // Import link tables
        await CopyFileAsync(conn, Path.Combine(dataDir, "movie_genres.tsv"),
            "COPY movie_genres (movie_id, genre_id) FROM STDIN");

        await CopyFileAsync(conn, Path.Combine(dataDir, "movie_keywords.tsv"),
            "COPY movie_keywords (movie_id, keyword_id) FROM STDIN");

        // Re-enable foreign key checks
        await ExecuteAsync(conn, "SET session_replication_role = 'origin';");

        // Update sequences
        Console.WriteLine("  Updating sequences...");
        await ExecuteAsync(conn, "SELECT setval('movies_id_seq', (SELECT COALESCE(MAX(id), 0) FROM movies));");
        await ExecuteAsync(conn, "SELECT setval('genres_id_seq', (SELECT COALESCE(MAX(id), 0) FROM genres));");
        await ExecuteAsync(conn, "SELECT setval('keywords_id_seq', (SELECT COALESCE(MAX(id), 0) FROM keywords));");

        Console.WriteLine("  Bulk import complete!");
    }

    /// <summary>
    /// Streams a TSV file directly into a PostgreSQL table using COPY.
    /// This is the fastest possible way to load data into PostgreSQL.
    /// </summary>
    private async Task CopyFileAsync(NpgsqlConnection conn, string filePath, string copySql)
    {
        var fileName = Path.GetFileName(filePath);

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"  WARNING: File not found: {filePath}");
            return;
        }

        var lineCount = File.ReadLines(filePath).Count();
        Console.WriteLine($"  Importing {fileName} ({lineCount} rows)...");

        using var reader = new StreamReader(filePath);
        await using var writer = await conn.BeginTextImportAsync(copySql);

        string? line;
        int count = 0;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            await writer.WriteLineAsync(line);
            count++;

            if (count % 50000 == 0)
                Console.WriteLine($"    ...{count}/{lineCount} rows");
        }

        Console.WriteLine($"    Done: {count} rows imported");
    }

    private async Task ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        _dataSource.Dispose();
    }
}