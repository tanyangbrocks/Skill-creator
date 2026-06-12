namespace SkillCreator.World;

/// <summary>
/// Single source of truth for world scale.
/// Change <see cref="Grain"/> to adjust particle granularity — everything else derives automatically.
/// </summary>
public static class WorldScale
{
    // ── The only knob you need to turn ────────────────────────────────────
    /// <summary>
    /// How many tiles per game-unit.
    /// Larger Grain = finer tiles = better visuals = higher GPU cost.
    /// Grain=16 → ~3 px/tile (Phase A); Grain=32/64 → future hardware.
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

    // GPU CA active zone (must be power-of-2; GpuZoneH = full world height)
    public const int   GpuZoneW          = 128;
    public const int   GpuZoneH          = WorldH;
    public const int   GpuZoneD          = 128;

    // Chunk radii
    public const int   SimRadiusChunks   = 8;
    public const int   MeshRadiusChunks  = 7;
}
