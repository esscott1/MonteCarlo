using System.Text.Json;

namespace MonteCarloSimulation.Web
{
    // Counts the site's visits per day for the Observe page's Visits section. A visit is one browser session: visit.js on
    // the public pages reports it once per tab (POST /api/visits). Days are local days in "Visits:TimeZone" (Mountain time
    // by default). The counts are kept in a small JSON file, { "2026-10-08": 13, ... }, where restarts and deploys don't
    // touch it, found the same way as the site flags: "Visits:Path" if set, else /home/data/montecarlo on App Service,
    // else App_Data under the web project (gitignored).
    public sealed class VisitCounter
    {
        public const int ReportDays = 90;
        public const string DefaultTimeZone = "America/Denver";

        private readonly TimeProvider _clock;
        private readonly ILogger<VisitCounter> _logger;
        private readonly object _gate = new();

        public VisitCounter(IConfiguration config, IWebHostEnvironment environment, TimeProvider clock, ILogger<VisitCounter> logger)
        {
            _clock = clock;
            _logger = logger;
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(config["Visits:TimeZone"] ?? DefaultTimeZone);
            FilePath = config["Visits:Path"]
                ?? (Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null
                    ? "/home/data/montecarlo/visits.json"
                    : Path.Combine(environment.ContentRootPath, "App_Data", "visits.json"));
        }

        public string FilePath { get; }

        public TimeZoneInfo TimeZone { get; }

        public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), TimeZone).DateTime);

        public void Record()
        {
            lock (_gate)
            {
                // Re-read first, so a visit another instance just counted isn't overwritten. If the file can't be read,
                // this visit goes uncounted rather than replacing the history with one day.
                if (ReadFile() is not { } counts) return;
                var today = Today;
                counts[today] = counts.GetValueOrDefault(today) + 1;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Written whole to a temporary file and moved into place, so a reader never sees half a file
                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(counts, JsonOptions));
                File.Move(temporary, FilePath, overwrite: true);
            }
        }

        // The last 90 days (today included, so today's count is still growing), oldest first, days without visits as 0
        public VisitsView Report()
        {
            Dictionary<DateOnly, int> counts;
            lock (_gate) counts = ReadFile() ?? new();

            var today = Today;
            var days = new List<VisitDay>(ReportDays);
            for (int back = ReportDays - 1; back >= 0; back--)
            {
                var date = today.AddDays(-back);
                days.Add(new VisitDay(date, counts.GetValueOrDefault(date)));
            }

            DateOnly? since = counts.Count > 0 ? counts.Keys.Min() : null;
            int Last(int n) => days.Skip(ReportDays - n).Sum(d => d.Visits);
            // Averaged over the days counted, so days before the counter existed don't pull it down
            int countedDays = since is DateOnly first ? Math.Clamp(today.DayNumber - first.DayNumber + 1, 1, 30) : 30;

            return new VisitsView(TimeZone.Id, today, since, days, Last(7), Last(30), Last(90),
                Math.Round((double)Last(30) / countedDays, 1));
        }

        private Dictionary<DateOnly, int>? ReadFile()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<DateOnly, int>>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                    : new();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogError(e, "Couldn't read the visit counts at {Path}.", FilePath);
                return null;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    }

    public sealed record VisitDay(DateOnly Date, int Visits);

    public sealed record VisitsView(
        string TimeZone,
        DateOnly Today,
        DateOnly? CountingSince,
        IReadOnlyList<VisitDay> Days,
        int Last7,
        int Last30,
        int Last90,
        double AverageDaily30);
}
