using System.Text.Json;
using Microsoft.FeatureManagement;

namespace MonteCarloSimulation.Web
{
    // The site-wide switches flipped on the Observe page's Features section: whether Plus is offered and whether Pro is.
    // Each is kept in a small JSON file once flipped, and until then is its default from configuration
    // ("FeatureManagement": TierPlus, TierPro, read by Microsoft.FeatureManagement). The file lives where restarts and
    // deploys don't touch it: "SiteFlags:Path" if set, else /home/data/montecarlo on App Service (its persistent /home
    // share; deploys only replace /home/site/wwwroot), else App_Data under the web project (gitignored).
    public sealed class SiteFlags
    {
        private static readonly TimeSpan RereadAfter = TimeSpan.FromSeconds(5);

        private readonly IFeatureManager _defaults;
        private readonly TimeProvider _clock;
        private readonly ILogger<SiteFlags> _logger;
        private readonly object _gate = new();
        private Dictionary<string, SavedFlag> _saved = new();
        private DateTimeOffset _readAt = DateTimeOffset.MinValue;

        public SiteFlags(IFeatureManager defaults, IConfiguration config, IWebHostEnvironment environment, TimeProvider clock, ILogger<SiteFlags> logger)
        {
            _defaults = defaults;
            _clock = clock;
            _logger = logger;
            FilePath = config["SiteFlags:Path"]
                ?? (Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null
                    ? "/home/data/montecarlo/site-flags.json"
                    : Path.Combine(environment.ContentRootPath, "App_Data", "site-flags.json"));
        }

        public string FilePath { get; }

        public static IReadOnlyList<Tier> PaidTiers { get; } = [Tier.Plus, Tier.Pro];

        public async Task<bool> IsOnAsync(Tier tier) =>
            tier != Tier.Free && (Saved().TryGetValue(Flags.For(tier), out var saved) ? saved.On : await _defaults.IsEnabledAsync(Flags.For(tier)));

        // Each paid tier's switch: on or off, and when it was last flipped (null: never, so its default applies)
        public async Task<IReadOnlyList<SiteFlagState>> ListAsync()
        {
            var saved = Saved();
            var states = new List<SiteFlagState>();
            foreach (var tier in PaidTiers)
            {
                states.Add(saved.TryGetValue(Flags.For(tier), out var flag)
                    ? new SiteFlagState(tier, flag.On, flag.ChangedAt)
                    : new SiteFlagState(tier, await _defaults.IsEnabledAsync(Flags.For(tier)), null));
            }
            return states;
        }

        public void Set(Tier tier, bool on)
        {
            lock (_gate)
            {
                var saved = new Dictionary<string, SavedFlag>(ReadFile()) { [Flags.For(tier)] = new SavedFlag(on, _clock.GetUtcNow()) };
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Written whole to a temporary file and moved into place, so a reader never sees half a file
                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(saved, JsonOptions));
                File.Move(temporary, FilePath, overwrite: true);
                _saved = saved;
                _readAt = _clock.GetUtcNow();
            }
        }

        // The saved switches, re-read from the file at most every few seconds (another instance may have flipped one)
        private Dictionary<string, SavedFlag> Saved()
        {
            lock (_gate)
            {
                if (_clock.GetUtcNow() - _readAt >= RereadAfter)
                {
                    _saved = ReadFile();
                    _readAt = _clock.GetUtcNow();
                }
                return _saved;
            }
        }

        private Dictionary<string, SavedFlag> ReadFile()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, SavedFlag>>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                    : new();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogError(e, "Couldn't read the site flags at {Path}; using the defaults.", FilePath);
                return new();
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        private sealed record SavedFlag(bool On, DateTimeOffset ChangedAt);
    }

    public sealed record SiteFlagState(Tier Tier, bool On, DateTimeOffset? ChangedAt);

    // What the switches add up to, as the Observe page and /api/me name it
    public static class SiteModes
    {
        public const string FreeOnly = "FreeOnly";
        public const string PlusAvailable = "PlusAvailable";
        public const string ProAvailable = "ProAvailable";
        public const string PlusAndProAvailable = "PlusAndProAvailable";

        public static string For(bool plusOn, bool proOn) => (plusOn, proOn) switch
        {
            (true, true) => PlusAndProAvailable,
            (true, false) => PlusAvailable,
            (false, true) => ProAvailable,
            _ => FreeOnly
        };
    }
}
