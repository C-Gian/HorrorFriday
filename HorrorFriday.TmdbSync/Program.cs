using System.Text.Json;
using HorrorFriday.TmdbSync.Models;
using HorrorFriday.TmdbSync.Services;

// ── Load secrets from .env ────────────────────────────────────────────────────

var envPath = File.Exists(".env") ? ".env" : Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envPath))
    foreach (var line in File.ReadAllLines(envPath))
    {
        var parts = line.Split('=', 2);
        if (parts.Length == 2 && !parts[0].StartsWith('#'))
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
    }

var connectionString = Environment.GetEnvironmentVariable("HF_DB") ?? "";
var tmdbApiKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? "";
var openAiKey = Environment.GetEnvironmentVariable("HF_OPENAI_KEY") ?? "";

// ── Load appsettings.json ─────────────────────────────────────────────────────

var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
SyncSettings settings = new();
if (File.Exists(settingsPath))
{
    var json = await File.ReadAllTextAsync(settingsPath);
    settings = JsonSerializer.Deserialize<SyncSettings>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
}

// Resolve relative CSV paths against the project source dir (not bin/Debug)
// so appsettings.json can use "..\HorrorFriday.Importer\Data\..." relative to its own location
static string ResolvePath(string path)
{
    if (Path.IsPathRooted(path)) return path;
    // Try relative to appsettings.json's directory first (project root when running from IDE)
    var fromAppDir = Path.GetFullPath(path, AppContext.BaseDirectory);
    if (File.Exists(fromAppDir)) return fromAppDir;
    // Fall back: relative to working directory
    return Path.GetFullPath(path);
}

var csvOldPath = ResolvePath(settings.CsvPaths.OldCsv);
var csvNewPath = ResolvePath(settings.CsvPaths.NewCsv);
var csvDiffDir = Path.IsPathRooted(settings.CsvPaths.DiffOutputDir)
    ? settings.CsvPaths.DiffOutputDir
    : Path.GetFullPath(settings.CsvPaths.DiffOutputDir, AppContext.BaseDirectory);

PrintBanner();

// ── Main loop ─────────────────────────────────────────────────────────────────

// Lazy-init services: only connect when a sync mode is actually chosen
SyncDatabaseService? db = null;
TmdbApiService? tmdbService = null;
SyncOrchestrator? orchestrator = null;

