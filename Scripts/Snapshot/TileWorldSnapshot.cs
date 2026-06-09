namespace SkillCreator.Snapshot;

using SkillCreator.World;

/// <summary>
/// S-13：TileWorld 圓形區域的不可變材質快照。
/// Cells 鍵 = y * Width + x（與 TileWorld.Idx 一致），值為完整 TileCell（含 Variant 與 Timer）。
/// </summary>
public sealed record TileWorldSnapshot(
    GridPos                   Center,
    int                       Radius,
    Dictionary<int, TileCell> Cells
);
