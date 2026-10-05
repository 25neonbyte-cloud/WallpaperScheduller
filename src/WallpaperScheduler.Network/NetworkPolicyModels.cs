using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Network;

public sealed class NetworkPolicyEnvelope
{
    public int ProtocolVersion { get; set; } = 1;
    public string Revision { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid ControllerNodeId { get; set; }
    public string Group { get; set; } = "default";
    public List<WallpaperRule> Rules { get; set; } = [];
    public VisualComfortSettings VisualComfort { get; set; } = new();
    public List<NetworkAssetDescriptor> Assets { get; set; } = [];
}

public sealed record NetworkAssetDescriptor(string Id, string FileName, long Size);

public sealed record AgentHeartbeat(
    Guid NodeId,
    string NodeName,
    string Group,
    string AppVersion,
    string? PolicyRevision,
    DateTimeOffset TimestampUtc);

public sealed record AgentPresence(
    Guid NodeId,
    string NodeName,
    string Group,
    string AppVersion,
    string? PolicyRevision,
    DateTimeOffset AgentTimestampUtc,
    DateTimeOffset LastSeenUtc,
    string? RemoteAddress);

public sealed record BuiltNetworkPolicy(
    NetworkPolicyEnvelope Envelope,
    IReadOnlyDictionary<string, string> AssetPaths);
