using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HorrorFriday.Web.Models;
using HorrorFriday.Web.Shared;
using HorrorFriday.Web.Services;

namespace HorrorFriday.Web.Pages;

public partial class Home : ComponentBase
{
    // ─────────────── Injected services ───────────────

    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private AuthService AuthService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ─────────────── UI state ───────────────

    protected bool IsAdvancedOpen { get; set; }
    protected bool IsAiMode { get; set; } = false;
    protected string? SearchError { get; set; }

    // ─────────────── Search state ───────────────

    protected string SearchQuery { get; set; } = string.Empty;

    // ─────────────── Genre filter (tri-state) ───────────────

    protected List<string> AllGenres { get; set; } = new();
    protected HashSet<string> IncludedGenres { get; set; } = new();
    protected HashSet<string> ExcludedGenres { get; set; } = new();

    // ─────────────── Media type filter ───────────────

    protected string MediaType { get; set; } = "all";

    // ─────────────── Region / Provider / Certification filters ───────────────

    protected string? SelectedRegion { get; set; }
    protected List<string> AllRegions { get; set; } = new();
    protected List<ProviderDto> AllProviders { get; set; } = new();
    protected List<string> AllCertifications { get; set; } = new();
    protected HashSet<int> SelectedProviderIds { get; set; } = new();
    protected HashSet<string> SelectedCertifications { get; set; } = new();

    protected IEnumerable<RegionOption> RegionOptions =>
        new[] { new RegionOption("", "Any country") }
        .Concat(AllRegions.Select(r => new RegionOption(r, GetRegionLabel(r))));

    protected string GetRegionLabel(string code) =>
        RegionNames.TryGetValue(code, out var name) ? $"{name} ({code})" : code;

    protected string? GetRegionName(string code) =>
        RegionNames.TryGetValue(code, out var name) ? name : code;

    public record RegionOption(string Value, string Label);

