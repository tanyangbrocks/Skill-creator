namespace SkillCreator.World;

public static class TileWorldConstants
{
    /// <summary>
    /// 1 tile 對應的 Godot world unit 數。
    /// 縮小此值可提升視覺細粒度（玩家相對 tile 變大），不影響 CA / 物理邏輯。
    /// 1/32 ≈ 玩家高度 ~29 tiles，球體半徑 ≥ 10 tiles 可達平滑效果。
    /// 1/64 ≈ 玩家高度 ~57 tiles，更細緻但世界 Godot unit 面積更小。
    /// </summary>
    public const float TileSize = 1f / 32f;
}
