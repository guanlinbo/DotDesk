using System;

namespace DotDesk.Core.Models
{
    public sealed record ConnectionInfo(
        string DeviceCode,
        string RemoteName,
        DateTime ConnectedAt,
        string Transport = "unknown",
        int? LatencyMs = null);
}
