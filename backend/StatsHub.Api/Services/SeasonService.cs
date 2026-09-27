using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface ISeasonService
    {
        Task<List<SeasonDto>> GetSeasonsByUserAsync(int userId);
        Task<SeasonDto?> GetSeasonByIdAsync(int id, int requestingUserId);
        Task<SeasonDto> CreateSeasonAsync(int userId, CreateSeasonDto dto);
        Task<SeasonDto?> UpdateSeasonAsync(int id, UpdateSeasonDto dto, int requestingUserId);
        Task<bool> DeleteSeasonAsync(int id, int requestingUserId);
        Task<bool> UserOwnsSeasonAsync(int seasonId, int userId);
        Task<Season> GetOrCreateCurrentSeasonAsync(int userId);
    }

    public class SeasonService : ISeasonService
    {
        // The app only tracks the current season for now.
        private const string CurrentSeasonName = "2026-2027 Season";
        private const int CurrentSeasonYear = 2026;

        private readonly AppDbContext _context;

        public SeasonService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<bool> UserOwnsSeasonAsync(int seasonId, int userId)
        {
            return await _context.Seasons.AnyAsync(s => s.Id == seasonId && s.UserId == userId);
        }

        // Every parent has exactly one active season right now; it's provisioned
        // automatically the first time it's needed instead of being user-managed.
        public async Task<Season> GetOrCreateCurrentSeasonAsync(int userId)
        {
            var season = await _context.Seasons.FirstOrDefaultAsync(s => s.UserId == userId && s.Year == CurrentSeasonYear);
            if (season != null) return season;

            season = new Season
            {
                UserId = userId,
                Name = CurrentSeasonName,
                Sport = "Basketball",
                Year = CurrentSeasonYear,
                StartDate = new DateTime(2026, 9, 1),
                EndDate = new DateTime(2027, 6, 30),
                CreatedAt = DateTime.UtcNow
            };
            _context.Seasons.Add(season);
            await _context.SaveChangesAsync();
            return season;
        }

        public async Task<List<SeasonDto>> GetSeasonsByUserAsync(int userId)
        {
            var seasons = await _context.Seasons
                .Where(s => s.UserId == userId)
                .Include(s => s.Teams)
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();

            var result = new List<SeasonDto>();
            foreach (var season in seasons) result.Add(await MapToDtoAsync(season));
            return result;
        }

        public async Task<SeasonDto?> GetSeasonByIdAsync(int id, int requestingUserId)
        {
            var season = await _context.Seasons
                .Include(s => s.Teams)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (season == null || season.UserId != requestingUserId) return null;

            return await MapToDtoAsync(season);
        }

        public async Task<SeasonDto> CreateSeasonAsync(int userId, CreateSeasonDto dto)
        {
            var season = new Season
            {
                UserId = userId,
                Name = dto.Name,
                Sport = dto.Sport,
                Year = dto.Year,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                CreatedAt = DateTime.UtcNow
            };

            _context.Seasons.Add(season);
            await _context.SaveChangesAsync();

            return await MapToDtoAsync(season);
        }

        public async Task<SeasonDto?> UpdateSeasonAsync(int id, UpdateSeasonDto dto, int requestingUserId)
        {
            var season = await _context.Seasons.FindAsync(id);
            if (season == null || season.UserId != requestingUserId) return null;

            if (!string.IsNullOrEmpty(dto.Name)) season.Name = dto.Name;
            if (dto.Year.HasValue) season.Year = dto.Year.Value;
            if (dto.StartDate.HasValue) season.StartDate = dto.StartDate.Value;
            if (dto.EndDate.HasValue) season.EndDate = dto.EndDate;

            season.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return await GetSeasonByIdAsync(id, requestingUserId);
        }

        public async Task<bool> DeleteSeasonAsync(int id, int requestingUserId)
        {
            var season = await _context.Seasons.FindAsync(id);
            if (season == null || season.UserId != requestingUserId) return false;

            _context.Seasons.Remove(season);
            await _context.SaveChangesAsync();
            return true;
        }

        // No Games nav on Team to sum (HomeTeamId/AwayTeamId aren't a formal FK
        // to it - see Game.cs), so this matches explicitly: a manual game by
        // literal team id, an IBBA-synced one by a team's linked IbbaTeamId on
        // either side of the fixture. One query for all of the season's teams
        // (not one per team - every query is a full network round trip to the
        // database in production), and a game shared by two of the season's
        // teams counts once.
        private async Task<SeasonDto> MapToDtoAsync(Season season)
        {
            var teams = season.Teams ?? new List<Team>();
            var teamIds = teams.Select(t => t.Id).ToList();
            var ibbaTeamIds = teams.Where(t => t.IbbaTeamId != null).Select(t => t.IbbaTeamId!.Value).ToList();

            var totalGames = teamIds.Count == 0 ? 0 : await _context.Games.CountAsync(g =>
                (g.IbbaGameCode == null && ((g.HomeTeamId != null && teamIds.Contains(g.HomeTeamId.Value)) || (g.AwayTeamId != null && teamIds.Contains(g.AwayTeamId.Value)))) ||
                (g.IbbaGameCode != null && ((g.HomeTeamId != null && ibbaTeamIds.Contains(g.HomeTeamId.Value)) || (g.AwayTeamId != null && ibbaTeamIds.Contains(g.AwayTeamId.Value)))));

            return new SeasonDto
            {
                Id = season.Id,
                Name = season.Name,
                Sport = season.Sport,
                Year = season.Year,
                StartDate = season.StartDate,
                EndDate = season.EndDate,
                TotalGames = totalGames
            };
        }
    }
}
