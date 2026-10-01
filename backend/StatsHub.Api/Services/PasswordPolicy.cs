namespace StatsHub.Api.Services
{
    // What makes an account password acceptable - length and "not one of the
    // passwords everyone tries first", the way current guidance (NIST 800-63B)
    // puts it, rather than forced symbols and digits.
    public static class PasswordPolicy
    {
        public const int MinLength = 10;
        public const int MaxLength = 128;

        // The most common passwords (from public breach lists) that are long
        // enough to pass the length rule, plus obvious ones for this app.
        private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
        {
            "1234567890", "0123456789", "0987654321", "9876543210", "1111111111", "0000000000",
            "1234512345", "1122334455", "1212121212", "1q2w3e4r5t", "1q2w3e4r5t6y", "q1w2e3r4t5",
            "qwertyuiop", "qwerty1234", "qwerty12345", "qwerty123456", "1qaz2wsx3edc", "zaq12wsxcde3",
            "asdfghjkl1", "asdfghjkl;", "zxcvbnm123", "password12", "password123", "password1234",
            "password!1", "password1!", "passw0rd12", "p@ssword12", "p@ssw0rd123", "iloveyou12",
            "iloveyou123", "abcdefghij", "abcdef1234", "abc1234567", "abcd123456", "a123456789",
            "aa12345678", "123456789a", "1234567890a", "123456789q", "welcome123", "welcome1234",
            "letmein123", "trustno1234", "sunshine12", "princess12", "football12", "football123",
            "baseball12", "basketball", "basketball1", "basketball12", "basketball123", "superman12",
            "batman1234", "starwars12", "dragon1234", "monkey1234", "shadow1234", "michael123",
            "jennifer12", "jordan2323", "jordan23jordan", "liverpool1", "chelsea123", "arsenal123",
            "barcelona1", "realmadrid", "manchester", "manutd1234", "computer12", "internet12",
            "changeme12", "changeme123", "administrator", "admin12345", "admin123456", "user123456",
            "test123456", "testing123", "qazwsxedc1", "qazwsxedcrfv", "1qazxsw23edc", "google1234",
            "facebook12", "whatsapp12", "samsung123", "iphone1234", "statshub123", "statshub1234",
            "maccabi123", "hapoel1234", "israel1234", "shalom1234", "123123123123", "121212121212",
            "123321123321", "147258369", "1472583690", "1357924680", "0102030405", "1020304050",
        };

        // Why this password can't be used, or null when it's fine.
        public static string? Problem(string? password, string? email = null)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinLength)
                return $"Use at least {MinLength} characters.";
            if (password.Length > MaxLength)
                return $"Use at most {MaxLength} characters.";
            if (password.Distinct().Count() < 4 || IsSequence(password) || IsRepeatedBlock(password))
                return "That password is too easy to guess - avoid repeats and sequences like 1234 or abcd.";
            if (Common.Contains(password) || Common.Contains(password.TrimEnd('!', '.', '?')))
                return "That password is too common - pick something less guessable.";

            var name = email?.Split('@')[0];
            if (!string.IsNullOrEmpty(name) && name.Length >= 4 && password.Contains(name, StringComparison.OrdinalIgnoreCase))
                return "Don't use your email in your password.";

            return null;
        }

        // Every character one up (or down) from the one before: 3456789012, hgfedcba.
        private static bool IsSequence(string password)
        {
            var lower = password.ToLowerInvariant();
            bool Step(int direction) => Enumerable.Range(1, lower.Length - 1)
                .All(i => lower[i] - lower[i - 1] == direction || (direction == 1 && lower[i - 1] == '9' && lower[i] == '0') || (direction == -1 && lower[i - 1] == '0' && lower[i] == '9'));
            return Step(1) || Step(-1);
        }

        // The same short block over and over: abcabcabcabc, 12341234.
        private static bool IsRepeatedBlock(string password)
        {
            for (var size = 1; size <= password.Length / 2; size++)
            {
                if (password.Length % size != 0) continue;
                var block = password[..size];
                if (string.Concat(Enumerable.Repeat(block, password.Length / size)) == password) return true;
            }
            return false;
        }
    }
}
