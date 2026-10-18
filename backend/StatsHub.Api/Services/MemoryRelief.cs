using System.Runtime;

namespace StatsHub.Api.Services
{
    // IBBA work (syncs, crest and opponent backfills) downloads pages and
    // spreadsheets that are garbage the moment it's done - but .NET keeps the
    // memory they took and reuses it rather than handing it back, so each
    // nightly sync left the server a little bigger (and the host bills RAM).
    // A few seconds after a background job finishes - once it's let go of
    // everything - one full collection compacts the heap and returns the
    // free memory to the system. Jobs finishing close together share one.
    public static class MemoryRelief
    {
        private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);
        private static int _scheduled;

        public static void ReleaseSoon()
        {
            if (Interlocked.Exchange(ref _scheduled, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(Delay);
                Interlocked.Exchange(ref _scheduled, 0);
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            });
        }
    }
}
