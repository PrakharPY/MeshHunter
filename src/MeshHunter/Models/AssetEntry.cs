namespace MeshHunter.Models;

public enum AssetKind
{
    Mesh,
    Texture,
    GamePackage,
    Archive,
    Other
}

public sealed record AssetEntry(
    string Name,
    string Path,
    AssetKind Kind,
    string Format,
    string Status,
    string Description)
{
    public override string ToString() => $"[{Kind}] {Name} — {Status}";
}
