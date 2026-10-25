using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Models;

namespace StatsHub.Api.Data
{
    // Who may see or change what - in one place, so every service applies the
    // same rule (it used to be repeated in each, where one copy drifting from
    // the others is an access bug). The expressions run inside database
    // queries; the methods are the usual yes/no checks.
    public static class AccessRules
    {
        // A team is managed by its season's owner, or by any parent of a
        // player on its roster - so a second parent invited onto their kid's
        // team can manage it too.
        public static Expression<Func<Team, bool>> TeamManagedBy(Guid userId) =>
            t => t.Season.UserId == userId || t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == userId));

        // A player is seen by each of their parents, and by the player
        // themself once their own account is linked.
        public static Expression<Func<Player, bool>> PlayerVisibleTo(Guid userId) =>
            p => p.LinkedUserId == userId || p.Parents.Any(pp => pp.UserId == userId);

        // The same, for a player already loaded with their Parents.
        public static bool IsVisibleTo(this Player player, Guid userId) =>
            player.LinkedUserId == userId || player.Parents.Any(pp => pp.UserId == userId);

        public static Task<bool> CanManageTeamAsync(this AppDbContext db, Guid teamId, Guid userId) =>
            db.Teams.Where(t => t.Id == teamId).AnyAsync(TeamManagedBy(userId));

        public static Task<bool> CanReadPlayerAsync(this AppDbContext db, Guid playerId, Guid userId) =>
            db.Players.Where(p => p.Id == playerId).AnyAsync(PlayerVisibleTo(userId));

        // Any of the player's parents (not just the one who created them) can
        // record stats and edit them; the player themself only reads.
        public static Task<bool> CanWritePlayerAsync(this AppDbContext db, Guid playerId, Guid userId) =>
            db.PlayerParents.AnyAsync(pp => pp.PlayerId == playerId && pp.UserId == userId);

        // A manual game through its own app team; an IBBA game through any app
        // team linked to either side (Home/AwayTeamId are IbbaTeam ids then -
        // see Game.cs) - in both cases a team this user manages.
        public static Task<bool> CanAccessGameAsync(this AppDbContext db, Game game, Guid userId) =>
            TeamsForGame(db, game).AnyAsync(TeamManagedBy(userId));

        // The app teams a game belongs to (see CanAccessGameAsync).
        public static IQueryable<Team> TeamsForGame(this AppDbContext db, Game game)
        {
            if (game.IbbaGameCode == null)
            {
                var teamId = game.HomeTeamId ?? game.AwayTeamId;
                return db.Teams.Where(t => teamId.HasValue && t.Id == teamId.Value);
            }
            var sideIds = new[] { game.HomeTeamId, game.AwayTeamId }.OfType<Guid>().ToList();
            return db.Teams.Where(t => t.IbbaTeamId != null && sideIds.Contains(t.IbbaTeamId.Value));
        }
    }
}
