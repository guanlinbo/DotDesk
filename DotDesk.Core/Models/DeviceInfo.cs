namespace DotDesk.Core.Models
{
    public sealed record DeviceInfo(
        string Code,
        string Name,
        bool Online,
        string? Address = null,
        int? ServerLatencyMs = null);
}
