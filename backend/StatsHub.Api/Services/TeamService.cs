using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface ITeamService
    {
        Task<List<TeamDto>> GetMyTeamsAsync(int userId);
        Task<TeamDto> CreateTeamAsync(int userId, CreateTeamDto dto);
        Task<bool> DeleteTeamAsync(int teamId, int userId);
        Task<bool> AddPlayerToTeamAsync(int teamId, int playerId, int userId);
        Task<bool> RemovePlayerFromTeamAsync(int teamId, int playerId, int userId);
    }

    public class TeamService : ITeamService
    {
        private readonly AppDbContext _context;
        private readonly ISeasonService _seasonService;

        public TeamService(AppDbContext context, ISeasonService seasonService)
        {
            _context = context;
            _seasonService = seasonService;
        }

        public async Task<List<TeamDto>> GetMyTeamsAsync(int userId)
        {
            var season = await _seasonService.GetOrCreateCurrentSeasonAsync(userId);

            // Teams from the user's own season, plus any team a player they
            // co-parent is already rostered on (covers a second parent whose
            // kid's team was created under the other parent's season).
            var teams = await _context.Teams
                .Where(t =>
                    t.SeasonId == season.Id ||
                    t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == userId)))
                .Distinct()
                .OrderBy(t => t.Name)
                .ToListAsync();

            return teams.Select(MapToDto).ToList();
        }

        public async Task<TeamDto> CreateTeamAsync(int userId, CreateTeamDto dto)
        {
            var season = await _seasonService.GetOrCreateCurrentSeasonAsync(userId);
            var team = new Team
            {
                SeasonId = season.Id,
                Name = dto.Name.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.Teams.Add(team);
            await _context.SaveChangesAsync();

            return MapToDto(team);
        }

        public async Task<bool> DeleteTeamAsync(int teamId, int userId)
        {
            var team = await _context.Teams.Include(t => t.Season).FirstOrDefaultAsync(t => t.Id == teamId);
            if (team == null || team.Season.UserId != userId) return false;

            _context.Teams.Remove(team);
            await _context.SaveChangesAsync();
            return true;
        }

        // A parent can add their player onto a team they own the season for, or
        // onto a team another one of their linked players is already rostered on
        // (covers a second parent who doesn't own the season).
        private async Task<bool> CanManageTeamAsync(int teamId, int userId)
        {
            return await _context.Teams.AnyAsync(t =>
                t.Id == teamId && (
                    t.Season.UserId == userId ||
                    t.PlayerTeams.Any(pt => pt.Player.Parents.Any(pp => pp.UserId == userId))
                ));
        }

        public async Task<bool> AddPlayerToTeamAsync(int teamId, int playerId, int userId)
        {
            if (!await CanManageTeamAsync(teamId, userId)) return false;

            var player = await _context.Players.FindAsync(playerId);
            if (player == null || !await _context.PlayerParents.AnyAsync(pp => pp.PlayerId == playerId && pp.UserId == userId)) return false;

            var existing = await _context.PlayerTeams.FirstOrDefaultAsync(pt => pt.TeamId == teamId && pt.PlayerId == playerId);
            if (existing != null) return true;

            _context.PlayerTeams.Add(new PlayerTeam
            {
                TeamId = teamId,
                PlayerId = playerId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RemovePlayerFromTeamAsync(int teamId, int playerId, int userId)
        {
            if (!await CanManageTeamAsync(teamId, userId)) return false;

            var membership = await _context.PlayerTeams.FirstOrDefaultAsync(pt => pt.TeamId == teamId && pt.PlayerId == playerId);
            if (membership == null) return false;

            _context.PlayerTeams.Remove(membership);
            await _context.SaveChangesAsync();
            return true;
        }

        private static TeamDto MapToDto(Team team) => new TeamDto
        {
            Id = team.Id,
            Name = team.Name
        };
    }
}