while (true)
{
    PrintMenu();
    var key = Console.ReadKey(intercept: true).KeyChar;
    Console.WriteLine();

    // Modes 5-7 are handled before TMDB connection checks
    if (char.ToUpper(key) == '7')
    {
        var diffFile = Path.Combine(csvDiffDir, "diff_only_in_new.txt");
        Console.WriteLine($"""

  ┌─ Import Nuovi Record con Embedding ──────────────────────────────────────┐
  │ Legge diff_only_in_new.txt, carica i record corrispondenti dal nuovo     │
  │ CSV, genera embedding via OpenAI (text-embedding-3-small) e li inserisce │
  │ nel DB con tutti i campi (cast, director, budget, imdb…).                │
  │                                                                          │
  │ ⚠  Operazione a pagamento: ~2.957 record × embedding OpenAI.             │
  │ ⚠  Assicurati di aver eseguito migrations/003_movie_enrichment_columns   │
  └──────────────────────────────────────────────────────────────────────────┘

  File diff   : {diffFile}
  Nuovo CSV   : {csvNewPath}
""");

        if (!File.Exists(diffFile)) { PrintError($"File diff non trovato: {diffFile}\nEsegui prima il Mode [5] CSV Diff Report."); continue; }
        if (!File.Exists(csvNewPath)) { PrintError($"Nuovo CSV non trovato: {csvNewPath}"); continue; }
        if (string.IsNullOrEmpty(connectionString)) { PrintError("HF_DB non impostato."); continue; }
        if (string.IsNullOrEmpty(openAiKey)) { PrintError("HF_OPENAI_KEY non impostato. Aggiungi la chiave OpenAI nel file .env."); continue; }

        Console.Write("  Confermi? [S/N] ");
        if (char.ToUpper(Console.ReadKey(intercept: true).KeyChar) != 'S')
        {
            Console.WriteLine("\n  Annullato.\n");
            continue;
        }
        Console.WriteLine("\n");

        using var cts7 = new CancellationTokenSource();
        ConsoleCancelEventHandler h7 = (_, e) => { e.Cancel = true; cts7.Cancel(); Console.WriteLine("\n\nAnnullamento..."); };
        Console.CancelKeyPress += h7;
        try
        {
            using var importSvc = new NewRecordsImportService(connectionString, openAiKey, diffFile, csvNewPath);
            await importSvc.RunAsync(cts7.Token);
        }
        catch (OperationCanceledException) { Console.WriteLine("  Interrotto dall'utente.\n"); }
        catch (Exception ex) { PrintError(ex.Message); }
        finally { Console.CancelKeyPress -= h7; }
        continue;
    }

    if (char.ToUpper(key) == '6')
    {
        Console.WriteLine($"""

  ┌─ CSV Enrichment DB ─────────────────────────────────────────────────────┐
  │ Legge il nuovo CSV, carica i record corrispondenti in una tabella        │
  │ temporanea via COPY FROM STDIN, poi esegue un singolo UPDATE bulk.       │
  │ Aggiunge: budget, revenue, cast, director, crew, imdb_rating/votes.     │
  │                                                                          │
  │ ⚠  Assicurati di aver eseguito migrations/003_movie_enrichment_columns   │
  └──────────────────────────────────────────────────────────────────────────┘

  Nuovo CSV: {csvNewPath}
""");
        if (!File.Exists(csvNewPath)) { PrintError($"Nuovo CSV non trovato: {csvNewPath}"); continue; }

        if (string.IsNullOrEmpty(connectionString))
        {
            PrintError("HF_DB non impostato. Copia .env.example in .env e compila i valori.");
            continue;
        }

        // Ensure DB is connected (reuse existing or init fresh)
        if (db == null)
        {
            try
            {
                db = new SyncDatabaseService(connectionString);
                await db.TestConnectionAsync();
            }
            catch (Exception ex)
            {
                PrintError($"Impossibile connettersi al database: {ex.Message}");
                db?.Dispose(); db = null;
                continue;
            }
        }

        try
        {
            var enrichSvc = new CsvEnrichmentService(connectionString);
            await enrichSvc.RunAsync(csvNewPath);
        }
        catch (Exception ex) { PrintError(ex.Message); }
        continue;
    }

    // Mode 5 needs nothing (pure file I/O) — handle before any connection checks
    if (char.ToUpper(key) == '5')
    {
        Console.WriteLine("""

  ┌─ CSV Diff Report ────────────────────────────────────────────────────────┐
  │ Legge i due CSV TMDB e confronta gli ID (prima colonna).                 │
  │ Salva i risultati in due file .txt nella cartella Data/csv-diff/.         │
  │ Non tocca il database — operazione di sola lettura.                      │
  └──────────────────────────────────────────────────────────────────────────┘
""");
        Console.WriteLine($"  Vecchio CSV : {csvOldPath}");
        Console.WriteLine($"  Nuovo CSV   : {csvNewPath}");
        Console.WriteLine($"  Output      : {csvDiffDir}\n");

        if (!File.Exists(csvOldPath)) { PrintError($"Vecchio CSV non trovato: {csvOldPath}"); continue; }
        if (!File.Exists(csvNewPath)) { PrintError($"Nuovo CSV non trovato: {csvNewPath}"); continue; }

        var sw5 = System.Diagnostics.Stopwatch.StartNew();
        var csvSvc = new CsvAnalysisService();
        try { await csvSvc.RunDiffAsync(csvOldPath, csvNewPath, csvDiffDir); }
        catch (Exception ex) { PrintError(ex.Message); }
        sw5.Stop();
        Console.WriteLine($"  Completato in {sw5.Elapsed:mm\\:ss}.");
        continue;
    }

    if (char.ToUpper(key) is 'Q' or 'X')
    {
        Console.WriteLine("\nBye!\n");
        db?.Dispose();
        tmdbService?.Dispose();
        return;
    }

    if (key is not '1' and not '2' and not '3' and not '4')
    {
        Console.WriteLine("  Opzione non valida. Premi 1–7 o Q.\n");
        continue;
    }

    // ── Lazy init for sync modes 1-4 ─────────────────────────────────────────

    if (db == null)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            PrintError("HF_DB non impostato. Copia .env.example in .env e compila i valori.");
            continue;
        }
        Console.Write("Connessione al database... ");
        try
        {
            db = new SyncDatabaseService(connectionString);
            await db.TestConnectionAsync();
            Console.WriteLine("OK");
        }
        catch (Exception ex)
        {
            PrintError($"Impossibile connettersi al database: {ex.Message}");
            db?.Dispose();
            db = null;
            continue;
        }

        if (!await db.MigrationAppliedAsync())
        {
            PrintError("Migration 002 non applicata. Esegui migrations/002_tmdb_sync_tables.sql prima.");
            db.Dispose();
            db = null;
            continue;
        }
    }

    if (tmdbService == null)
    {
        if (string.IsNullOrEmpty(tmdbApiKey))
        {
            PrintError("TMDB_API_KEY non impostato. Copia .env.example in .env e compila i valori.");
            continue;
        }
        Console.Write("Verifica API key TMDB... ");
        tmdbService = new TmdbApiService(tmdbApiKey, settings.DelayBetweenRequestsMs);
        if (!await tmdbService.TestConnectionAsync())
        {
            PrintError("API key TMDB non valida. Controlla TMDB_API_KEY nel file .env.");
            tmdbService.Dispose();
            tmdbService = null;
            continue;
        }
        Console.WriteLine("OK");

        orchestrator = new SyncOrchestrator(tmdbService, db!, settings);
        Console.WriteLine($"\nImpostazioni: {settings.DelayBetweenRequestsMs}ms delay | " +
            $"{1000.0 / settings.DelayBetweenRequestsMs:F1} req/s\n");
    }

    // ── Run chosen sync mode ──────────────────────────────────────────────────

    using var cts = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\n\nAnnullamento in corso... termino l'elemento corrente.");
    };
    Console.CancelKeyPress += cancelHandler;

    SyncStats? stats = null;
    var sw = System.Diagnostics.Stopwatch.StartNew();

    switch (key)
    {
        case '1':
            Console.WriteLine("""

  ┌─ Full Sync ──────────────────────────────────────────────────────────────┐
  │ Fase 1: scopre TUTTI i film/serie horror su TMDB (anno per anno).        │
  │ Fase 2: backfill certificazioni & provider per tutto il DB.              │
  │                                                                          │
  │ ⚠  Prima run: prevedi MOLTE ORE. Ctrl+C per pausa, [3] per riprendere.  │
  └──────────────────────────────────────────────────────────────────────────┘
""");
            Console.Write("  Confermi? [S/N] ");
            if (char.ToUpper(Console.ReadKey(intercept: true).KeyChar) != 'S')
            {
                Console.WriteLine("\n  Annullato.\n");
                break;
            }
            Console.WriteLine("\n");
            stats = await orchestrator!.RunFullSyncAsync(cts.Token);
            break;

        case '2':
            Console.WriteLine($"""

  ┌─ Daily Delta ────────────────────────────────────────────────────────────┐
  │ Importa film/serie usciti negli ultimi {settings.DailyLookbackDays} giorni.                   │
  │ Ogni nuovo elemento riceve certificazioni e provider subito.             │
  │ Da eseguire ogni giorno dopo il primo Full Sync.                         │
  └──────────────────────────────────────────────────────────────────────────┘

""");
            stats = await orchestrator!.RunDailySyncAsync(cts.Token);
            break;

        case '3':
            Console.WriteLine("""

  ┌─ Backfill Metadata ──────────────────────────────────────────────────────┐
  │ Recupera certificazioni e watch provider per gli elementi del DB che     │
  │ non li hanno ancora. Auto-riprendibile: stop/restart quando vuoi.        │
  │ Aggiorna anche vote_average, popularity, poster_path.                    │
  └──────────────────────────────────────────────────────────────────────────┘

""");
            stats = await orchestrator!.RunBackfillOnlyAsync(cts.Token);
            break;

        case '4':
            Console.WriteLine($"""

  ┌─ Discover Fast ──────────────────────────────────────────────────────────┐
  │ Trova e inserisce nuovi record usando solo i dati delle pagine discover  │
  │ (titolo, anno, generi, popularity) — nessuna chiamata extra per record.  │
  │ Veloce: ~30 min anche per 150k nuovi record.                             │
  │                                                                          │
  │ Poi usa [3] Backfill per aggiungere cert., provider e dettagli.          │
  │ Con HorrorGenreIds: [] in appsettings.json scopre tutti i generi.        │
  └──────────────────────────────────────────────────────────────────────────┘

""");
            stats = await orchestrator!.RunDiscoverOnlyAsync(null, cts.Token);
            break;
    }

    Console.CancelKeyPress -= cancelHandler;
    sw.Stop();

    if (stats != null)
        PrintSummary(stats, sw.Elapsed);
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.DarkRed;
    Console.WriteLine("""
╔══════════════════════════════════════════════════╗
║       HorrorFriday  ·  TMDB Sync Tool            ║
╚══════════════════════════════════════════════════╝
""");
    Console.ResetColor();
}

