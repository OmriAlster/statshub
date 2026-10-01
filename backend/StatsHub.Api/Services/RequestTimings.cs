using System.Collections.Concurrent;
using System.Diagnostics;

namespace StatsHub.Api.Services
{
    // How long the slow parts of one request took (each IBBA page fetched and
    // parsed, each database step of a sync), sent back in the Server-Timing
    // response header - visible per request in the browser's network tab -
    // and logged. Background jobs (crest/opponent backfill, notifications)
    // get their own and log it when they finish.
    //
    // "Current" follows the work through awaits (and into Task.WhenAll), so
    // code deep in the scrapers can add a measurement without being handed
    // anything; with nothing current, measuring is a no-op.
    public class RequestTimings
    {
        private static readonly AsyncLocal<RequestTimings?> CurrentHolder = new();
        public static RequestTimings? Current
        {
            get => CurrentHolder.Value;
            set => CurrentHolder.Value = value;
        }

        private readonly ConcurrentQueue<(string Name, double Ms)> _entries = new();
        private int _dbCommands;

        public void Add(string name, double ms) => _entries.Enqueue((name, ms));

        // Every database round trip (see TimingDbInterceptor); each measured
        // step also reports how many happened inside it, as "<step>.queries".
        public void AddDbCommand(double ms)
        {
            Interlocked.Increment(ref _dbCommands);
            Add("db-command", ms);
        }

        private void AddQueries(string name, int before)
        {
            var count = Volatile.Read(ref _dbCommands) - before;
            if (count > 0) Add($"{name}.queries", count);
        }

        public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> work)
        {
            var stopwatch = Stopwatch.StartNew();
            var queries = Volatile.Read(ref _dbCommands);
            try { return await work(); }
            finally { Add(name, stopwatch.Elapsed.TotalMilliseconds); AddQueries(name, queries); }
        }

        public async Task MeasureAsync(string name, Func<Task> work)
        {
            var stopwatch = Stopwatch.StartNew();
            var queries = Volatile.Read(ref _dbCommands);
            try { await work(); }
            finally { Add(name, stopwatch.Elapsed.TotalMilliseconds); AddQueries(name, queries); }
        }

        // `using var _ = RequestTimings.Time("parse-x");` - measures until the
        // end of the enclosing block, on whatever is current.
        public static IDisposable Time(string name) => new Scope(Current, name);

        // Runs a background job with timings of its own, logged at the end.
        public static async Task RunBackgroundAsync(string job, ILogger logger, Func<RequestTimings, Task> work)
        {
            var timings = new RequestTimings();
            Current = timings;
            var stopwatch = Stopwatch.StartNew();
            try { await work(timings); }
            finally
            {
                logger.LogInformation("Background {Job} took {Total:F0}ms: {Breakdown}", job, stopwatch.Elapsed.TotalMilliseconds, timings);
            }
        }

        public bool Any => !_entries.IsEmpty;

        // Same-named entries (e.g. three team pages) summed, with a count.
        public IEnumerable<(string Name, double TotalMs, int Count)> Summary() =>
            _entries.GroupBy(e => e.Name).Select(g => (g.Key, g.Sum(e => e.Ms), g.Count()));

        public string ToServerTimingHeader() => string.Join(", ",
            Summary().Select(s => $"{s.Name};dur={s.TotalMs:F0}" + (s.Count > 1 ? $";desc=\"{s.Count}x\"" : "")));

        public override string ToString() => string.Join(", ",
            Summary().Select(s => $"{s.Name} {s.TotalMs:F0}ms" + (s.Count > 1 ? $" ({s.Count}x)" : "")));

        private sealed class Scope : IDisposable
        {
            private readonly RequestTimings? _timings;
            private readonly string _name;
            private readonly long _start = Stopwatch.GetTimestamp();
            private readonly int _queries;

            public Scope(RequestTimings? timings, string name)
            {
                (_timings, _name) = (timings, name);
                _queries = timings == null ? 0 : Volatile.Read(ref timings._dbCommands);
            }

            public void Dispose()
            {
                if (_timings == null) return;
                _timings.Add(_name, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
                _timings.AddQueries(_name, _queries);
            }
        }
    }
}
