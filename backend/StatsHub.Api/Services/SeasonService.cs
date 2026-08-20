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
                .ThenInclude(t => t.Games)
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();

            return seasons.Select(MapToDto).ToList();
        }

        public async Task<SeasonDto?> GetSeasonByIdAsync(int id, int requestingUserId)
        {
            var season = await _context.Seasons
                .Include(s => s.Teams)
                .ThenInclude(t => t.Games)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (season == null || season.UserId != requestingUserId) return null;

            return MapToDto(season);
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

            return MapToDto(season);
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

        private static SeasonDto MapToDto(Season season) => new SeasonDto
        {
            Id = season.Id,
            Name = season.Name,
            Sport = season.Sport,
            Year = season.Year,
            StartDate = season.StartDate,
            EndDate = season.EndDate,
            TotalGames = season.Teams?.Sum(t => t.Games.Count) ?? 0
        };
    }
}
