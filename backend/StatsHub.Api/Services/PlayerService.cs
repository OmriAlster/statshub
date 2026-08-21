using Microsoft.EntityFrameworkCore;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IPlayerService
    {
        Task<List<PlayerDto>> GetPlayersOwnedByAsync(int userId);
        Task<PlayerDto?> GetLinkedPlayerAsync(int linkedUserId);
        Task<PlayerDto?> GetPlayerByIdAsync(int id, int requestingUserId);
        Task<PlayerDto> CreatePlayerAsync(int userId, CreatePlayerDto dto);
        Task<PlayerDto?> UpdatePlayerAsync(int id, UpdatePlayerDto dto, int requestingUserId);
        Task<bool> DeletePlayerAsync(int id, int requestingUserId);
        Task<PlayerInviteDto?> CreatePlayerInviteAsync(int playerId, int requestingUserId, string? password);
        Task<PlayerDto?> ClaimPlayerInviteAsync(string inviteCode, int claimingUserId, string? password);
        Task<ParentInviteDto?> CreateParentInviteAsync(int playerId, int requestingUserId, string? password);
        Task<PlayerDto?> ClaimParentInviteAsync(string inviteCode, int claimingUserId, string? password);
        Task<bool> CanAccessPlayerAsync(int playerId, int userId);
    }

    public class PlayerService : IPlayerService
    {
        private readonly AppDbContext _context;

        public PlayerService(AppDbContext context)
        {
            _context = context;
        }

        private IQueryable<Player> PlayersWithTeams() =>
            _context.Players
                .Include(p => p.PlayerTeams).ThenInclude(pt => pt.Team)
                .Include(p => p.Parents).ThenInclude(pp => pp.User);

        private Task<bool> IsParentAsync(int playerId, int userId) =>
            _context.PlayerParents.AnyAsync(pp => pp.PlayerId == playerId && pp.UserId == userId);

        public async Task<List<PlayerDto>> GetPlayersOwnedByAsync(int userId)
        {
            var players = await PlayersWithTeams()
                .Where(p => p.Parents.Any(pp => pp.UserId == userId))
                .ToListAsync();
            return players.Select(MapToDto).ToList();
        }

        public async Task<PlayerDto?> GetLinkedPlayerAsync(int linkedUserId)
        {
            var player = await PlayersWithTeams().FirstOrDefaultAsync(p => p.LinkedUserId == linkedUserId);
            return player == null ? null : MapToDto(player);
        }

        public async Task<bool> CanAccessPlayerAsync(int playerId, int userId)
        {
            return await _context.Players.AnyAsync(p =>
                p.Id == playerId && (p.LinkedUserId == userId || p.Parents.Any(pp => pp.UserId == userId)));
        }

        public async Task<PlayerDto?> GetPlayerByIdAsync(int id, int requestingUserId)
        {
            var player = await PlayersWithTeams().FirstOrDefaultAsync(p => p.Id == id);
            if (player == null) return null;
            if (player.LinkedUserId != requestingUserId && !player.Parents.Any(pp => pp.UserId == requestingUserId)) return null;

            return MapToDto(player);
        }

        public async Task<PlayerDto> CreatePlayerAsync(int userId, CreatePlayerDto dto)
        {
            var player = new Player
            {
                UserId = userId,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                JerseyNumber = dto.JerseyNumber,
                Position = dto.Position,
                Height = dto.Height,
                Weight = dto.Weight,
                DateOfBirth = dto.DateOfBirth,
                ProfilePictureUrl = dto.ProfilePictureUrl,
                CreatedAt = DateTime.UtcNow
            };

            _context.Players.Add(player);
            await _context.SaveChangesAsync();

            _context.PlayerParents.Add(new PlayerParent { PlayerId = player.Id, UserId = userId, CreatedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();

            return await GetPlayerByIdAsync(player.Id, userId) ?? MapToDto(player);
        }

        public async Task<PlayerDto?> UpdatePlayerAsync(int id, UpdatePlayerDto dto, int requestingUserId)
        {
            var player = await PlayersWithTeams().FirstOrDefaultAsync(p => p.Id == id);
            if (player == null || !player.Parents.Any(pp => pp.UserId == requestingUserId)) return null;

            if (!string.IsNullOrEmpty(dto.FirstName)) player.FirstName = dto.FirstName;
            if (!string.IsNullOrEmpty(dto.LastName)) player.LastName = dto.LastName;
            if (dto.JerseyNumber.HasValue) player.JerseyNumber = dto.JerseyNumber.Value;
            if (!string.IsNullOrEmpty(dto.Position)) player.Position = dto.Position;
            if (dto.Height.HasValue) player.Height = dto.Height;
            if (dto.Weight.HasValue) player.Weight = dto.Weight;
            if (dto.DateOfBirth.HasValue) player.DateOfBirth = dto.DateOfBirth.Value;
            if (!string.IsNullOrEmpty(dto.ProfilePictureUrl)) player.ProfilePictureUrl = dto.ProfilePictureUrl;

            player.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return MapToDto(player);
        }

        public async Task<bool> DeletePlayerAsync(int id, int requestingUserId)
        {
            var player = await _context.Players.FindAsync(id);
            if (player == null || !await IsParentAsync(id, requestingUserId)) return false;

            _context.Players.Remove(player);
            await _context.SaveChangesAsync();
            return true;
        }

        // Generates a short-lived code the parent can send to their player so the
        // player can sign in with their own Google account and see their own stats.
        public async Task<PlayerInviteDto?> CreatePlayerInviteAsync(int playerId, int requestingUserId, string? password)
        {
            var player = await _context.Players.FindAsync(playerId);
            if (player == null || !await IsParentAsync(playerId, requestingUserId)) return null;

            string code;
            do
            {
                code = InviteCodeGenerator.Generate();
            } while (await _context.Players.AnyAsync(p => p.InviteCode == code));

            player.InviteCode = code;
            player.InviteCodeExpiresAt = DateTime.UtcNow.AddDays(7);
            player.InvitePasswordHash = string.IsNullOrWhiteSpace(password) ? null : PasswordHasher.Hash(password);
            player.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new PlayerInviteDto { InviteCode = code, ExpiresAt = player.InviteCodeExpiresAt.Value };
        }

        public async Task<PlayerDto?> ClaimPlayerInviteAsync(string inviteCode, int claimingUserId, string? password)
        {
            var player = await PlayersWithTeams().FirstOrDefaultAsync(p => p.InviteCode == inviteCode);
            if (player == null) return null;
            if (player.InviteCodeExpiresAt.HasValue && player.InviteCodeExpiresAt.Value < DateTime.UtcNow) return null;
            if (!string.IsNullOrEmpty(player.InvitePasswordHash) && !PasswordHasher.Verify(password ?? string.Empty, player.InvitePasswordHash)) return null;

            player.LinkedUserId = claimingUserId;
            player.InviteCode = null;
            player.InviteCodeExpiresAt = null;
            player.InvitePasswordHash = null;
            player.UpdatedAt = DateTime.UtcNow;

            var playerUser = await _context.Users.FindAsync(claimingUserId);
            if (playerUser != null)
            {
                playerUser.Role = "Player";
                playerUser.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return MapToDto(player);
        }

        // Generates a short-lived code a second parent/guardian can use to get
        // full access to the same player (e.g. mom invites dad).
        public async Task<ParentInviteDto?> CreateParentInviteAsync(int playerId, int requestingUserId, string? password)
        {
            var player = await _context.Players.FindAsync(playerId);
            if (player == null || !await IsParentAsync(playerId, requestingUserId)) return null;

            string code;
            do
            {
                code = InviteCodeGenerator.Generate();
            } while (await _context.Players.AnyAsync(p => p.ParentInviteCode == code));

            player.ParentInviteCode = code;
            player.ParentInviteCodeExpiresAt = DateTime.UtcNow.AddDays(7);
            player.ParentInvitePasswordHash = string.IsNullOrWhiteSpace(password) ? null : PasswordHasher.Hash(password);
            player.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new ParentInviteDto { InviteCode = code, ExpiresAt = player.ParentInviteCodeExpiresAt.Value };
        }

        public async Task<PlayerDto?> ClaimParentInviteAsync(string inviteCode, int claimingUserId, string? password)
        {
            var player = await PlayersWithTeams().FirstOrDefaultAsync(p => p.ParentInviteCode == inviteCode);
            if (player == null) return null;
            if (player.ParentInviteCodeExpiresAt.HasValue && player.ParentInviteCodeExpiresAt.Value < DateTime.UtcNow) return null;
            if (!string.IsNullOrEmpty(player.ParentInvitePasswordHash) && !PasswordHasher.Verify(password ?? string.Empty, player.ParentInvitePasswordHash)) return null;

            if (!player.Parents.Any(pp => pp.UserId == claimingUserId))
            {
                _context.PlayerParents.Add(new PlayerParent { PlayerId = player.Id, UserId = claimingUserId, CreatedAt = DateTime.UtcNow });
            }

            player.ParentInviteCode = null;
            player.ParentInviteCodeExpiresAt = null;
            player.ParentInvitePasswordHash = null;
            player.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return await GetPlayerByIdAsync(player.Id, claimingUserId);
        }

        private static PlayerDto MapToDto(Player p) => new PlayerDto
        {
            Id = p.Id,
            FirstName = p.FirstName,
            LastName = p.LastName,
            JerseyNumber = p.JerseyNumber,
            Position = p.Position,
            Height = p.Height,
            Weight = p.Weight,
            DateOfBirth = p.DateOfBirth,
            ProfilePictureUrl = p.ProfilePictureUrl,
            Teams = p.PlayerTeams.Select(pt => new TeamDto { Id = pt.Team.Id, Name = pt.Team.Name, JerseyNumber = pt.JerseyNumber }).ToList(),
            Parents = p.Parents.Select(pp => new ParentDto { UserId = pp.UserId, FirstName = pp.User.FirstName, LastName = pp.User.LastName }).ToList()
        };
    }
}
