namespace SkillCreator.World;

using SkillCreator.World.Materials;

/// <summary>
/// 代表玩家一次放置動作產生的物件單元。
/// Tiles 隨破壞縮減；Original 是放置當下的完整快照（唯讀）。
/// Damage = 1 - Tiles.Count / OriginalCount；Damage >= 0.5 時由 Registry 解體。
/// </summary>
public class PlacedUnit
{
    public int          Id            { get; }
    public MaterialType Mat           { get; }
    public int          OriginalCount { get; }

    /// <summary>現存 tiles（隨破壞動態縮減）。</summary>
    public HashSet<GridPos> Tiles    { get; }

    /// <summary>放置當下的完整 tiles 快照（序列化用，不隨破壞改變）。</summary>
    public HashSet<GridPos> Original { get; }

    public float Damage => OriginalCount == 0 ? 1f : 1f - (float)Tiles.Count / OriginalCount;
    public bool  IsIntact => Damage < 0.5f;

    public PlacedUnit(int id, MaterialType mat, IEnumerable<GridPos> tiles)
    {
        Id            = id;
        Mat           = mat;
        Original      = new HashSet<GridPos>(tiles);
        Tiles         = new HashSet<GridPos>(Original);
        OriginalCount = Original.Count;
    }

    // 反序列化用（還原時 Tiles 可能少於 Original）
    public PlacedUnit(int id, MaterialType mat, HashSet<GridPos> original, HashSet<GridPos> current)
    {
        Id            = id;
        Mat           = mat;
        Original      = original;
        Tiles         = current;
        OriginalCount = original.Count;
    }
}
