using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonteCarloSimulation.Web
{
    // The figures behind the Annuity tab's suggested SPIA rate: one set of index figures and the model fitted to them.
    public sealed record SpiaFigures(
        SpiaCells Cells, string AsOf, string Basis, string Attribution, string SourceUrl, string LicenseUrl)
    {
        public SpiaPayoutModel Model { get; } = SpiaPayoutModel.Fit(Cells);
    }

    // The LifeAnnuities.us SPIA Payout Index (CC BY 4.0: free to republish with attribution and a link), as the Annuity
    // tab's suggested rate uses it. It's published monthly, so it's fetched at most once a week, with no scheduled job:
    // the first rate request after Monday 6:00 AM ("SpiaRates:TimeZone", Mountain time by default) fetches it, and every
    // other request uses the stored figures. A fetch that fails for any reason - network, timeout, a non-200, figures
    // missing - is only logged: the stored figures stay, with their own as-of date, and the next try is an hour later.
    // The visitor never hears about it.
    //
    // The last good figures, and when they were fetched and last tried, are kept in a small JSON file so a restart doesn't
    // fetch again, found the same way as the site flags: "SpiaRates:Path" if set, else /home/data/montecarlo on App
    // Service, else App_Data under the web project (gitignored). Before anything has been fetched, the figures are the
    // committed copy of the index, SpiaPayoutIndex.seed.json.
    public sealed class SpiaPayoutIndex
    {
        public const string HttpClientName = "spia-index";
        public const string DefaultUrl = "https://lifeannuities.us/rates/spia-payout-index.json";
        public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);
        private static readonly TimeSpan RefreshTime = TimeSpan.FromHours(6); // Monday 6:00 AM, local

        private readonly IHttpClientFactory _httpClients;
        private readonly TimeProvider _clock;
        private readonly ILogger<SpiaPayoutIndex> _logger;
        private readonly string _url;
        private readonly TimeZoneInfo _timeZone;
        private readonly string _seedPath;
        private readonly SemaphoreSlim _fetching = new(1, 1);
        private volatile Stored _stored;

        // What the store file holds: the figures, and when they were fetched (null for the seed) and last tried
        private sealed record Stored(SpiaFigures Figures, DateTimeOffset? FetchedAt, DateTimeOffset? LastAttempt);

        public SpiaPayoutIndex(
            IHttpClientFactory httpClients, IConfiguration config, IWebHostEnvironment environment, TimeProvider clock,
            ILogger<SpiaPayoutIndex> logger)
        {
            _httpClients = httpClients;
            _clock = clock;
            _logger = logger;
            _url = config["SpiaRates:IndexUrl"] ?? DefaultUrl;
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(config["SpiaRates:TimeZone"] ?? VisitCounter.DefaultTimeZone);
            FilePath = config["SpiaRates:Path"]
                ?? (Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null
                    ? "/home/data/montecarlo/spia-index.json"
                    : Path.Combine(environment.ContentRootPath, "App_Data", "spia-index.json"));
            _seedPath = Path.Combine(environment.ContentRootPath, "SpiaPayoutIndex.seed.json");
            _stored = ReadFile() ?? new Stored(Seed(), null, null);
        }

        public string FilePath { get; }

        // The figures to use now, fetching first when a refresh is due. Never throws for a failed fetch.
        public async Task<SpiaFigures> CurrentAsync(CancellationToken cancellationToken = default)
        {
            if (!Due(_stored)) return _stored.Figures;
            // One fetch at a time; requests arriving meanwhile use the stored figures rather than wait
            if (!await _fetching.WaitAsync(0, cancellationToken)) return _stored.Figures;
            try
            {
                // Another instance sharing the file may have fetched already
                if (ReadFile() is { } onFile) _stored = onFile;
                if (Due(_stored)) await FetchAsync(cancellationToken);
            }
            finally
            {
                _fetching.Release();
            }
            return _stored.Figures;
        }

        // The most recent Monday 6:00 AM, local, as an instant
        public DateTimeOffset LastRefreshPoint(DateTimeOffset now)
        {
            var local = TimeZoneInfo.ConvertTime(now, _timeZone);
            int sinceMonday = ((int)local.DayOfWeek + 6) % 7;
            var point = local.Date.AddDays(-sinceMonday) + RefreshTime;
            if (point > local.DateTime) point = point.AddDays(-7);
            var unspecified = DateTime.SpecifyKind(point, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, _timeZone.GetUtcOffset(unspecified));
        }

        // Due when the figures predate this week's refresh point, unless a try since then failed less than an hour ago
        private bool Due(Stored stored)
        {
            var now = _clock.GetUtcNow();
            var point = LastRefreshPoint(now);
            if (stored.FetchedAt is { } fetched && fetched >= point) return false;
            return stored.LastAttempt is not { } tried || tried < point || now - tried >= RetryAfterFailure;
        }

        private async Task FetchAsync(CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(FetchTimeout);
                var client = _httpClients.CreateClient(HttpClientName);
                using var response = await client.GetAsync(_url, timeout.Token);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync(timeout.Token);
                var figures = Parse(json) ?? throw new FormatException("The index is missing figures.");
                Save(new Stored(figures, now, now));
                _logger.LogInformation("SPIA payout index fetched: as of {AsOf}", figures.AsOf);
            }
            catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Kept quiet for the visitor: the stored figures stay, and the next try is an hour away
                _logger.LogWarning(error, "SPIA payout index fetch from {Url} failed; keeping the figures as of {AsOf}", _url, _stored.Figures.AsOf);
                Save(_stored with { LastAttempt = now });
            }
        }

        // The index's current figures and how to credit them, or null when anything needed is missing or not positive
        public static SpiaFigures? Parse(string json)
        {
            try
            {
                var root = JsonNode.Parse(json);
                var current = root?["current"];
                if (current is null) return null;
                double Cell(string name) => current[name]?.GetValue<double>() ?? double.NaN;
                var cells = new SpiaCells(Cell("male_65"), Cell("male_70"), Cell("male_75"), Cell("female_65"), Cell("joint_65"));
                string? asOf = current["asOf"]?.GetValue<string>();
                if (!cells.Valid || string.IsNullOrWhiteSpace(asOf)) return null;
                return new SpiaFigures(
                    cells,
                    asOf,
                    root!["basis"]?.GetValue<string>() ?? current["basis"]?.GetValue<string>() ?? "",
                    root!["attribution"]?.GetValue<string>() ?? "Source: LifeAnnuities.us SPIA Payout Index (CC BY 4.0)",
                    root["url"]?.GetValue<string>() ?? "https://lifeannuities.us/rates/spia-payout-index/",
                    root["license"]?.GetValue<string>() ?? "https://creativecommons.org/licenses/by/4.0/");
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        private SpiaFigures Seed() =>
            Parse(File.ReadAllText(_seedPath)) ?? throw new InvalidOperationException($"{_seedPath} has no usable figures.");

        private sealed record StoredFile(
            double Male65, double Male70, double Male75, double Female65, double Joint65, string AsOf, string Basis,
            string Attribution, string SourceUrl, string LicenseUrl, DateTimeOffset? FetchedAt, DateTimeOffset? LastAttempt);

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        private Stored? ReadFile()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(FilePath), JsonOptions);
                if (file is null) return null;
                var cells = new SpiaCells(file.Male65, file.Male70, file.Male75, file.Female65, file.Joint65);
                if (!cells.Valid || string.IsNullOrWhiteSpace(file.AsOf)) return null;
                return new Stored(
                    new SpiaFigures(cells, file.AsOf, file.Basis, file.Attribution, file.SourceUrl, file.LicenseUrl),
                    file.FetchedAt, file.LastAttempt);
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(error, "Couldn't read {Path}; using the figures in memory", FilePath);
                return null;
            }
        }

        private void Save(Stored stored)
        {
            _stored = stored;
            var f = stored.Figures;
            var file = new StoredFile(f.Cells.Male65, f.Cells.Male70, f.Cells.Male75, f.Cells.Female65, f.Cells.Joint65,
                f.AsOf, f.Basis, f.Attribution, f.SourceUrl, f.LicenseUrl, stored.FetchedAt, stored.LastAttempt);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Written whole to a temporary file and moved into place, so a reader never sees half a file
                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
                File.Move(temporary, FilePath, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Still in memory; only a restart would fetch again
                _logger.LogWarning(error, "Couldn't save {Path}", FilePath);
            }
        }
    }
}
