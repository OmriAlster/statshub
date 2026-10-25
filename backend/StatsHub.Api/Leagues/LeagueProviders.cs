namespace StatsHub.Api.Leagues;

/// <summary>Every league registered in Program.cs, by key and by player address.</summary>
public class LeagueProviders
{
    private readonly Dictionary<string, ILeagueProvider> _byKey;

    public LeagueProviders(IEnumerable<ILeagueProvider> providers) =>
        _byKey = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ILeagueProvider> All => _byKey.Values;

    /// <summary>The provider a link, team or game was synced by.</summary>
    public ILeagueProvider Get(string key) =>
        _byKey.TryGetValue(key, out var provider) ? provider : throw new InvalidOperationException($"No league provider \"{key}\" is registered.");

    /// <summary>The league a pasted player address belongs to - or a clear "not supported".</summary>
    public ILeagueProvider ForPlayerUrl(string url) =>
        _byKey.Values.FirstOrDefault(p => p.IsPlayerUrl(url.Trim()))
        ?? throw new InvalidOperationException(
            $"That isn't a player page from a supported league ({string.Join(", ", _byKey.Values.Select(p => p.Name))}). Paste the address of the player's own page.");
}
