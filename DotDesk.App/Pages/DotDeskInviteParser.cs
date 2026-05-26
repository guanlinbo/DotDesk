using System.Linq;
using System.Text.RegularExpressions;

namespace DotDesk.App
{
    internal readonly record struct DotDeskInvite(string DeviceCode, string Password);

    internal static class DotDeskInviteParser
    {
        public static bool TryParse(string? text, out DotDeskInvite invite)
        {
            invite = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string raw = text.Trim();
            string digits = Regex.Replace(raw, @"\D", "");
            if (digits.Length < 9)
                return false;

            string code = digits[..9];
            string? password = TryMatchPassword(raw);
            if (string.IsNullOrWhiteSpace(password))
                return false;

            invite = new DotDeskInvite(code, password);
            return true;
        }

        private static string? TryMatchPassword(string raw)
        {
            var labels = new[]
            {
                "一次性密码",
                "访问密码",
                "密码",
                "password",
                "pwd"
            };

            foreach (string label in labels)
            {
                var match = Regex.Match(
                    raw,
                    $@"{Regex.Escape(label)}\s*[:：]\s*([A-Za-z0-9][A-Za-z0-9\-\s]{{4,16}})",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                string normalized = NormalizePassword(match.Groups[1].Value);
                if (normalized.Length == 6)
                    return normalized;
            }

            var tokens = Regex.Matches(raw, @"[A-Za-z0-9]{6}")
                .Select(m => m.Value.ToLowerInvariant())
                .Where(v => !Regex.IsMatch(v, @"^\d{6}$"));

            return tokens.FirstOrDefault();
        }

        private static string NormalizePassword(string text) =>
            Regex.Replace(text, @"[^A-Za-z0-9]", "").Trim().ToLowerInvariant();
    }
}
