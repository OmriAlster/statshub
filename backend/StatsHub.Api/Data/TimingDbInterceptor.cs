using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StatsHub.Api.Services;

namespace StatsHub.Api.Data
{
    // Records every database command on the current RequestTimings as
    // "db-command" - so a request's Server-Timing shows how many round trips
    // it made (each costs ~150ms against production's Postgres, ~0 locally).
    //
    // Diagnostics:SimulatedDbLatencyMs (development only, off by default) adds
    // that much delay before every command - to feel production's network
    // round trip against the local SQLite file and measure flows realistically.
    public class TimingDbInterceptor : DbCommandInterceptor
    {
        private readonly int _simulatedLatencyMs;

        public TimingDbInterceptor(int simulatedLatencyMs = 0) => _simulatedLatencyMs = simulatedLatencyMs;

        private async ValueTask Delay(CancellationToken cancellationToken)
        {
            if (_simulatedLatencyMs > 0) await Task.Delay(_simulatedLatencyMs, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { await Delay(cancellationToken); return result; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { await Delay(cancellationToken); return result; }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { await Delay(cancellationToken); return result; }

        private void Record(CommandExecutedEventData data) =>
            RequestTimings.Current?.AddDbCommand(data.Duration.TotalMilliseconds + _simulatedLatencyMs);

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        { Record(eventData); return base.ReaderExecutedAsync(command, eventData, result, cancellationToken); }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        { Record(eventData); return base.ReaderExecuted(command, eventData, result); }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Record(eventData); return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken); }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        { Record(eventData); return base.NonQueryExecuted(command, eventData, result); }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        { Record(eventData); return base.ScalarExecutedAsync(command, eventData, result, cancellationToken); }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        { Record(eventData); return base.ScalarExecuted(command, eventData, result); }
    }
}
