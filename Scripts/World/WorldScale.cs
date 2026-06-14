namespace SkillCreator.World;

/// <summary>
/// Single source of truth for world scale.
/// Change <see cref="Grain"/> to adjust particle granularity — everything else derives automatically.
/// </summary>
public static class WorldScale
{
    // ── The only knob you need to turn ────────────────────────────────────
    /// <summary>
    /// 「遊戲單位」（設計語言）= 1 Grain = Grain tiles = 1 Godot 世界單位。
    /// 玩家身高 = 2 遊戲單位；玩家寬度 = 1 遊戲單位。
    /// Larger Grain = finer tiles = better visuals = higher GPU cost.
    /// Grain=16 → ~3 px/tile (Phase A); Grain=32/64 → future hardware.
    ///
    /// 調高此值的效果（以 16→32 為例）：
    ///   · 世界、玩家、水池半徑等所有「遊戲單位」換算全部等比例放大
    ///   · TileSize = 1/Grain 自動縮小，物理顯示大小不變
    ///   · CA（沙/水）動畫細膩度 2×，但格子總數 4×，CPU/GPU 工作量也 4×
    ///
    /// ⚠ 注意：GpuZoneW/H/D 是固定 tile 數，不隨 Grain 縮放。
    ///   Grain 翻倍時須手動把三個 GpuZone 常數也乘 2，
    ///   否則 GPU CA 的覆蓋物理範圍只剩原本的一半。
    /// </summary>
    public const int Grain = 16;

    // ── Derived constants — do not edit directly ───────────────────────────
    public const float TileSize          = 1f / (float)Grain;   // Godot unit per tile
    public const int   PlayerH           = Grain * 2;            // player height in tiles
    public const int   PlayerW           = Grain;                // player width  in tiles

    // World dimensions (proportional to player)
    public const int   WorldH            = PlayerH * 50;         // 50× player height
    public const int   WorldW            = PlayerH * 100;
    public const int   WorldD            = WorldW;

    // Camera: player occupies ~10% of screen height
    public const int   CamTilesV         = PlayerH * 10;
    public const float OrthoSize         = CamTilesV * TileSize * 0.5f;

    // GPU CA active zone (must be power-of-2)
    // GpuZoneH 以玩家 Y 為中心；256 tiles = 8 chunks 上下，足夠 Powder/Liquid 物理
    public const int   GpuZoneW          = 128;
    public const int   GpuZoneH          = 256;
    public const int   GpuZoneD          = 128;

    // Chunk radii
    public const int   SimRadiusChunks   = 8;
    public const int   MeshRadiusChunks  = 13;

    // Grid → Godot3D: godotX = gridX * TileSize - OriginX  (centers grid at Godot origin)
    // Godot3D → Grid: gridX  = (godotX + OriginX) / TileSize
    public static readonly float OriginX = WorldW * 0.5f * TileSize;
    public static readonly float OriginZ = WorldD * 0.5f * TileSize;
}
