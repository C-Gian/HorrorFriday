using System.Diagnostics;
using System.Text.Json;
using HorrorFriday.CsvImporter.Models;
using HorrorFriday.CsvImporter.Services;

// ── Load .env ─────────────────────────────────────────────────────────────────

var envPath = File.Exists(".env") ? ".env" : Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envPath))
    foreach (var line in File.ReadAllLines(envPath))
    {
        var parts = line.Split('=', 2);
        if (parts.Length == 2 && !parts[0].StartsWith('#'))
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
    }

var connectionString = Environment.GetEnvironmentVariable("HF_DB") ?? "";
var openAiKey = Environment.GetEnvironmentVariable("HF_OPENAI_KEY") ?? "";

// ── Load appsettings.json ─────────────────────────────────────────────────────

var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
CsvImporterSettings settings = new();
if (File.Exists(settingsPath))
{
    var json = await File.ReadAllTextAsync(settingsPath);
    settings = JsonSerializer.Deserialize<CsvImporterSettings>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
}

static string ResolvePath(string path)
{
    if (Path.IsPathRooted(path)) return path;
    var fromAppDir = Path.GetFullPath(path, AppContext.BaseDirectory);
    if (File.Exists(fromAppDir)) return fromAppDir;
    return Path.GetFullPath(path);
}

var csvOldPath = ResolvePath(settings.OldCsvPath);
var csvNewPath = ResolvePath(settings.NewCsvPath);
var dataImportDir = Path.IsPathRooted(settings.DataImportDir)
    ? settings.DataImportDir
    : Path.GetFullPath(settings.DataImportDir, AppContext.BaseDirectory);
var csvDiffDir = Path.IsPathRooted(settings.DiffOutputDir)
    ? settings.DiffOutputDir
    : Path.GetFullPath(settings.DiffOutputDir, AppContext.BaseDirectory);

PrintBanner();

