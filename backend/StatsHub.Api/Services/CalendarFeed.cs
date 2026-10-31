using System.Security.Cryptography;
using System.Text;
using StatsHub.Api.DTOs;

namespace StatsHub.Api.Services
{
    /// <summary>
    /// The players' games as a calendar a phone subscribes to (an .ics address):
    /// Google Calendar or the iPhone calendar re-read it every few hours, so a
    /// game that moves or is cancelled is updated there by itself. Calendar
    /// apps can't sign in, so the address carries its own key - the account id
    /// and a signature of it with the server's secret; nothing is stored.
    /// </summary>
    public static class CalendarFeed
    {
        private const int SignatureChars = 24;

        public static string TokenFor(Guid userId, string secret) => $"{userId:N}{Sign(userId, secret)}";

        public static Guid? UserFrom(string token, string secret)
        {
            if (token.Length != 32 + SignatureChars || !Guid.TryParseExact(token[..32], "N", out var userId)) return null;
            var expected = Encoding.ASCII.GetBytes(Sign(userId, secret));
            var given = Encoding.ASCII.GetBytes(token[32..]);
            return CryptographicOperations.FixedTimeEquals(expected, given) ? userId : null;
        }

        private static string Sign(Guid userId, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("calendar:" + secret));
            return Convert.ToHexString(hmac.ComputeHash(userId.ToByteArray())).ToLowerInvariant()[..SignatureChars];
        }

        public record Entry(GameDto Game, List<string> PlayerNames);

        // A game lasts about this long on the calendar (four quarters plus breaks).
        private static readonly TimeSpan GameLength = TimeSpan.FromMinutes(90);

        public static string Build(IEnumerable<Entry> entries, string appUrl, DateTime now)
        {
            var ics = new StringBuilder();
            void Line(string text) => Fold(ics, text);
            Line("BEGIN:VCALENDAR");
            Line("VERSION:2.0");
            Line("PRODID:-//StatsHub//Games//EN");
            Line("CALSCALE:GREGORIAN");
            Line("METHOD:PUBLISH");
            Line("X-WR-CALNAME:StatsHub games");
            Line("X-PUBLISHED-TTL:PT3H");
            Line("REFRESH-INTERVAL;VALUE=DURATION:PT3H");

            foreach (var (game, names) in entries.OrderBy(e => e.Game.GameDate))
            {
                var start = DateTime.SpecifyKind(game.GameDate, DateTimeKind.Utc);
                var who = string.Join(" & ", names);
                var cup = game.GameType == "League" ? "" : $" ({game.GameType})";
                var result = game.Status == "Completed" && game.TeamScore.HasValue && game.OpponentScore.HasValue
                    ? $" · {(game.TeamScore > game.OpponentScore ? "W" : "L")} {game.TeamScore}-{game.OpponentScore}"
                    : "";
                var side = game.IsHomeGame == true ? "Home" : game.IsHomeGame == false ? "Away" : null;

                Line("BEGIN:VEVENT");
                Line($"UID:{game.Id}@statshub");
                Line($"DTSTAMP:{Stamp(now)}");
                Line($"DTSTART:{Stamp(start)}");
                Line($"DTEND:{Stamp(start + GameLength)}");
                Line($"SUMMARY:{Escape($"🏀 {who} vs {game.OpponentName}{cup}{result}")}");
                if (!string.IsNullOrWhiteSpace(game.Location)) Line($"LOCATION:{Escape(game.Location)}");
                Line($"DESCRIPTION:{Escape(string.Join("\n", new[] { side, game.TeamName, $"{appUrl}/games/{game.Id}" }.Where(s => !string.IsNullOrEmpty(s))))}");
                Line($"URL:{appUrl}/games/{game.Id}");
                Line("STATUS:CONFIRMED");
                Line("END:VEVENT");
            }

            Line("END:VCALENDAR");
            return ics.ToString();
        }

        private static string Stamp(DateTime utc) => utc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");

        private static string Escape(string text) =>
            text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

        // Lines over 75 bytes continue on the next line after a space (RFC 5545),
        // split between characters, never inside one.
        private static void Fold(StringBuilder ics, string line)
        {
            var bytes = 0;
            var start = true;
            foreach (var rune in line.EnumerateRunes())
            {
                var size = rune.Utf8SequenceLength;
                if (bytes + size > (start ? 75 : 74))
                {
                    ics.Append("\r\n ");
                    bytes = 0;
                    start = false;
                }
                ics.Append(rune.ToString());
                bytes += size;
            }
            ics.Append("\r\n");
        }
    }
}