    private static readonly Dictionary<string, string> RegionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AD"] = "Andorra", ["AE"] = "UAE", ["AG"] = "Antigua & Barbuda",
        ["AL"] = "Albania", ["AO"] = "Angola", ["AR"] = "Argentina",
        ["AT"] = "Austria", ["AU"] = "Australia", ["AZ"] = "Azerbaijan",
        ["BA"] = "Bosnia & Herzegovina", ["BB"] = "Barbados", ["BE"] = "Belgium",
        ["BF"] = "Burkina Faso", ["BG"] = "Bulgaria", ["BH"] = "Bahrain",
        ["BJ"] = "Benin", ["BO"] = "Bolivia", ["BR"] = "Brazil",
        ["BS"] = "Bahamas", ["BT"] = "Bhutan", ["BW"] = "Botswana",
        ["BY"] = "Belarus", ["BZ"] = "Belize", ["CA"] = "Canada",
        ["CD"] = "Congo (DRC)", ["CH"] = "Switzerland", ["CI"] = "Côte d'Ivoire",
        ["CL"] = "Chile", ["CM"] = "Cameroon", ["CO"] = "Colombia",
        ["CR"] = "Costa Rica", ["CV"] = "Cape Verde", ["CY"] = "Cyprus",
        ["CZ"] = "Czech Republic", ["DE"] = "Germany", ["DK"] = "Denmark",
        ["DO"] = "Dominican Republic", ["DZ"] = "Algeria", ["EC"] = "Ecuador",
        ["EE"] = "Estonia", ["EG"] = "Egypt", ["ES"] = "Spain",
        ["ET"] = "Ethiopia", ["FI"] = "Finland", ["FJ"] = "Fiji",
        ["FR"] = "France", ["GA"] = "Gabon", ["GB"] = "United Kingdom",
        ["GH"] = "Ghana", ["GM"] = "Gambia", ["GQ"] = "Equatorial Guinea",
        ["GR"] = "Greece", ["GT"] = "Guatemala", ["GW"] = "Guinea-Bissau",
        ["GY"] = "Guyana", ["HK"] = "Hong Kong", ["HN"] = "Honduras",
        ["HR"] = "Croatia", ["HU"] = "Hungary", ["ID"] = "Indonesia",
        ["IE"] = "Ireland", ["IL"] = "Israel", ["IN"] = "India",
        ["IQ"] = "Iraq", ["IS"] = "Iceland", ["IT"] = "Italy",
        ["JM"] = "Jamaica", ["JO"] = "Jordan", ["JP"] = "Japan",
        ["KE"] = "Kenya", ["KR"] = "South Korea", ["KW"] = "Kuwait",
        ["LB"] = "Lebanon", ["LC"] = "Saint Lucia", ["LI"] = "Liechtenstein",
        ["LT"] = "Lithuania", ["LU"] = "Luxembourg", ["LV"] = "Latvia",
        ["LY"] = "Libya", ["MA"] = "Morocco", ["MC"] = "Monaco",
        ["MD"] = "Moldova", ["ME"] = "Montenegro", ["MG"] = "Madagascar",
        ["MK"] = "North Macedonia", ["ML"] = "Mali", ["MT"] = "Malta",
        ["MU"] = "Mauritius", ["MW"] = "Malawi", ["MX"] = "Mexico",
        ["MY"] = "Malaysia", ["MZ"] = "Mozambique", ["NA"] = "Namibia",
        ["NE"] = "Niger", ["NG"] = "Nigeria", ["NI"] = "Nicaragua",
        ["NL"] = "Netherlands", ["NO"] = "Norway", ["NP"] = "Nepal",
        ["NZ"] = "New Zealand", ["OM"] = "Oman", ["PA"] = "Panama",
        ["PE"] = "Peru", ["PH"] = "Philippines", ["PK"] = "Pakistan",
        ["PL"] = "Poland", ["PS"] = "Palestine", ["PT"] = "Portugal",
        ["PW"] = "Palau", ["PY"] = "Paraguay", ["QA"] = "Qatar",
        ["RO"] = "Romania", ["RS"] = "Serbia", ["RU"] = "Russia",
        ["RW"] = "Rwanda", ["SA"] = "Saudi Arabia", ["SC"] = "Seychelles",
        ["SE"] = "Sweden", ["SG"] = "Singapore", ["SI"] = "Slovenia",
        ["SK"] = "Slovakia", ["SL"] = "Sierra Leone", ["SM"] = "San Marino",
        ["SN"] = "Senegal", ["SV"] = "El Salvador", ["TD"] = "Chad",
        ["TH"] = "Thailand", ["TJ"] = "Tajikistan", ["TL"] = "Timor-Leste",
        ["TN"] = "Tunisia", ["TR"] = "Turkey", ["TT"] = "Trinidad & Tobago",
        ["TW"] = "Taiwan", ["TZ"] = "Tanzania", ["UA"] = "Ukraine",
        ["UG"] = "Uganda", ["US"] = "United States", ["UY"] = "Uruguay",
        ["UZ"] = "Uzbekistan", ["VA"] = "Vatican City", ["VC"] = "St. Vincent",
        ["VE"] = "Venezuela", ["XK"] = "Kosovo", ["YE"] = "Yemen",
        ["ZA"] = "South Africa", ["ZM"] = "Zambia", ["ZW"] = "Zimbabwe",
    };

    // ─────────────── Advanced filter state ───────────────

    protected string YearFromText { get; set; } = "";
    protected string YearToText { get; set; } = "";
    protected string MinRatingText { get; set; } = "";
    protected string MaxRatingText { get; set; } = "";
    protected string MaxRuntimeText { get; set; } = "";
    private const int DefaultMinVoteCount = 50;
    private const string DefaultSortBy = "popularity";
    private const string DefaultSortDirection = "desc";

    protected int? MinVoteCount { get; set; } = DefaultMinVoteCount;
    protected string? Language { get; set; }
    protected string SortBy { get; set; } = DefaultSortBy;
    protected string SortDirection { get; set; } = DefaultSortDirection;
    protected bool IncludeUpcoming { get; set; } = false;

    // ─────────────── Results state ───────────────

    protected List<MovieDto>? Movies { get; set; }
    protected int TotalCount { get; set; }
    protected int CurrentPage { get; set; } = 1;
    protected int TotalPages { get; set; }
    protected bool IsLoading { get; set; }

    private const int PageSize = 24;

    // ─────────────── User movie statuses ───────────────

    protected Dictionary<int, string> MovieStatuses { get; set; } = new();
    protected int? OpenStatusPickerId { get; set; }

    // ─────────────── Hide-from-results filter ───────────────

    protected HashSet<string> HideStatuses { get; set; } = new();

    protected static readonly List<(string Value, string Label)> AllStatuses = new()
    {
        ("to_watch", "To Watch"),
        ("watching",  "Watching"),
        ("watched",   "Watched"),
        ("dropped",   "Dropped")
    };

    // ─────────────── Sort options ───────────────

    protected static readonly List<SortOption> SortOptions = new()
    {
        new("popularity", "Popularity"),
        new("rating", "Rating"),
        new("year", "Release Year"),
        new("title", "Title"),
        new("vote_count", "Vote Count"),
        new("revenue", "Revenue")
    };

    // ─────────────── Computed ───────────────

    protected int ActiveFilterCount =>
        IncludedGenres.Count + ExcludedGenres.Count
        + (!string.IsNullOrEmpty(YearFromText) || !string.IsNullOrEmpty(YearToText) ? 1 : 0)
        + (!string.IsNullOrEmpty(MinRatingText) || !string.IsNullOrEmpty(MaxRatingText) ? 1 : 0)
        + (!string.IsNullOrEmpty(MaxRuntimeText) ? 1 : 0)
        + (MinVoteCount != DefaultMinVoteCount ? 1 : 0)
        + (!string.IsNullOrEmpty(Language) ? 1 : 0)
        + (SortBy != DefaultSortBy || SortDirection != DefaultSortDirection ? 1 : 0)
        + (IncludeUpcoming ? 1 : 0)
        + HideStatuses.Count
        + (MediaType != "all" ? 1 : 0)
        + (!string.IsNullOrEmpty(SelectedRegion) ? 1 : 0)
        + SelectedProviderIds.Count
        + SelectedCertifications.Count;

    // ─────────────── Parsed values (with validation) ───────────────

    private int? ParsedYearFrom => TryParseYear(YearFromText);
    private int? ParsedYearTo => TryParseYear(YearToText);
    private decimal? ParsedMinRating => TryParseRating(MinRatingText);
    private decimal? ParsedMaxRating => TryParseRating(MaxRatingText);
    private int? ParsedMaxRuntime => TryParseRuntime(MaxRuntimeText);

    // ─────────────── Lifecycle ───────────────

    private bool _pendingScrollRestore;
    private double _scrollRestoreY;

    protected override async Task OnInitializedAsync()
    {
        await AuthService.InitializeAsync();

        var restored = await TryRestoreFilterStateAsync();
        if (!restored)
        {
            await Task.WhenAll(LoadGenres(), LoadRegions(), LoadProviders());
            await TryPreSelectRegionFromBrowserAsync();
        }

        await ExecuteSearch();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingScrollRestore)
        {
            _pendingScrollRestore = false;
            try { await JS.InvokeVoidAsync("hfScrollTo", _scrollRestoreY); }
            catch { }
        }
    }

    private async Task<bool> TryRestoreFilterStateAsync()
    {
        try
        {
            var json = await JS.InvokeAsync<string?>("hfLoadState", "hf_filter_state");
            if (string.IsNullOrEmpty(json)) return false;

            await JS.InvokeVoidAsync("hfClearState", "hf_filter_state");

            var state = System.Text.Json.JsonSerializer.Deserialize<FilterState>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (state is null) return false;

            SearchQuery     = state.SearchQuery ?? "";
            IsAiMode        = state.IsAiMode;
            IsAdvancedOpen  = state.IsAdvancedOpen;
            MediaType       = state.MediaType;
            IncludedGenres  = state.IncludedGenres.ToHashSet();
            ExcludedGenres  = state.ExcludedGenres.ToHashSet();
            HideStatuses    = state.HideStatuses.ToHashSet();
            YearFromText    = state.YearFromText;
            YearToText      = state.YearToText;
            MinRatingText   = state.MinRatingText;
            MaxRatingText   = state.MaxRatingText;
            MaxRuntimeText  = state.MaxRuntimeText;
            SortBy          = state.SortBy;
            SortDirection   = state.SortDirection;
            IncludeUpcoming = state.IncludeUpcoming;
            CurrentPage     = state.CurrentPage;

            await Task.WhenAll(LoadGenres(), LoadRegions());

            if (!string.IsNullOrEmpty(state.SelectedRegion))
            {
                SelectedRegion = state.SelectedRegion;
                await Task.WhenAll(LoadProviders(), LoadCertifications());
                SelectedProviderIds    = state.SelectedProviderIds.ToHashSet();
                SelectedCertifications = state.SelectedCertifications.ToHashSet();
            }
            else
            {
                await LoadProviders();
            }

            _pendingScrollRestore = true;
            _scrollRestoreY       = state.ScrollY;

            return true;
        }
        catch { return false; }
    }

    private async Task TryPreSelectRegionFromBrowserAsync()
    {
        try
        {
            var code = await JS.InvokeAsync<string>("getBrowserRegion");
            if (!string.IsNullOrEmpty(code) && AllRegions.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                SelectedRegion = code.ToUpperInvariant();
                await Task.WhenAll(LoadProviders(), LoadCertifications());
            }
        }
        catch { }
    }

    // ─────────────── Data loading ───────────────

    private async Task LoadGenres()
    {
        try
        {
            var result = await Http.GetFromJsonAsync<List<string>>("api/movies/genres");
            if (result is not null)
                AllGenres = result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Home] Failed to load genres: {ex.Message}");
        }
    }

    private async Task LoadRegions()
    {
        try
        {
            var result = await Http.GetFromJsonAsync<List<string>>("api/movies/regions");
            if (result is not null)
                AllRegions = result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Home] Failed to load regions: {ex.Message}");
        }
    }

    private async Task LoadProviders()
    {
        try
        {
            var url = string.IsNullOrEmpty(SelectedRegion)
                ? "api/movies/providers"
                : $"api/movies/providers?region={Uri.EscapeDataString(SelectedRegion)}";
            var result = await Http.GetFromJsonAsync<List<ProviderDto>>(url);
            if (result is not null)
                AllProviders = result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Home] Failed to load providers: {ex.Message}");
        }
    }

    private async Task LoadCertifications()
    {
        if (string.IsNullOrEmpty(SelectedRegion))
        {
            AllCertifications.Clear();
            return;
        }
        try
        {
            var url = $"api/movies/certifications?region={Uri.EscapeDataString(SelectedRegion)}";
            var result = await Http.GetFromJsonAsync<List<string>>(url);
            if (result is not null)
                AllCertifications = result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Home] Failed to load certifications: {ex.Message}");
        }
    }

    // ─────────────── Search ───────────────

    protected async Task ExecuteSearch()
    {
        OpenStatusPickerId = null;
        SearchError = null;
        IsLoading = true;
        StateHasChanged();

        try
        {
            var request = new SearchRequest
            {
                Query = !IsAiMode && !string.IsNullOrWhiteSpace(SearchQuery)
                    ? SearchQuery.Trim() : null,
                SemanticQuery = IsAiMode && !string.IsNullOrWhiteSpace(SearchQuery)
                    ? SearchQuery.Trim() : null,
                IncludeGenres = IncludedGenres.Count > 0 ? IncludedGenres.ToList() : null,
                ExcludeGenres = ExcludedGenres.Count > 0 ? ExcludedGenres.ToList() : null,
                YearFrom = ParsedYearFrom,
                YearTo = IncludeUpcoming ? null : (ParsedYearTo ?? DateTime.Now.Year),
                MaxRuntime = ParsedMaxRuntime,
                MinRating = ParsedMinRating,
                MaxRating = ParsedMaxRating,
                MinVoteCount = MinVoteCount,
                Language = Language,
                SortBy = SortBy,
                SortDirection = SortDirection,
                Page = CurrentPage,
                PageSize = PageSize,
                HideStatuses = AuthService.IsLoggedIn && HideStatuses.Count > 0
                    ? HideStatuses.ToList() : null,
                MediaType = MediaType != "all" ? MediaType : null,
                Region = SelectedRegion,
                Certifications = SelectedCertifications.Count > 0 ? SelectedCertifications.ToList() : null,
                ProviderIds = SelectedProviderIds.Count > 0 ? SelectedProviderIds.ToList() : null
            };

            // Send auth header so the backend can apply the hide-statuses filter
            var searchReq = new HttpRequestMessage(HttpMethod.Post, "api/movies/search")
            {
                Content = JsonContent.Create(request)
            };
            if (AuthService.IsLoggedIn)
                searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthService.Token);

            var response = await Http.SendAsync(searchReq);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PagedResult<MovieDto>>();
                if (result is not null)
                {
                    Movies = result.Items;
                    TotalCount = result.TotalCount;
                    TotalPages = result.TotalPages;
                }
            }
            else
            {
                Console.WriteLine($"[Home] Search returned {response.StatusCode}");
                try
                {
                    var errBody = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    SearchError = errBody.GetProperty("error").GetString();
                }
                catch
                {
                    SearchError = $"Search error ({(int)response.StatusCode}).";
                }
                SetEmptyResults();
            }

            // Load user statuses for the returned movies
            if (AuthService.IsLoggedIn && Movies?.Count > 0)
                await LoadMovieStatusesAsync(Movies.Select(m => m.Id).ToList());
            else
                MovieStatuses.Clear();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Home] Search failed: {ex.Message}");
            SetEmptyResults();
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    protected async Task ExecuteSearchFromButton()
    {
        CurrentPage = 1;
        await ExecuteSearch();
    }

    // ─────────────── User movie statuses ───────────────

    private async Task LoadMovieStatusesAsync(List<int> movieIds)
    {
        try
        {
            var response = await AuthService.SendAuthorizedAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "api/user/movies/statuses")
                {
                    Content = JsonContent.Create(new { MovieIds = movieIds })
                };
                return request;
            });
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var statuses = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(
                    body,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                MovieStatuses = statuses ?? new();
                Console.WriteLine($"[Home] Loaded {MovieStatuses.Count} statuses for {movieIds.Count} movies.");
            }
            else
            {
                Console.WriteLine($"[Home] LoadStatuses failed {response.StatusCode}: {body}");
                MovieStatuses = new();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Home] LoadStatuses exception: {ex.Message}");
            MovieStatuses = new();
        }
    }

    protected async Task HandleMovieStatusChanged(MovieStatusChange change)
    {
        await SetMovieStatus(change.MovieId, change.Status);
    }

    private async Task SetMovieStatus(int movieId, string? status)
    {
        OpenStatusPickerId = null;

        try
        {
            HttpResponseMessage response;

            if (status is null)
            {
                response = await AuthService.SendAuthorizedAsync(
                    () => new HttpRequestMessage(HttpMethod.Delete, $"api/user/movies/{movieId}"));
            }
            else
            {
                response = await AuthService.SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Put, $"api/user/movies/{movieId}")
                {
                    Content = JsonContent.Create(new { Status = status })
                });
            }

            if (response.IsSuccessStatusCode)
            {
                if (status is null)
                    MovieStatuses.Remove(movieId);
                else
                    MovieStatuses[movieId] = status;
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[Home] SetMovieStatus failed {response.StatusCode}: {body}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Home] SetMovieStatus exception: {ex.Message}");
        }

        StateHasChanged();
    }

    protected void ToggleStatusPicker(int movieId)
    {
        OpenStatusPickerId = OpenStatusPickerId == movieId ? null : movieId;
        StateHasChanged();
    }

    protected void CloseStatusPicker()
    {
        OpenStatusPickerId = null;
        StateHasChanged();
    }

    protected async Task ToggleHideStatus(string status)
    {
        if (HideStatuses.Contains(status))
            HideStatuses.Remove(status);
        else
            HideStatuses.Add(status);

        CurrentPage = 1;
        await ExecuteSearch();
    }

    protected async Task SetMediaType(string type)
    {
        MediaType = type;
        CurrentPage = 1;
        await ExecuteSearch();
    }

    protected async Task OnRegionChangedAsync(string value)
    {
        SelectedRegion = string.IsNullOrEmpty(value) ? null : value;
        SelectedProviderIds.Clear();
        SelectedCertifications.Clear();
        await Task.WhenAll(LoadProviders(), LoadCertifications());
        CurrentPage = 1;
        await ExecuteSearch();
    }

    protected async Task ToggleProvider(int providerId)
    {
        if (SelectedProviderIds.Contains(providerId))
            SelectedProviderIds.Remove(providerId);
        else
            SelectedProviderIds.Add(providerId);
        CurrentPage = 1;
        await ExecuteSearch();
    }

    protected async Task ToggleCertification(string cert)
    {
        if (SelectedCertifications.Contains(cert))
            SelectedCertifications.Remove(cert);
        else
            SelectedCertifications.Add(cert);
        CurrentPage = 1;
        await ExecuteSearch();
    }

    // ─────────────── Input clamping (prevents invalid input) ───────────────

    protected static string ClampYearInput(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var digits = new string(raw.Where(char.IsDigit).Take(4).ToArray());
        if (digits.Length > 0 && digits[0] != '1' && digits[0] != '2')
            digits = "";
        return digits;
    }

    protected static string ClampRatingInput(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var result = new System.Text.StringBuilder();
        bool hasDot = false;
        foreach (var c in raw)
        {
            if (char.IsDigit(c)) result.Append(c);
            else if (c == '.' && !hasDot) { result.Append(c); hasDot = true; }
        }
        var text = result.ToString();
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var val) && val > 10)
            return "10";
        return text;
    }

    protected static string ClampRuntimeInput(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return new string(raw.Where(char.IsDigit).Take(3).ToArray());
    }

    // ─────────────── Parse helpers ───────────────

    private static int? TryParseYear(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return int.TryParse(text, out var v) && v >= 1888 && v <= 2030 ? v : null;
    }

    private static decimal? TryParseRating(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return decimal.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 && v <= 10 ? v : null;
    }

    private static int? TryParseRuntime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return int.TryParse(text, out var v) && v >= 1 && v <= 600 ? v : null;
    }

    // ─────────────── User interactions ───────────────

    protected async Task HandleSearchKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            CurrentPage = 1;
            await ExecuteSearch();
        }
    }

    protected async Task ToggleGenre(string genre)
    {
        if (IncludedGenres.Contains(genre))
        {
            IncludedGenres.Remove(genre);
            ExcludedGenres.Add(genre);
        }
        else if (ExcludedGenres.Contains(genre))
        {
            ExcludedGenres.Remove(genre);
        }
        else
        {
            IncludedGenres.Add(genre);
        }

        CurrentPage = 1;
        await ExecuteSearch();
    }

    protected string GetGenreState(string genre)
    {
        if (IncludedGenres.Contains(genre)) return "included";
        if (ExcludedGenres.Contains(genre)) return "excluded";
        return "";
    }

    protected void ToggleAdvanced()
    {
        IsAdvancedOpen = !IsAdvancedOpen;
    }

    protected void ToggleAiMode()
    {
        IsAiMode = !IsAiMode;
    }

    protected async Task ClearAllFilters()
    {
        IncludedGenres.Clear();
        ExcludedGenres.Clear();
        HideStatuses.Clear();
        YearFromText = "";
        YearToText = "";
        MinRatingText = "";
        MaxRatingText = "";
        MaxRuntimeText = "";
        MinVoteCount = DefaultMinVoteCount;
        Language = null;
        SortBy = DefaultSortBy;
        SortDirection = DefaultSortDirection;
        IncludeUpcoming = false;
        MediaType = "all";
        SelectedRegion = null;
        SelectedProviderIds.Clear();
        SelectedCertifications.Clear();
        AllCertifications.Clear();
        await LoadProviders();
        CurrentPage = 1;
        await ExecuteSearch();
    }

    protected async Task NavigateToMovie(int movieId)
    {
        try
        {
            var state = new FilterState
            {
                SearchQuery     = SearchQuery,
                IsAiMode        = IsAiMode,
                IsAdvancedOpen  = IsAdvancedOpen,
                MediaType       = MediaType,
                IncludedGenres  = IncludedGenres.ToList(),
                ExcludedGenres  = ExcludedGenres.ToList(),
                HideStatuses    = HideStatuses.ToList(),
                YearFromText    = YearFromText,
                YearToText      = YearToText,
                MinRatingText   = MinRatingText,
                MaxRatingText   = MaxRatingText,
                MaxRuntimeText  = MaxRuntimeText,
                SortBy          = SortBy,
                SortDirection   = SortDirection,
                IncludeUpcoming = IncludeUpcoming,
                SelectedRegion  = SelectedRegion,
                SelectedProviderIds    = SelectedProviderIds.ToList(),
                SelectedCertifications = SelectedCertifications.ToList(),
                CurrentPage     = CurrentPage,
                ScrollY         = await JS.InvokeAsync<double>("hfScrollY")
            };
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            await JS.InvokeVoidAsync("hfSaveState", "hf_filter_state", json);
        }
        catch { }

        Navigation.NavigateTo($"/movie/{movieId}");
    }

    protected async Task HandleLogout()
    {
        await AuthService.LogoutAsync();
        MovieStatuses.Clear();
        StateHasChanged();
    }

    // ─────────────── Pagination ───────────────

    protected async Task PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await ExecuteSearch();
        }
    }

    protected async Task NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await ExecuteSearch();
        }
    }

    // ─────────────── Helpers ───────────────

    private void SetEmptyResults()
    {
        Movies = new();
        TotalCount = 0;
        TotalPages = 0;
    }

    public class SearchRequest
    {
        public string? Query { get; set; }
        public string? SemanticQuery { get; set; }
        public List<string>? IncludeGenres { get; set; }
        public List<string>? ExcludeGenres { get; set; }
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public int? MaxRuntime { get; set; }
        public decimal? MinRating { get; set; }
        public decimal? MaxRating { get; set; }
        public int? MinVoteCount { get; set; }
        public string? Language { get; set; }
        public string? SortBy { get; set; }
        public string? SortDirection { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public List<string>? HideStatuses { get; set; }
        public string? MediaType { get; set; }
        public string? Region { get; set; }
        public List<string>? Certifications { get; set; }
        public List<int>? ProviderIds { get; set; }
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public record SortOption(string Value, string Label);

    private class FilterState
    {
        public string? SearchQuery { get; set; }
        public bool IsAiMode { get; set; }
        public bool IsAdvancedOpen { get; set; }
        public string MediaType { get; set; } = "all";
        public List<string> IncludedGenres { get; set; } = [];
        public List<string> ExcludedGenres { get; set; } = [];
        public List<string> HideStatuses { get; set; } = [];
        public string YearFromText { get; set; } = "";
        public string YearToText { get; set; } = "";
        public string MinRatingText { get; set; } = "";
        public string MaxRatingText { get; set; } = "";
        public string MaxRuntimeText { get; set; } = "";
        public string SortBy { get; set; } = "popularity";
        public string SortDirection { get; set; } = "desc";
        public bool IncludeUpcoming { get; set; }
        public string? SelectedRegion { get; set; }
        public List<int> SelectedProviderIds { get; set; } = [];
        public List<string> SelectedCertifications { get; set; } = [];
        public int CurrentPage { get; set; } = 1;
        public double ScrollY { get; set; }
    }
}