while (true)
{
    PrintMenu();
    var key = Console.ReadKey(intercept: true).KeyChar;
    Console.WriteLine();

    switch (char.ToUpper(key))
    {
        case '1':
            Console.WriteLine($"""

  ┌─ Bulk CSV Import ────────────────────────────────────────────────────────┐
  │ Fase A: legge il CSV, genera embedding OpenAI (text-embedding-3-small).  │
  │ Fase B: carica i dati nel DB via PostgreSQL COPY (bulk, velocissimo).    │
  │                                                                          │
  │ ⚠  Operazione a pagamento: ~293k record × embedding OpenAI.              │
  │    La Fase A è riprendibile: se i file in Data/Import/ esistono, salta.  │
  └──────────────────────────────────────────────────────────────────────────┘

  CSV: {csvOldPath}
""");
            if (!File.Exists(csvOldPath)) { PrintError($"CSV non trovato: {csvOldPath}"); break; }
            if (string.IsNullOrEmpty(connectionString)) { PrintError("HF_DB non impostato."); break; }
            if (string.IsNullOrEmpty(openAiKey)) { PrintError("HF_OPENAI_KEY non impostato."); break; }
            Console.Write("  Confermi? [S/N] ");
            if (char.ToUpper(Console.ReadKey(intercept: true).KeyChar) != 'S') { Console.WriteLine("\n  Annullato.\n"); break; }
            Console.WriteLine("\n");
            await RunBulkImportAsync(csvOldPath, dataImportDir, connectionString, openAiKey, settings.EmbeddingBatchSize);
            break;

        case '2':
            Console.WriteLine("""

  ┌─ CSV Diff Report ────────────────────────────────────────────────────────┐
  │ Confronta gli ID tra il vecchio e il nuovo CSV TMDB.                     │
  │ Salva due file .txt in Data/csv-diff/. Solo lettura file, no DB.         │
  └──────────────────────────────────────────────────────────────────────────┘
""");
            Console.WriteLine($"  Vecchio CSV : {csvOldPath}");
            Console.WriteLine($"  Nuovo CSV   : {csvNewPath}");
            Console.WriteLine($"  Output      : {csvDiffDir}\n");
            if (!File.Exists(csvOldPath)) { PrintError($"Vecchio CSV non trovato: {csvOldPath}"); break; }
            if (!File.Exists(csvNewPath)) { PrintError($"Nuovo CSV non trovato: {csvNewPath}"); break; }
            var sw2 = Stopwatch.StartNew();
            try { await new CsvAnalysisService().RunDiffAsync(csvOldPath, csvNewPath, csvDiffDir); }
            catch (Exception ex) { PrintError(ex.Message); }
            sw2.Stop();
            var elapsed2 = sw2.Elapsed.ToString(@"mm\:ss");
            Console.WriteLine($"  Completato in {elapsed2}.");
            break;

        case '3':
            Console.WriteLine($"""

  ┌─ CSV Enrichment DB ─────────────────────────────────────────────────────┐
  │ Legge il nuovo CSV, aggiorna i record esistenti con i campi mancanti:    │
  │ budget, revenue, cast, director, crew, imdb_rating/votes.               │
  │ ⚠  Esegui prima migrations/003_movie_enrichment_columns.sql              │
  └──────────────────────────────────────────────────────────────────────────┘

  Nuovo CSV: {csvNewPath}
""");
            if (!File.Exists(csvNewPath)) { PrintError($"Nuovo CSV non trovato: {csvNewPath}"); break; }
            if (string.IsNullOrEmpty(connectionString)) { PrintError("HF_DB non impostato."); break; }
            try { await new CsvEnrichmentService(connectionString).RunAsync(csvNewPath); }
            catch (Exception ex) { PrintError(ex.Message); }
            break;

        case '4':
            var diffFile = Path.Combine(csvDiffDir, "diff_only_in_new.txt");
            Console.WriteLine($"""

  ┌─ Import Nuovi Record con Embedding ──────────────────────────────────────┐
  │ Legge diff_only_in_new.txt, genera embedding OpenAI per ogni record      │
  │ presente solo nel nuovo CSV e li inserisce nel DB con tutti i campi.     │
  │ ⚠  Operazione a pagamento. Esegui [2] prima per generare il diff.        │
  └──────────────────────────────────────────────────────────────────────────┘

  File diff : {diffFile}
  Nuovo CSV : {csvNewPath}
""");
            if (!File.Exists(diffFile)) { PrintError($"File diff non trovato: {diffFile}\nEsegui prima [2] CSV Diff Report."); break; }
            if (!File.Exists(csvNewPath)) { PrintError($"Nuovo CSV non trovato: {csvNewPath}"); break; }
            if (string.IsNullOrEmpty(connectionString)) { PrintError("HF_DB non impostato."); break; }
            if (string.IsNullOrEmpty(openAiKey)) { PrintError("HF_OPENAI_KEY non impostato."); break; }
            Console.Write("  Confermi? [S/N] ");
            if (char.ToUpper(Console.ReadKey(intercept: true).KeyChar) != 'S') { Console.WriteLine("\n  Annullato.\n"); break; }
            Console.WriteLine("\n");
            using (var cts4 = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler h4 = (_, e) => { e.Cancel = true; cts4.Cancel(); Console.WriteLine("\n\nAnnullamento..."); };
                Console.CancelKeyPress += h4;
                try
                {
                    using var svc = new NewRecordsImportService(connectionString, openAiKey, diffFile, csvNewPath);
                    await svc.RunAsync(cts4.Token);
                }
                catch (OperationCanceledException) { Console.WriteLine("  Interrotto.\n"); }
                catch (Exception ex) { PrintError(ex.Message); }
                finally { Console.CancelKeyPress -= h4; }
            }
            break;

        case 'Q': case 'X':
            Console.WriteLine("\nBye!\n");
            return;

        default:
            Console.WriteLine("  Opzione non valida. Premi 1–4 o Q.\n");
            break;
    }
}

// ── Bulk Import (Phase A + B) ─────────────────────────────────────────────────

