using System.Collections.Concurrent;

namespace MonteCarloSimulation.Web
{
    // A per-address request limit over a rolling window (5 an hour for the passphrase forms). Unlike ASP.NET Core's
    // fixed-window limiter, which on refusal can only say "a whole window", it knows exactly when the next request is
    // allowed: one window after the oldest request still counted. Slots free up one at a time as requests age out.
    //
    // Kept in memory, so counts reset when the app restarts - as the built-in limiter's did. One instance per limited
    // endpoint, registered as a singleton.
    public sealed class RequestQuota(int limit, TimeSpan window, TimeProvider time)
    {
        // How often (in acquires) to drop addresses with nothing left in the window, so the map can't grow forever
        private const int SweepEvery = 1_000;

        private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _requests = new();
        private int _acquires;

        public int Limit => limit;

        public QuotaDecision TryAcquire(string address)
        {
            if (Interlocked.Increment(ref _acquires) % SweepEvery == 0) Sweep();

            var now = time.GetUtcNow();
            var requests = _requests.GetOrAdd(address, _ => new Queue<DateTimeOffset>());
            lock (requests)
            {
                DropExpired(requests, now);
                if (requests.Count >= limit)
                    return new QuotaDecision(false, 0, requests.Peek() + window, requests.Peek() + window - now);

                requests.Enqueue(now);
                return new QuotaDecision(true, limit - requests.Count, requests.Peek() + window, TimeSpan.Zero);
            }
        }

        private void DropExpired(Queue<DateTimeOffset> requests, DateTimeOffset now)
        {
            while (requests.Count > 0 && requests.Peek() + window <= now) requests.Dequeue();
        }

        // A request racing a sweep can land in a queue that was just removed and go uncounted once; for a
        // demo site's passphrase forms that's an acceptable price for not holding a global lock.
        private void Sweep()
        {
            var now = time.GetUtcNow();
            foreach (var (address, requests) in _requests)
            {
                lock (requests)
                {
                    DropExpired(requests, now);
                    if (requests.Count == 0) _requests.TryRemove(address, out _);
                }
            }
        }
    }

    // Allowed: whether the request may proceed. Remaining: how many more are allowed in the window after this one.
    // NextSlotAt: when the oldest counted request ages out and frees a slot - for a refused request, the earliest
    // retry. RetryAfter: how long until then (zero when allowed).
    public readonly record struct QuotaDecision(bool Allowed, int Remaining, DateTimeOffset NextSlotAt, TimeSpan RetryAfter);
}
