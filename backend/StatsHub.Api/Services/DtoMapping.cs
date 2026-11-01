using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    // Games, box scores and shots as the API returns them - one mapping for
    // the signed-in views and the public share view alike.
    public static class DtoMapping
    {
        // The IBBA teams on either side of these games (their Home/AwayTeamId
        // are IbbaTeam ids - see Game.cs), for the opponent's name and crest.
        public static async Task<Dictionary<Guid, IbbaTeam>> LoadIbbaTeamsAsync(this AppDbContext db, IEnumerable<Game> games)
        {
            var ids = games.Where(g => g.IbbaGameCode != null)
                .SelectMany(g => new[] { g.HomeTeamId, g.AwayTeamId })
                .OfType<Guid>().Distinct().ToList();
            return ids.Count > 0
                ? await db.IbbaTeams.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id)
                : new Dictionary<Guid, IbbaTeam>();
        }

        // A game as seen by viewTeam. Everything relative to "us" (opponent
        // name/logo, our score vs. theirs, home/away) is derived from the
        // game's objective Home/Away ids compared against viewTeam's own -
        // nothing on the Game row itself privileges one side.
        public static GameDto ToGameDto(Game game, Team? viewTeam, IReadOnlyDictionary<Guid, IbbaTeam> ibbaTeams, bool canRecordLive)
        {
            var isIbba = game.IbbaGameCode != null;
            string opponentName;
            string? opponentLogoUrl;
            int? teamScore, opponentScore;
            bool? isHomeGame;

            if (isIbba)
            {
                bool? viewerIsHome = viewTeam?.IbbaTeamId is Guid viewIbbaId
                    ? (game.HomeTeamId == viewIbbaId ? true : game.AwayTeamId == viewIbbaId ? false : (bool?)null)
                    : null;

                // Defaults to the "home" framing both when the viewer really is
                // home, and when their side couldn't be determined at all (no
                // better guess available for that rare fallback).
                var opponentTeamId = viewerIsHome == false ? game.HomeTeamId : game.AwayTeamId;
                var opponent = opponentTeamId.HasValue && ibbaTeams.TryGetValue(opponentTeamId.Value, out var opp) ? opp : null;

                opponentName = opponent?.Name ?? string.Empty;
                opponentLogoUrl = opponent?.LogoUrl;
                teamScore = viewerIsHome == false ? game.AwayScore : game.HomeScore;
                opponentScore = viewerIsHome == false ? game.HomeScore : game.AwayScore;
                isHomeGame = viewerIsHome;
            }
            else
            {
                opponentName = game.OpponentName;
                opponentLogoUrl = null; // manual games never had opponent logos
                teamScore = game.TeamScore;
                opponentScore = game.OpponentScore;
                isHomeGame = viewTeam != null
                    ? (game.HomeTeamId == viewTeam.Id ? true : game.AwayTeamId == viewTeam.Id ? false : (bool?)null)
                    : null;
            }

            return new GameDto
            {
                Id = game.Id,
                TeamId = viewTeam?.Id ?? Guid.Empty,
                TeamName = viewTeam?.Name ?? string.Empty,
                TeamLogoUrl = viewTeam?.IbbaTeam?.LogoUrl,
                GameType = game.GameType,
                OpponentName = opponentName,
                OpponentLogoUrl = opponentLogoUrl,
                GameDate = game.GameDate,
                Location = game.Location,
                Status = game.Status,
                TeamScore = teamScore,
                OpponentScore = opponentScore,
                Notes = game.Notes,
                IsHomeGame = isHomeGame,
                IsFromIbba = isIbba,
                League = game.Provider,
                CanRecordLive = canRecordLive,
                PlayerStats = game.GameStats.Select(gs =>
                {
                    var dto = gs.ToDto();
                    dto.Scoresheet = game.ScoresheetLines.FirstOrDefault(l => l.PlayerId == gs.PlayerId)?.ToDto();
                    return dto;
                }).ToList(),
                Scoresheets = game.ScoresheetLines.Select(ToDto).ToList()
            };
        }

        public static ScoresheetLineDto ToDto(this ScoresheetLine line) => new()
        {
            PlayerId = line.PlayerId,
            Number = line.Number,
            Played = line.Played,
            Starter = line.Starter,
            Points = line.Points,
            FreeThrows = line.FreeThrows,
            TwoPointers = line.TwoPointers,
            ThreePointers = line.ThreePointers,
            Fouls = line.Fouls,
            SheetValid = line.SheetValid,
            SheetErrors = line.SheetErrors,
        };

        // A box score, with its shots when they were loaded.
        public static GameStatsDto ToDto(this GameStats gs) => new()
        {
            Id = gs.Id,
            GameId = gs.GameId,
            PlayerId = gs.PlayerId,
            PlayerName = $"{gs.Player.FirstName} {gs.Player.LastName}",
            Source = gs.Source,
            FieldGoalsMade = gs.FieldGoalsMade,
            FieldGoalsAttempted = gs.FieldGoalsAttempted,
            FieldGoalPercentage = gs.FieldGoalPercentage,
            ThreePointersMade = gs.ThreePointersMade,
            ThreePointersAttempted = gs.ThreePointersAttempted,
            ThreePointPercentage = gs.ThreePointPercentage,
            FreeThrowsMade = gs.FreeThrowsMade,
            FreeThrowsAttempted = gs.FreeThrowsAttempted,
            FreeThrowPercentage = gs.FreeThrowPercentage,
            OffensiveRebounds = gs.OffensiveRebounds,
            DefensiveRebounds = gs.DefensiveRebounds,
            TotalRebounds = gs.TotalRebounds,
            Assists = gs.Assists,
            Steals = gs.Steals,
            Blocks = gs.Blocks,
            Turnovers = gs.Turnovers,
            Fouls = gs.Fouls,
            MinutesPlayed = gs.MinutesPlayed,
            OnCourt = gs.OnCourt,
            TotalPoints = gs.TotalPoints,
            Shots = gs.Shots.Select(s => s.ToDto(gs)).ToList()
        };

        public static ShotDto ToDto(this Shot shot, GameStats gameStats) => new()
        {
            Id = shot.Id,
            GameStatsId = shot.GameStatsId,
            GameId = gameStats.GameId,
            PlayerId = gameStats.PlayerId,
            Quarter = shot.Quarter,
            X = shot.X,
            Y = shot.Y,
            Made = shot.Made,
            Value = shot.Value
        };
    }
}
