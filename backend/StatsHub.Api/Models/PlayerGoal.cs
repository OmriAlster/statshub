namespace StatsHub.Api.Models
{
    // A target the family sets for the season ("10 points a game", "70% from
    // the line") - its progress is worked out from the player's games.
    public class PlayerGoal
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid PlayerId { get; set; }

        // One of PlayerGoal.Stats.
        public string Stat { get; set; } = string.Empty;
        public double Target { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Player Player { get; set; } = null!;

        // Per game for the averages, percent for the shooting.
        public static readonly IReadOnlyDictionary<string, double> Stats = new Dictionary<string, double>
        {
            ["ppg"] = 60,
            ["rpg"] = 30,
            ["apg"] = 30,
            ["spg"] = 20,
            ["fgPct"] = 100,
            ["threePct"] = 100,
            ["ftPct"] = 100,
        };
    }
}
