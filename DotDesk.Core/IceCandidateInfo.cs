using System;
using System.Text;
using System.Text.RegularExpressions;

namespace DotDesk.Core.Network
{
    public sealed record IceCandidateInfo(
        string Raw,
        string Type,
        string Protocol,
        string Ip,
        int Port)
    {
        public bool IsRelay => Type.Equals("relay", StringComparison.OrdinalIgnoreCase);
        public bool IsServerReflexive => Type.Equals("srflx", StringComparison.OrdinalIgnoreCase);
        public bool IsHost => Type.Equals("host", StringComparison.OrdinalIgnoreCase);
        public bool IsIpv6 => Ip.Contains(':');

        public override string ToString() =>
            $"{Type.ToUpperInvariant()} {Protocol.ToUpperInvariant()} {Ip}:{Port}";
    }

    public static class IceCandidateTools
    {
        public static IceCandidateInfo? Parse(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;

            var raw = candidate.Trim();
            if (raw.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase))
                raw = raw["a=".Length..];

            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8) return null;

            var typeIndex = Array.FindIndex(parts, p => p.Equals("typ", StringComparison.OrdinalIgnoreCase));
            if (typeIndex < 0 || typeIndex + 1 >= parts.Length) return null;

            int.TryParse(parts[5], out var port);
            return new IceCandidateInfo(
                candidate.Trim(),
                parts[typeIndex + 1],
                parts[2],
                parts[4],
                port);
        }

        public static bool HasRelayCandidate(string? sdp) =>
            !string.IsNullOrWhiteSpace(sdp)
            && Regex.IsMatch(sdp, @"a=candidate:.* typ relay(?:\s|$)", RegexOptions.IgnoreCase);

        public static string StripRelayCandidates(string sdp)
        {
            var lines = sdp.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder(sdp.Length);

            foreach (var line in lines)
            {
                if (line.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase))
                {
                    var info = Parse(line);
                    if (info?.IsRelay == true)
                        continue;
                }

                if (line.Length > 0)
                    sb.Append(line).Append("\r\n");
            }

            return sb.ToString();
        }

        public static string Describe(string? candidate)
        {
            var info = Parse(candidate);
            return info == null ? candidate ?? "<empty>" : info.ToString();
        }
    }
}
