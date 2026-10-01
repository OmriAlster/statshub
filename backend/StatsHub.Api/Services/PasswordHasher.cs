using System.Security.Cryptography;

namespace StatsHub.Api.Services
{
    // PBKDF2-SHA256 for account passwords and the optional invite passwords.
    // Stored as "pbkdf2-sha256$<iterations>$<salt>$<hash>" so the work factor
    // can go up over time: a hash made with fewer iterations (or the original
    // "<salt>.<hash>" form, 100,000 iterations) still verifies, and
    // NeedsRehash tells the sign-in to store a fresh one.
    public static class PasswordHasher
    {
        private const string Prefix = "pbkdf2-sha256";
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int LegacyIterations = 100_000;
        // OWASP's current recommendation for PBKDF2-HMAC-SHA256.
        public const int Iterations = 600_000;

        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string? storedHash)
        {
            if (!TryParse(storedHash, out var iterations, out var salt, out var expectedHash)) return false;
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        // Made with an older, weaker setting - worth replacing next time the
        // password is known (a successful sign-in).
        public static bool NeedsRehash(string? storedHash) =>
            !TryParse(storedHash, out var iterations, out _, out _) || iterations < Iterations;

        private static bool TryParse(string? storedHash, out int iterations, out byte[] salt, out byte[] hash)
        {
            iterations = 0;
            salt = hash = Array.Empty<byte>();
            if (string.IsNullOrEmpty(storedHash)) return false;
            try
            {
                var parts = storedHash.Split('$');
                if (parts.Length == 4 && parts[0] == Prefix && int.TryParse(parts[1], out iterations))
                {
                    salt = Convert.FromBase64String(parts[2]);
                    hash = Convert.FromBase64String(parts[3]);
                    return iterations > 0;
                }

                var legacy = storedHash.Split('.');
                if (legacy.Length != 2) return false;
                iterations = LegacyIterations;
                salt = Convert.FromBase64String(legacy[0]);
                hash = Convert.FromBase64String(legacy[1]);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
