namespace SkillCreator.World;

using SkillCreator.World.Materials;

/// <summary>
/// 仿 Minecraft 的動態野怪生成器。
///
/// 生成邏輯（Common / Area 怪物）：
///   1. 定期嘗試在玩家附近隨機位置（MinSpawnDist ~ MaxSpawnDist 水平 tile）生成怪物。
///   2. 找到可站立的洞穴地板（腳下固體、身體空氣）才實際生成。
///   3. Area 怪物額外限制只在 AreaCenter 半徑內生成。
///   4. 超過 DespawnHardDist 立即 ForceDespawn；
///      DespawnSoftDist ~ DespawnHardDist 區間每秒有 SoftDespawnRate 機率消失。
///
/// SpawnRateMultiplier 可由技能 / 道具即時調整生成速率。
/// </summary>
public class MobSpawnController
{
    // ── 生成環常數（tile 單位；render radius ≈ MeshRadiusChunks×16 = 208 tile）──
    public const int   MinSpawnDist    = 32;   // 內徑：剛好在安全區外
    public const int   MaxSpawnDist    = 128;  // 外徑：在 render radius（208 tile）內
    public const int   DespawnHardDist = 256;  // 超過此距離立即消除（2× render radius）
    public const int   DespawnSoftDist = 192;  // 超過此距離開始機率性消除（render radius 邊緣）
    public const int   MaxCommonActive = 8;     // Common + Area 同時上限
    public const float BaseInterval   = 8f;    // 基礎生成嘗試間隔（秒）

    private const int   SpawnTriesPerAttempt = 24;
    private const float SoftDespawnRate      = 0.05f; // soft 區每秒消除機率

    /// <summary>生成速率倍率；1.0 = 正常，2.0 = 快兩倍。可被技能 / 道具修改。</summary>
    public float SpawnRateMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// 按需生成指定 chunk 的回調（由 Main.cs 注入，指向 MapGenerator3D.EnsureChunksGenerated）。
    /// null 表示不進行按需生成（只在已生成的區塊內找生成點）。
    /// </summary>
    public Action<TileWorld3D, int, int, int>? EnsureChunkAt { get; set; }

    /// <summary>
    /// 取得指定世界座標 (wx, wz) 的地表 tile Y（與地形生成雜訊一致）。
    /// 讓敵人生成在各位置的正確地表高度，而非固定用玩家 Y。
    /// </summary>
    public Func<int, int, int>? GetTerrainY { get; set; }

    private readonly List<MobTableEntry> _table;
    private readonly Random              _rng   = new();
    private float                        _timer = 0f; // 從 0 開始，第一幀立即嘗試

    public MobSpawnController(IEnumerable<MobTableEntry> table)
        => _table = table.ToList();

    /// <summary>每幀由 EnemyManager.Update 呼叫。</summary>
    public void Update(TileWorld3D world, GridPos playerPos, EnemyManager enemies, float delta)
    {
        HandleDespawns(playerPos, enemies, delta);

        _timer -= delta * SpawnRateMultiplier;
        if (_timer > 0f) return;
        _timer = BaseInterval / Math.Max(0.01f, SpawnRateMultiplier);

        if (enemies.DynamicActiveCount >= MaxCommonActive) return;

        var entry = PickEntry();
        if (entry is null) return;

        var pos = TryFindSpawnPos(world, playerPos, entry);
        if (pos.HasValue)
            enemies.Spawn(pos.Value, entry.Type, category: entry.Category);
    }

    // ── 內部方法 ───────────────────────────────────────────────────

    private void HandleDespawns(GridPos playerPos, EnemyManager enemies, float delta)
    {
        foreach (var e in enemies.Enemies)
        {
            if (e.Category is not (SpawnCategory.Common or SpawnCategory.Area)) continue;
            if (!e.IsAlive) continue;

            float d = HorizDist(e.Position, playerPos);
            if (d > DespawnHardDist ||
               (d > DespawnSoftDist && _rng.NextDouble() < SoftDespawnRate * delta))
            {
                e.ForceDespawn();
            }
        }
    }

    private MobTableEntry? PickEntry()
    {
        var pool = _table.FindAll(t =>
            t.Category is SpawnCategory.Common or SpawnCategory.Area);
        if (pool.Count == 0) return null;

        float total = 0f;
        foreach (var t in pool) total += t.Weight;

        float roll = (float)_rng.NextDouble() * total;
        foreach (var t in pool)
        {
            roll -= t.Weight;
            if (roll <= 0f) return t;
        }
        return pool[^1];
    }

    private GridPos? TryFindSpawnPos(TileWorld3D world, GridPos player, MobTableEntry entry)
    {
        int W = world.Width, H = world.Height, D = world.Depth;
        int S = Chunk3D.Size;

        for (int attempt = 0; attempt < SpawnTriesPerAttempt; attempt++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2;
            int hDist    = MinSpawnDist + _rng.Next(MaxSpawnDist - MinSpawnDist + 1);
            int tx = player.X + (int)(Math.Cos(angle) * hDist);
            int tz = player.Z + (int)(Math.Sin(angle) * hDist);

            tx = Math.Clamp(tx, 1, W - 2);
            tz = Math.Clamp(tz, 1, D - 2);

            // 用地形噪音取得此位置的地表 Y，讓敵人生成在正確高度而非玩家 Y
            int surfaceH = GetTerrainY?.Invoke(tx, tz) ?? (player.Y + WorldScale.PlayerH);
            int ty = Math.Clamp(surfaceH - WorldScale.PlayerH, 0, H - 2);
            if ((uint)ty >= (uint)(H - 1)) continue;

            // Area 怪物：檢查是否在限定區域內
            if (entry.Category == SpawnCategory.Area && entry.AreaRadius > 0)
            {
                long adx = tx - entry.AreaCenter.X;
                long adz = tz - entry.AreaCenter.Z;
                if (adx * adx + adz * adz > (long)entry.AreaRadius * entry.AreaRadius)
                    continue;
            }

            // 按需生成目標 chunk + 地表 chunk（讓敵人有地板可以落地，而非墜入虛空）
            EnsureChunkAt?.Invoke(world, tx / S, ty / S,       tz / S);
            EnsureChunkAt?.Invoke(world, tx / S, surfaceH / S, tz / S);

            if (world.GetTile(tx, ty, tz) == MaterialType.Air
             && world.GetTile(tx, ty + 1, tz) != MaterialType.Air)
                return new GridPos(tx, ty, tz);
        }
        return null;
    }

    private static float HorizDist(GridPos a, GridPos b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
