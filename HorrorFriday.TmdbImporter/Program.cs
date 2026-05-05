using System.Text.Json;
using HorrorFriday.TmdbImporter.Models;
using HorrorFriday.TmdbImporter.Services;

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
var tmdbApiKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? "";

// ── Load appsettings.json ─────────────────────────────────────────────────────

var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
SyncSettings settings = new();
if (File.Exists(settingsPath))
{
    var json = await File.ReadAllTextAsync(settingsPath);
    settings = JsonSerializer.Deserialize<SyncSettings>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
}

PrintBanner();

SyncDatabaseService? db = null;
TmdbApiService? tmdbService = null;
SyncOrchestrator? orchestrator = null;
TmdbDumpImportService? dumpImporter = null;
TmdbTvDumpImportService? tvDumpImporter = null;

while (true)
{
    PrintMenu();
    var key = Console.ReadKey(intercept: true).KeyChar;
    Console.WriteLine();

    if (char.ToUpper(key) is 'Q' or 'X')
    {
        Console.WriteLine("\nBye!\n");
        db?.Dispose();
        tmdbService?.Dispose();
        return;
    }

    if (key is not '1' and not '2' and not '3' and not '4' and not '5' and not '6')
    {
        Console.WriteLine("  Opzione non valida. Premi 1–6 o Q.\n");
        continue;
    }

    if (db == null)
    {
        if (string.IsNullOrEmpty(connectionString)) { PrintError("HF_DB non impostato."); continue; }
        Console.Write("Connessione al database... ");
        try
        {
            db = new SyncDatabaseService(connectionString);
            await db.TestConnectionAsync();
            Console.WriteLine("OK");
        }
        catch (Exception ex) { PrintError($"Impossibile connettersi al database: {ex.Message}"); db?.Dispose(); db = null; continue; }

        if (!await db.MigrationAppliedAsync())
        {
            PrintError("Migration 002 non applicata. Esegui migrations/002_tmdb_sync_tables.sql prima.");
            db.Dispose(); db = null; continue;
        }
    }

    if (tmdbService == null)
    {
        if (string.IsNullOrEmpty(tmdbApiKey)) { PrintError("TMDB_API_KEY non impostato."); continue; }
        Console.Write("Verifica API key TMDB... ");
        tmdbService = new TmdbApiService(tmdbApiKey, settings.DelayBetweenRequestsMs);
        if (!await tmdbService.TestConnectionAsync())
        {
            PrintError("API key TMDB non valida. Controlla TMDB_API_KEY nel file .env.");
            tmdbService.Dispose(); tmdbService = null; continue;
        }
        Console.WriteLine("OK");
        orchestrator = new SyncOrchestrator(tmdbService, db!, settings);
        dumpImporter = new TmdbDumpImportService(tmdbService, db!, settings);
        tvDumpImporter = new TmdbTvDumpImportService(tmdbService, db!, settings);
        Console.WriteLine($"\nDelay: {settings.DelayBetweenRequestsMs}ms | {1000.0 / settings.DelayBetweenRequestsMs:F1} req/s\n");
    }

    using var cts = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, e) =>
    {
        e.Cancel = true; cts.Cancel();
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
  │ Fase 1: scopre TUTTI i film/serie su TMDB (anno per anno).               │
  │ Fase 2: backfill certificazioni & provider per tutto il DB.              │
  │ ⚠  Prima run: prevedi MOLTE ORE. Ctrl+C per pausa, [3] per riprendere.  │
  └──────────────────────────────────────────────────────────────────────────┘
""");
            Console.Write("  Confermi? [S/N] ");
            if (char.ToUpper(Console.ReadKey(intercept: true).KeyChar) != 'S') { Console.WriteLine("\n  Annullato.\n"); break; }
            Console.WriteLine("\n");
            stats = await orchestrator!.RunFullSyncAsync(cts.Token);
            break;

        case '2':
            Console.WriteLine($"""

  ┌─ Daily Delta ────────────────────────────────────────────────────────────┐
  │ Importa film/serie usciti negli ultimi {settings.DailyLookbackDays} giorni.                   │
  │ Ogni nuovo elemento riceve certificazioni e provider subito.             │
  └──────────────────────────────────────────────────────────────────────────┘

""");
            stats = await orchestrator!.RunDailySyncAsync(cts.Token);
            break;

        case '3':
            Console.WriteLine("""

  ┌─ Backfill Metadata ──────────────────────────────────────────────────────┐
  │ Recupera certificazioni e watch provider per gli elementi senza.         │
  │ Auto-riprendibile: stop/restart libero. Ctrl+C per pausare.              │
  └──────────────────────────────────────────────────────────────────────────┘

""");
            stats = await orchestrator!.RunBackfillOnlyAsync(cts.Token);
            break;

        case '4':
            Console.WriteLine("""

  ┌─ Discover Fast ──────────────────────────────────────────────────────────┐
  │ Trova e inserisce nuovi record usando solo i dati delle pagine discover  │
  │ (nessuna chiamata per-record). Veloce anche per 150k+ nuovi record.      │
  │ Poi usa [3] Backfill per certificazioni, provider e dettagli.            │
  └──────────────────────────────────────────────────────────────────────────┘

""");
            stats = await orchestrator!.RunDiscoverOnlyAsync(null, cts.Token);
            break;

        case '5':
        {
            // Find the dump file in Data/ subfolder (.json.gz or extracted .json)
            // Check current working directory first (project root when running via IDE/dotnet run),
            // then fall back to the binary output directory.
            var dataDir = Directory.Exists("Data") ? "Data" : Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            var dumps = Directory.GetFiles(dataDir, "*movie_ids*.json.gz")
                .Concat(Directory.GetFiles(dataDir, "*movie_ids*.json"))
                .ToArray();

            if (dumps.Length == 0)
            {
                PrintError($"""
Nessun file dump trovato in:
  {dataDir}

Scarica il dump da:
  http://files.tmdb.org/p/exports/movie_ids_MM_DD_YYYY.json.gz
e copialo in quella cartella (formato .json.gz o .json estratto).
""");
                break;
            }

            // If multiple dumps present, use the most recent by filename (date is in the name)
            var dumpFile = dumps.OrderDescending().First();

            Console.WriteLine($"""

  ┌─ Dump Import ─────────────────────────────────────────────────────────────┐
  │ Importa dal dump giornaliero ufficiale TMDB (~900k voci).                 │
  │ Pre-filtri  : adult=false, video=false, popularity >= {settings.MinPopularity:F1}             │
  │ Post-filtri : runtime >= {settings.MinRuntimeMinutes} min                                      │
  │ Riprendibile: Ctrl+C per pausa, [5] per continuare.                       │
  │ ⚠  Richiede migrazione 005 applicata prima di avviare.                   │
  └───────────────────────────────────────────────────────────────────────────┘

  File: {Path.GetFileName(dumpFile)}
""");
            Console.Write("  Confermi? [S/N] ");
            if (char.ToUpper(Console.ReadKey(intercept: true).KeyChar) != 'S') { Console.WriteLine("\n  Annullato.\n"); break; }
            Console.WriteLine("\n");
            stats = await dumpImporter!.RunAsync(dumpFile, cts.Token);
            break;
        }

        case '6':
        {
            var dataDir6 = Directory.Exists("Data") ? "Data" : Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir6);
            var tvDumps = Directory.GetFiles(dataDir6, "*tv_series_ids*.json.gz")
                .Concat(Directory.GetFiles(dataDir6, "*tv_series_ids*.json"))
                .ToArray();

            if (tvDumps.Length == 0)
            {
                PrintError($"""
Nessun file dump TV trovato in:
  {dataDir6}

Scarica il dump da:
  http://files.tmdb.org/p/exports/tv_series_ids_MM_DD_YYYY.json.gz
e copialo in quella cartella (formato .json.gz o .json estratto).
""");
                break;
            }

            if (!await db!.TvDumpMigrationAppliedAsync())
            {
                PrintError("Migrazione 006 non applicata. Esegui migrations/006_tv_dump_schema.sql prima.");
                break;
            }

            var tvDumpFile = tvDumps.OrderDescending().First();

            Console.WriteLine($"""

  ┌─ TV Dump Import ──────────────────────────────────────────────────────────┐
  │ Importa serie TV dal dump giornaliero ufficiale TMDB (~230k voci).        │
  │ Pre-filtri  : adult=false, popularity >= {settings.MinPopularity:F1}                        │
  │ Riprendibile: Ctrl+C per pausa, [6] per continuare.                       │
  │ ⚠  Richiede migrazione 006 applicata prima di avviare.                   │
  └───────────────────────────────────────────────────────────────────────────┘

  File: {Path.GetFileName(tvDumpFile)}
""");
            Console.Write("  Confermi? [S/N] ");
            if (char.ToUpper(Console.ReadKey(intercept: true).KeyChar) != 'S') { Console.WriteLine("\n  Annullato.\n"); break; }
            Console.WriteLine("\n");
            stats = await tvDumpImporter!.RunAsync(tvDumpFile, cts.Token);
            break;
        }
    }

    Console.CancelKeyPress -= cancelHandler;
    sw.Stop();
    if (stats != null) PrintSummary(stats, sw.Elapsed);
}

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.DarkRed;
    Console.WriteLine("""
╔══════════════════════════════════════════════════╗
║       HorrorFriday  ·  TMDB Importer             ║
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
│       (lascia dettagli a Mode [3])                                       │
│                                                                          │
│  [5]  Dump Film          — importa film dal dump giornaliero TMDB        │
│       (~900k voci, filtri runtime/popularity, riprendibile)              │
│       ⚠  Richiede migrazione 005 e dump in Data/                        │
│                                                                          │
│  [6]  Dump Serie TV      — importa serie TV dal dump giornaliero TMDB    │
│       (~230k voci, filtro popularity, riprendibile)                      │
│       ⚠  Richiede migrazione 006 e dump in Data/                        │
│                                                                          │
│  [Q]  Esci                                                               │
└──────────────────────────────────────────────────────────────────────────┘
""");
    Console.Write("  Scelta [1-6/Q]: ");
}

static void PrintSummary(SyncStats s, TimeSpan elapsed)
{
    var elapsedStr = elapsed.ToString(@"hh\:mm\:ss");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"""

  ┌─ Riepilogo ───────────────────────────────────────┐
  │  Nuovi film inseriti     : {s.NewMoviesInserted,8:N0}                 │
  │  Nuove serie inserite    : {s.NewTvInserted,8:N0}                 │
  │  Record arricchiti       : {s.Enriched,8:N0}                 │
  │  Certificazioni aggiorn. : {s.CertificationsUpdated,8:N0}                 │
  │  Provider aggiornati     : {s.ProvidersUpdated,8:N0}                 │
  │  Saltati / filtrati      : {s.Skipped,8:N0}                 │
  │  Errori                  : {s.Errors,8:N0}                 │
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