static void PrintMenu()
{
    Console.WriteLine("""
┌──────────────────────────────────────────────────────────────────────────┐
│  [1]  Full Sync          — scopre tutto + backfill certs & providers     │
│       ⚠  LENTO alla prima run; Ctrl+C per pausa, [3] per riprendere     │
│                                                                          │
│  [2]  Daily Delta        — importa elementi degli ultimi N giorni        │
│                                                                          │
│  [3]  Backfill Metadata  — certif. & provider per elementi esistenti     │
│       (auto-riprendibile: stop/restart libero)                           │
│                                                                          │
│  [4]  Discover Fast      — trova nuovi elementi senza chiamate per-record │
│       (usa dati della pagina discover, lascia dettagli a Mode [3])       │
│                                                                          │
│  [5]  CSV Diff Report    — confronta ID tra i due CSV (solo file I/O)   │
│                                                                          │
│  [6]  CSV Enrichment DB  — arricchisce i record esistenti con i nuovi   │
│       campi del nuovo CSV (cast, director, crew, budget, imdb…)          │
│       ⚠  Esegui prima migrations/003_movie_enrichment_columns.sql        │
│                                                                          │
│  [7]  Import Nuovi Record — inserisce i record presenti solo nel nuovo   │
│       CSV (da diff_only_in_new.txt) con embedding OpenAI                 │
│       ⚠  Operazione a pagamento — esegui [5] prima per generare il diff  │
│                                                                          │
│  [Q]  Esci                                                               │
└──────────────────────────────────────────────────────────────────────────┘
""");
    Console.Write("  Scelta: ");
}

static void PrintSummary(SyncStats s, TimeSpan elapsed)
{
    var elapsedStr = elapsed.ToString(@"hh\:mm\:ss");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"""

  ┌─ Riepilogo ───────────────────────────────────────┐
  │  Nuovi film inseriti     : {s.NewMoviesInserted,8:N0}                 │
  │  Nuove serie inserite    : {s.NewTvInserted,8:N0}                 │
  │  Certificazioni aggiorn. : {s.CertificationsUpdated,8:N0}                 │
  │  Provider aggiornati     : {s.ProvidersUpdated,8:N0}                 │
  │  Errori / saltati        : {s.Errors,8:N0}                 │
  │  Tempo totale            : {elapsedStr}                     │
  └───────────────────────────────────────────────────┘
""");
    Console.ResetColor();
}

static void PrintError(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n  ERRORE: {message}\n");
    Console.ResetColor();
}
