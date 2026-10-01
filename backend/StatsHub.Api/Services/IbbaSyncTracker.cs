using System.Collections.Concurrent;

namespace StatsHub.Api.Services
{
    // Which IBBA links are still loading their games and standings in the
    // background (see IbbaService) - so the link status can say "loading
    // games", and a second sync of the same player doesn't run alongside the
    // first. One asked for meanwhile (e.g. a team linked in the pop-up while
    // the first load is still going) runs again right after it, so nothing
    // it would have picked up is missed.
    public class IbbaSyncTracker
    {
        // Running links -> whether another run was asked for meanwhile.
        private readonly ConcurrentDictionary<Guid, bool> _running = new();

        // True when the caller should start the load; false when one is
        // running - then it's queued to run again once that finishes.
        public bool TryStart(Guid linkId)
        {
            if (_running.TryAdd(linkId, false)) return true;
            _running[linkId] = true;
            return false;
        }

        // True when another run was asked for while this one ran.
        public bool Finish(Guid linkId) => _running.TryRemove(linkId, out var again) && again;

        public bool IsRunning(Guid linkId) => _running.ContainsKey(linkId);
    }
}
