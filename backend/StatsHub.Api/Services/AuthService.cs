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
        Task<UserDto?> GetCurrentUserAsync(Guid userId);
        Task<AuthResponseDto?> LogOutEverywhereAsync(Guid userId);
        string GenerateJwt(User user);
    }

    // The login token carries the account's security stamp; a token whose
    // stamp no longer matches the account's is refused (see Program.cs).
    public static class SessionStamp
    {
        public const string ClaimType = "sstamp";
        public static string CacheKey(Guid userId) => $"sstamp:{userId}";
        public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

        public AuthService(AppDbContext context, IConfiguration configuration, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
        {
            _context = context;
            _configuration = configuration;
            _cache = cache;
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

            var user = await FindOrCreateGoogleUserAsync(payload.Subject, payload.Email, payload.EmailVerified, payload.GivenName ?? payload.Name, payload.FamilyName, payload.Picture);

            var token = GenerateJwt(user);
            var userDto = await BuildUserDtoAsync(user);
            return new AuthResponseDto { Token = token, User = userDto };
        }

        // The account a Google sign-in lands in: the one already connected to
        // this Google account, else one with the same email (connected now -
        // see the comment inside), else a new one.
        internal async Task<User> FindOrCreateGoogleUserAsync(string subject, string email, bool emailVerified, string? firstName, string? lastName, string? picture)
        {
            var googleEmail = NormalizeEmail(email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.GoogleId == subject)
                ?? await _context.Users.FirstOrDefaultAsync(u => u.GoogleId == null && u.Email.ToLower() == googleEmail);

            if (user != null && user.GoogleId == null)
            {
                // Signing in with Google to an account first made with a
                // password. Registering never proved that whoever set that
                // password owns this email - Google does - so the password is
                // dropped and every session it opened is ended. Otherwise
                // someone could register a parent's email first and keep a
                // way in after the real parent starts using the account.
                if (!emailVerified)
                    throw new UnauthorizedAccessException("Your Google account's email isn't verified, so it can't be connected to an existing account.");
                if (user.PasswordHash != null)
                {
                    user.PasswordHash = null;
                    user.SecurityStamp = SessionStamp.New();
                    _cache.Remove(SessionStamp.CacheKey(user.Id));
                }
            }

            if (user == null)
            {
                user = new User
                {
                    Email = googleEmail,
                    FirstName = firstName ?? "Player",
                    LastName = lastName ?? string.Empty,
                    GoogleId = subject,
                    ProfilePictureUrl = picture,
                    Role = "Parent",
                    SecurityStamp = SessionStamp.New(),
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
            }
            else
            {
                user.GoogleId ??= subject;
                user.ProfilePictureUrl = picture ?? user.ProfilePictureUrl;
                user.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<AuthResponseDto> DevLoginAsync(DevLoginDto dto)
        {
            var email = NormalizeEmail(dto.Email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
            if (user == null)
            {
                user = new User
                {
                    Email = email,
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
            var email = NormalizeEmail(dto.Email);
            var problem = PasswordPolicy.Problem(dto.Password, email);
            if (problem != null) throw new InvalidOperationException(problem);

            // Any existing account with this email - password or Google -
            // means "sign in instead". This used to attach the new password
            // to an existing Google account, which let anyone who knew a
            // parent's email set a password on their account and sign in as
            // them. Adding password login to a Google account needs to be
            // done while signed in to it, not by anonymous registration.
            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email))
                throw new InvalidOperationException("An account with this email already exists. Please sign in instead.");

            var user = new User
            {
                Email = email,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Role = "Parent",
                PasswordHash = PasswordHasher.Hash(dto.Password),
                SecurityStamp = SessionStamp.New(),
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);

            await _context.SaveChangesAsync();

            var token = GenerateJwt(user);
            var userDto = await BuildUserDtoAsync(user);
            return new AuthResponseDto { Token = token, User = userDto };
        }

        public async Task<AuthResponseDto> LoginWithPasswordAsync(PasswordLoginDto dto)
        {
            var email = NormalizeEmail(dto.Email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
            if (user == null || !PasswordHasher.Verify(dto.Password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid email or password.");

            // Stored with an older, weaker setting - upgrade it now that the
            // password is known.
            if (PasswordHasher.NeedsRehash(user.PasswordHash))
            {
                user.PasswordHash = PasswordHasher.Hash(dto.Password);
                await _context.SaveChangesAsync();
            }

            var token = GenerateJwt(user);
            var userDto = await BuildUserDtoAsync(user);
            return new AuthResponseDto { Token = token, User = userDto };
        }

        // Every token issued so far stops working; this device gets a new one.
        public async Task<AuthResponseDto?> LogOutEverywhereAsync(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return null;
            user.SecurityStamp = SessionStamp.New();
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _cache.Remove(SessionStamp.CacheKey(user.Id));
            return new AuthResponseDto { Token = GenerateJwt(user), User = await BuildUserDtoAsync(user) };
        }

        public async Task<UserDto?> GetCurrentUserAsync(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return null;
            return await BuildUserDtoAsync(user);
        }
        // Emails are matched case-insensitively and without stray spaces
        // everywhere (Pat@x.com and pat@x.com are one person); new accounts
        // are stored lower-cased. Lookups lower-case the stored value too, so
        // accounts saved with capitals before this still match.
        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();


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
            if (user.SecurityStamp != null) claims.Add(new(SessionStamp.ClaimType, user.SecurityStamp));

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
                        Position = linkedPlayer.Position,
                        Height = linkedPlayer.Height,
                        Weight = linkedPlayer.Weight,
                        DateOfBirth = linkedPlayer.DateOfBirth,
                        ProfilePictureUrl = linkedPlayer.ProfilePictureUrl,
                        Teams = linkedPlayer.PlayerTeams.Select(pt => new TeamDto { Id = pt.Team.Id, Name = pt.Team.Name, JerseyNumber = pt.JerseyNumber }).ToList(),
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