static async Task RunBulkImportAsync(string csvPath, string dataDir, string connStr, string aiKey, int batchSize)
{
    var moviesFile = Path.Combine(dataDir, "movies.tsv");
    bool phaseADone = File.Exists(moviesFile) && new FileInfo(moviesFile).Length > 0;

    if (!phaseADone)
    {
        Console.WriteLine("FASE A: Lettura CSV e generazione embedding...\n");
        Console.WriteLine("Testing connessione OpenAI...");
        using var embeddings = new EmbeddingService(aiKey);
        var test = await embeddings.GenerateEmbeddingAsync("test");
        if (test == null) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("Impossibile connettersi a OpenAI."); Console.ResetColor(); return; }
        Console.WriteLine($"OpenAI OK! (dim: {test.Length})\n");

        Console.WriteLine("Lettura CSV...");
        var reader = new CsvReaderService();
        var movies = new List<Movie>();
        foreach (var m in reader.ReadMovies(csvPath)) { movies.Add(m); if (movies.Count % 10000 == 0) Console.WriteLine($"  {movies.Count} film letti..."); }
        Console.WriteLine($"  Totale: {movies.Count} film validi\n");

        var storage = new LocalStorageService(dataDir);
        var processed = new List<(int id, Movie movie)>();
        int done = 0, errors = 0;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < movies.Count; i += batchSize)
        {
            var batch = movies.Skip(i).Take(batchSize).ToList();
            var texts = batch.Select(m =>
            {
                var parts = new List<string> { m.Title };
                if (!string.IsNullOrWhiteSpace(m.Overview)) parts.Add(m.Overview);
                if (m.Genres.Count > 0) parts.Add("Genres: " + string.Join(", ", m.Genres));
                if (m.Keywords.Count > 0) parts.Add("Keywords: " + string.Join(", ", m.Keywords.Take(10)));
                return string.Join(". ", parts);
            }).ToList();

            var embs = await embeddings.GenerateEmbeddingsBatchAsync(texts);
            for (int j = 0; j < batch.Count; j++)
            {
                if (embs[j] == null) { errors++; continue; }
                batch[j].Embedding = embs[j];
                processed.Add((storage.AddMovie(batch[j]), batch[j]));
                done++;
            }

            var rate = done / Math.Max(sw.Elapsed.TotalMinutes, 0.1);
            var eta = TimeSpan.FromMinutes((movies.Count - (i + batch.Count)) / Math.Max(rate, 1));
            Console.WriteLine($"  Embedding: {done}/{movies.Count} ({errors} err) — {rate:F0}/min — ETA: {eta.ToString(@"hh\:mm\:ss")}");
            await Task.Delay(100);
        }

        sw.Stop();
        Console.WriteLine($"\nFase A completata: {done} film in {sw.Elapsed.ToString(@"hh\:mm\:ss")} ({errors} errori)\n");
        storage.WriteAllFiles(processed);
        Console.WriteLine();
    }
    else
    {
        Console.WriteLine("Fase A già completata (file locali esistenti). Salto direttamente alla Fase B.\n");
    }

    Console.WriteLine("FASE B: Import nel database...\n");
    Console.WriteLine("Connessione al database...");
    using var db = new DatabaseService(connStr);
    try { await db.TestConnectionAsync(); Console.WriteLine("OK\n"); }
    catch (Exception ex) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"Errore DB: {ex.Message}"); Console.ResetColor(); return; }

    var swB = Stopwatch.StartNew();
    await db.BulkImportAsync(dataDir);
    swB.Stop();
    var elapsedB = swB.Elapsed.ToString(@"mm\:ss");
    Console.WriteLine($"\nFase B completata in {elapsedB}\n");
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("""
╔══════════════════════════════════════════════════╗
║       HorrorFriday  ·  CSV Importer              ║
╚══════════════════════════════════════════════════╝
""");
    Console.ResetColor();
}

static void PrintMenu()
{
    Console.WriteLine("""
┌──────────────────────────────────────────────────────────────────────────┐
│  [1]  Bulk CSV Import    — legge CSV, genera embedding, importa nel DB   │
│       ⚠  Operazione a pagamento (OpenAI). Fase A riprendibile.           │
│                                                                          │
│  [2]  CSV Diff Report    — confronta ID tra vecchio e nuovo CSV          │
│       (solo lettura file, non tocca il DB)                               │
│                                                                          │
│  [3]  CSV Enrichment DB  — arricchisce record esistenti con nuovi campi  │
│       ⚠  Esegui prima migrations/003_movie_enrichment_columns.sql        │
│                                                                          │
│  [4]  Import Nuovi Record — inserisce record solo nel nuovo CSV          │
│       con embedding OpenAI ⚠  Operazione a pagamento                    │
│       (esegui [2] prima per generare diff_only_in_new.txt)               │
│                                                                          │
│  [Q]  Esci                                                               │
└──────────────────────────────────────────────────────────────────────────┘
""");
    Console.Write("  Scelta: ");
}

static void PrintError(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n  ERRORE: {message}\n");
    Console.ResetColor();
}

// ── Settings class ────────────────────────────────────────────────────────────

public class CsvImporterSettings
{
    public string OldCsvPath { get; set; } = "";
    public string NewCsvPath { get; set; } = "";
    public string DataImportDir { get; set; } = "Data\\Import";
    public string DiffOutputDir { get; set; } = "Data\\csv-diff";
    public int EmbeddingBatchSize { get; set; } = 50;
}
