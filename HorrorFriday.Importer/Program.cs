using System.Diagnostics;
using HorrorFriday.Importer.Models;
using HorrorFriday.Importer.Services;

// ============================================================
// CONFIGURATION
// ============================================================

// Load .env file if present (local-only, gitignored)
var envPath = File.Exists(".env") ? ".env" : Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envPath))
    foreach (var line in File.ReadAllLines(envPath))
    {
        var parts = line.Split('=', 2);
        if (parts.Length == 2) Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
    }

var csvPath = @"C:\Users\Gian\source\repos\C-Gian\HorrorFriday\HorrorFriday.Importer\Data\TMDB_movie_dataset_v11.csv";
var dataDir = @"C:\Users\Gian\source\repos\C-Gian\HorrorFriday\HorrorFriday.Importer\Data\Import";

var connectionString = Environment.GetEnvironmentVariable("HF_DB")
    ?? throw new InvalidOperationException("Set HF_DB in .env file (see .env.example)");
var openAiKey = Environment.GetEnvironmentVariable("HF_OPENAI_KEY")
    ?? throw new InvalidOperationException("Set HF_OPENAI_KEY in .env file (see .env.example)");

const int BATCH_SIZE = 50;

// ============================================================
// PHASE A: Process CSV + Generate Embeddings → Save to local files
// ============================================================

Console.WriteLine("=== HorrorFriday Importer ===\n");
Console.WriteLine("PHASE A: Processing movies and generating embeddings...\n");

if (!File.Exists(csvPath))
{
    Console.WriteLine($"ERROR: CSV file not found at: {csvPath}");
    return;
}

// Check if Phase A was already completed (resume support)
var moviesFilePath = Path.Combine(dataDir, "movies.tsv");
bool phaseADone = File.Exists(moviesFilePath) && new FileInfo(moviesFilePath).Length > 0;

if (phaseADone)
{
    Console.WriteLine("Local files already exist - skipping Phase A.");
    Console.WriteLine("(Delete the Import folder to re-process from scratch)\n");
}
else
{
    // Test OpenAI connection
    Console.WriteLine("Testing OpenAI connection...");
    using var embeddings = new EmbeddingService(openAiKey);
    var testEmbedding = await embeddings.GenerateEmbeddingAsync("test");
    if (testEmbedding == null)
    {
        Console.WriteLine("ERROR: Cannot connect to OpenAI. Check your API key.");
        return;
    }
    Console.WriteLine($"OpenAI OK! (dimension: {testEmbedding.Length})\n");

    // Read and filter CSV
    Console.WriteLine("Reading CSV...");
    var csvReader = new CsvReaderService();
    var allMovies = new List<Movie>();
    int csvCount = 0;
    foreach (var movie in csvReader.ReadMovies(csvPath))
    {
        allMovies.Add(movie);
        csvCount++;
        if (csvCount % 10000 == 0)
            Console.WriteLine($"  CSV: {csvCount} valid movies read so far...");
    }
    var movies = allMovies;
    Console.WriteLine($"  CSV complete: {movies.Count} valid movies\n");

    // Process movies: generate embeddings in batches
    var storage = new LocalStorageService(dataDir);
    var processedMovies = new List<(int id, Movie movie)>();
    var stopwatch = Stopwatch.StartNew();

    int processed = 0;
    int embeddingErrors = 0;

    for (int i = 0; i < movies.Count; i += BATCH_SIZE)
    {
        var batch = movies.Skip(i).Take(BATCH_SIZE).ToList();

        // Build text for each movie: title + overview + genres + keywords
        var texts = batch.Select(m =>
        {
            var parts = new List<string> { m.Title };
            if (!string.IsNullOrWhiteSpace(m.Overview))
                parts.Add(m.Overview);
            if (m.Genres.Count > 0)
                parts.Add("Genres: " + string.Join(", ", m.Genres));
            if (m.Keywords.Count > 0)
                parts.Add("Keywords: " + string.Join(", ", m.Keywords.Take(10)));
            return string.Join(". ", parts);
        }).ToList();

        // Generate embeddings for the whole batch in one API call
        var batchEmbeddings = await embeddings.GenerateEmbeddingsBatchAsync(texts);

        // Store results
        for (int j = 0; j < batch.Count; j++)
        {
            var movie = batch[j];

            if (batchEmbeddings[j] == null)
            {
                embeddingErrors++;
                continue;
            }

            movie.Embedding = batchEmbeddings[j];
            int movieId = storage.AddMovie(movie);
            processedMovies.Add((movieId, movie));
            processed++;
        }

        // Progress update
        var elapsed = stopwatch.Elapsed;
        var rate = processed / Math.Max(elapsed.TotalMinutes, 0.1);
        var remaining = TimeSpan.FromMinutes((movies.Count - (i + batch.Count)) / Math.Max(rate, 1));

        Console.WriteLine(
            $"  Embeddings: {processed}/{movies.Count} " +
            $"({embeddingErrors} errors) " +
            $"- {rate:F0}/min " +
            $"- ETA: {remaining:hh\\:mm\\:ss}");

        await Task.Delay(100);
    }

    stopwatch.Stop();
    Console.WriteLine($"\nPhase A complete: {processed} movies processed in {stopwatch.Elapsed:hh\\:mm\\:ss}");
    Console.WriteLine($"Embedding errors: {embeddingErrors}\n");

    // Write everything to local files
    storage.WriteAllFiles(processedMovies);
    Console.WriteLine();
}

// ============================================================
// PHASE B: Bulk-load local files into database
// ============================================================

Console.WriteLine("PHASE B: Loading data into database...\n");

Console.WriteLine("Testing database connection...");
using var db = new DatabaseService(connectionString);
try
{
    await db.TestConnectionAsync();
    Console.WriteLine("Database connection OK!\n");
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: Cannot connect to database: {ex.Message}");
    return;
}

var importStopwatch = Stopwatch.StartNew();
await db.BulkImportAsync(dataDir);
importStopwatch.Stop();

Console.WriteLine($"\nPhase B complete in {importStopwatch.Elapsed:mm\\:ss}");
Console.WriteLine($"\n=== All Done! ===");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();