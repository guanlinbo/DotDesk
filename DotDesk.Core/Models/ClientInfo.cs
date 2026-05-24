using System;

namespace DotDesk.Core.Models
{
    public sealed record ClientInfo(
        string Id,
        string Name,
        string Role,
        string? Version = null,
        DateTime? LastSeenAt = null);
}
