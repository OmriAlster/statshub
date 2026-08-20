using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;

namespace StatsHub.Api.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto> LoginWithGoogleAsync(string idToken);
        Task<AuthResponseDto> DevLoginAsync(DevLoginDto dto);
        Task<AuthResponseDto> RegisterAsync(RegisterDto dto);
        Task<AuthResponseDto> LoginWithPasswordAsync(PasswordLoginDto dto);
        Task<UserDto?> GetCurrentUserAsync(int userId);
        string GenerateJwt(User user);
    }

    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<AuthResponseDto> LoginWithGoogleAsync(string idToken)
        {
            var clientId = _configuration["Google:ClientId"];
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                settings.Audience = new[] { clientId };
            }

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            }
            catch (InvalidJwtException)
            {
                throw new UnauthorizedAccessException("Invalid Google ID token");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.GoogleId == payload.Subject || u.Email == payload.Email);

            if (user == null)
            {
                user = new User
                {
                    Email = payload.Email,
                    FirstName = payload.GivenName ?? payload.Name ?? "Player",
                    LastName = payload.FamilyName ?? string.Empty,
                    GoogleId = payload.Subject,
                    ProfilePictureUrl = payload.Picture,
                    Role = "Parent",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
            }
            else
            {
                user.GoogleId ??= payload.Subject;
                user.ProfilePictureUrl = payload.Picture ?? user.ProfilePictureUrl;
                user.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            var token = GenerateJwt(user);
            var userDto = await BuildUserDtoAsync(user);
            return new AuthResponseDto { Token = token, User = userDto };
        }

        public async Task<AuthResponseDto> DevLoginAsync(DevLoginDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
            {
                user = new User
                {
                    Email = dto.Email,
                    FirstName = string.IsNullOrWhiteSpace(dto.FirstName) ? "Dev" : dto.FirstName,
                    LastName = dto.LastName,
                    Role = "Parent",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }

            var token = GenerateJwt(user);
            var userDto = await BuildUserDtoAsync(user);
            return new AuthResponseDto { Token = token, User = userDto };
        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
                throw new InvalidOperationException("Password must be at least 6 characters.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user != null && !string.IsNullOrEmpty(user.PasswordHash))
                throw new InvalidOperationException("An account with this email already exists. Please sign in instead.");

            if (user == null)
            {
                user = new User
                {
                    Email = dto.Email,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    Role = "Parent",
                    PasswordHash = PasswordHasher.Hash(dto.Password),
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
            }
            else
            {
                // an account created via Google previously - this just adds password login to it
                user.PasswordHash = PasswordHasher.Hash(dto.Password);
                if (!string.IsNullOrWhiteSpace(dto.FirstName)) user.FirstName = dto.FirstName;
                if (!string.IsNullOrWhiteSpace(dto.LastName)) user.LastName = dto.LastName;
                user.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            var token = GenerateJwt(user);
            var userDto = await BuildUserDtoAsync(user);
            return new AuthResponseDto { Token = token, User = userDto };
        }

        public async Task<AuthResponseDto> LoginWithPasswordAsync(PasswordLoginDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null || !PasswordHasher.Verify(dto.Password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid email or password.");

            var token = GenerateJwt(user);
            var userDto = await BuildUserDtoAsync(user);
            return new AuthResponseDto { Token = token, User = userDto };
        }

        public async Task<UserDto?> GetCurrentUserAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return null;
            return await BuildUserDtoAsync(user);
        }

        public string GenerateJwt(User user)
        {
            var keyString = _configuration["Jwt:Key"] ?? "dev-only-insecure-signing-key-change-me-please-32chars!";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role),
                new("name", $"{user.FirstName} {user.LastName}".Trim())
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"] ?? "StatsHub",
                audience: _configuration["Jwt:Audience"] ?? "StatsHubClient",
                claims: claims,
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task<UserDto> BuildUserDtoAsync(User user)
        {
            var dto = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                ProfilePictureUrl = user.ProfilePictureUrl,
                Role = user.Role
            };

            if (user.Role == "Player")
            {
                var linkedPlayer = await _context.Players
                    .Include(p => p.PlayerTeams).ThenInclude(pt => pt.Team)
                    .Include(p => p.Parents).ThenInclude(pp => pp.User)
                    .FirstOrDefaultAsync(p => p.LinkedUserId == user.Id);
                if (linkedPlayer != null)
                {
                    dto.LinkedPlayer = new PlayerDto
                    {
                        Id = linkedPlayer.Id,
                        FirstName = linkedPlayer.FirstName,
                        LastName = linkedPlayer.LastName,
                        JerseyNumber = linkedPlayer.JerseyNumber,
                        Position = linkedPlayer.Position,
                        Height = linkedPlayer.Height,
                        Weight = linkedPlayer.Weight,
                        DateOfBirth = linkedPlayer.DateOfBirth,
                        ProfilePictureUrl = linkedPlayer.ProfilePictureUrl,
                        Teams = linkedPlayer.PlayerTeams.Select(pt => new TeamDto { Id = pt.Team.Id, Name = pt.Team.Name }).ToList(),
                        Parents = linkedPlayer.Parents.Select(pp => new ParentDto { UserId = pp.UserId, FirstName = pp.User.FirstName, LastName = pp.User.LastName }).ToList()
                    };
                }
            }

            return dto;
        }
    }

    public static class InviteCodeGenerator
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous chars

        public static string Generate(int length = 8)
        {
            var bytes = RandomNumberGenerator.GetBytes(length);
            var sb = new StringBuilder(length);
            foreach (var b in bytes)
            {
                sb.Append(Alphabet[b % Alphabet.Length]);
            }
            return sb.ToString();
        }
    }
}
