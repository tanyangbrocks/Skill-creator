namespace SkillCreator.World;

using System.Collections.Concurrent;
using Godot;
using SkillCreator.World.Materials;
using SkillCreator.World.Terrain;

/// <summary>
/// 3D 地圖生成器，對應 <see cref="TileWorld3D"/>。
/// Phase 2-C：改為 instance class，支援懶加載 chunk 生成。
/// 啟動只生成 Z=0 初始 strip；玩家移動時 EnsureChunksGenerated 按需補生成。
/// </summary>
public class MapGenerator3D
{
    // ── 生成點 ─────────────────────────────────────────────────────────────
    public struct SpawnData
    {
        public GridPos PlayerSpawn;
        public List<(GridPos Pos, EnemyType Type)> EnemySpawns;
    }

    private static readonly (int dx, int dy, int dz)[] _dirs6 =
    {
        ( 0, +1,  0), (-1,  0,  0), ( 1,  0,  0),
        ( 0, -1,  0), ( 0,  0, -1), ( 0,  0, +1),
    };

    // ── 懶加載狀態（Generate / InitTerrainParams 後有效）─────────────────
    private int[,]? _heights;                              // 出生區高度圖 [initW, initD]
    // W-2: FastNoiseLite 取代 sin 週期函數
    private Godot.FastNoiseLite? _heightNoise;             // 地形高度（2D）
    private Godot.FastNoiseLite? _caveThin;                // 細蠕蟲隧道（3D，near-isosurface）
    private Godot.FastNoiseLite? _caveWide;                // 大洞穴（3D，高值區域）
    private int _worldSeed, _worldW, _worldH, _worldD;
    private readonly HashSet<Vector3I>   _generatedChunks = new();
    // ── 地形特徵系統 ─────────────────────────────────────────────────────────
    // 新增地形：繼承 TerrainFeature，加到這個清單即可
    private readonly List<TerrainFeature> _terrainFeatures = new() { new SurfaceWaterPool() };
    // G-3: 世界存檔目錄（空字串 = 不持久化，G-5 設值）
    public string WorldDir { get; set; } = "";

    // ── 非同步 chunk 生成（Direction B）────────────────────────────────────
    // noise pool：Godot FastNoiseLite 必須在主執行緒建立，用 pool 借給 Task.Run
    private readonly ConcurrentBag<(FastNoiseLite hn, FastNoiseLite ct, FastNoiseLite cw)> _noisePool = new();
    // in-flight：已排程但尚未 apply 的 chunk（僅主執行緒存取）
    private readonly HashSet<Vector3I> _inFlight = new();
    // ready queue：背景執行緒填完後 enqueue，主執行緒每幀 drain
    private readonly ConcurrentQueue<(Vector3I coord, MaterialType[] flat)> _readyChunks = new();

    // ── 主入口：只生成初始 Z strip ─────────────────────────────────────────

    /// <summary>
    /// W-2: 建立三個 FastNoiseLite 物件，所有地形/洞穴查詢都從這裡派生。
    /// 由 InitTerrainParams 和 Generate 共同呼叫，確保兩條路徑行為一致。
    /// </summary>
    private void InitNoises(int seed)
    {
        _worldSeed   = seed;
        _heightNoise = MakeHeightNoise(seed);
        _caveThin    = MakeCaveThin(seed);
        _caveWide    = MakeCaveWide(seed);

        // 暖 noise pool（主執行緒建立，借給背景 Task 使用）
        while (_noisePool.TryTake(out _)) { }   // 清舊 pool（換世界 seed 時）
        int poolSize = Math.Max(4, Math.Min(System.Environment.ProcessorCount, 8));
        for (int i = 0; i < poolSize; i++)
            _noisePool.Add((MakeHeightNoise(seed), MakeCaveThin(seed), MakeCaveWide(seed)));
    }

    private static FastNoiseLite MakeHeightNoise(int seed) => new Godot.FastNoiseLite
    {
        NoiseType         = Godot.FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        Seed              = seed,
        Frequency         = 0.001f,
        FractalType       = Godot.FastNoiseLite.FractalTypeEnum.Fbm,
        FractalOctaves    = 7,
        FractalLacunarity = 2.0f,
        FractalGain       = 0.5f,
    };

    private static FastNoiseLite MakeCaveThin(int seed) => new Godot.FastNoiseLite
    {
        NoiseType      = Godot.FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        Seed           = seed + 1,
        Frequency      = 0.022f,
        FractalType    = Godot.FastNoiseLite.FractalTypeEnum.Fbm,
        FractalOctaves = 2,
    };

    private static FastNoiseLite MakeCaveWide(int seed) => new Godot.FastNoiseLite
    {
        NoiseType      = Godot.FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        Seed           = seed + 2,
        Frequency      = 0.008f,
        FractalType    = Godot.FastNoiseLite.FractalTypeEnum.Fbm,
        FractalOctaves = 2,
    };

    /// <summary>
    /// 只初始化地形參數，供 GetHeightAt / IsCaveAt 確定性查詢使用。
    /// 不做任何 SetTile、CA、FloodFill 等重操作。
    /// 再次進入已有世界時呼叫此方法，而非完整的 Generate。
    /// </summary>
    public void InitTerrainParams(TileWorld3D world, int seed)
    {
        _worldW = world.Width; _worldH = world.Height; _worldD = world.Depth;
        InitNoises(seed);
        foreach (var f in _terrainFeatures)
        {
            f.Initialize(seed, _worldW, _worldH, _worldD);
            f.Prepare(GetHeightAt);
        }
    }

    /// <summary>
    /// 返回玩家出生點（載入已有世界時使用）。
    /// 敵人改由 MobSpawnController 動態生成，不再需要靜態出生點。
    /// </summary>
    public SpawnData RebuildSpawns(TileWorld3D world, GridPos playerSpawn)
        => new SpawnData { PlayerSpawn = playerSpawn, EnemySpawns = [] };

    public SpawnData Generate(TileWorld3D world, int seed = 12345)
    {
        var rng  = new Random(seed);
        int W = world.Width, H = world.Height, D = world.Depth;
        // G-0: 初始只生成出生區 3 chunks 寬 × 1 chunk 深，其餘懶加載
        int initW = Math.Min(W, Chunk3D.Size * 3);  // 48 tiles
        int initD = Math.Min(D, Chunk3D.Size);       // 16 tiles

        // W-2: 建立 noise 物件（同時設定 _worldSeed）
        _worldW = W; _worldH = H; _worldD = D;
        InitNoises(seed);

        // 出生區高度圖（initW×initD）
        _heights = GenerateHeightmap(initW, initD);

        FillAll(world, initW, H, initD, MaterialType.Stone);
        ApplyHeightmap(world, _heights, initW, H, initD);

        var caves = GenerateCaCaves(initW, H, initD, _heights, rng);
        ApplyCaves(world, caves, _heights, initW, H, initD);
        EnsureWalkableCaves(world, _heights, initW, H, initD);

        // 地形特徵：先 Initialize+Prepare，確保 ComputeWorldCenterSpawn 的 GenerateChunkLazy
        // 呼叫 GetSurfaceOverride 時已有正確的 WaterSurface（池邊在 spawn 外 ≥64 tiles）
        foreach (var f in _terrainFeatures)
        {
            f.Initialize(seed, W, H, D);
            f.Prepare(GetHeightAt);
        }

        EnsureConnectivity(world, _heights, initW, H, initD, rng);  // 修復出生區洞穴連通性
        var surfaceEntry = ComputeWorldCenterSpawn(world);           // 真正出生點：世界中心
        SealBedrock(world, initW, H, initD);
        PlaceOreVeins(world, _heights, initW, H, initD, rng);
        AddDecor(world, _heights, initW, H, initD, rng);

        // 地形特徵：把 tile 寫入初始條帶（只需 initW×initD 範圍）
        foreach (var f in _terrainFeatures)
            f.PlaceInWorld(world, GetHeightAt, initW, initD);

        // 標記初始已生成的 chunks（僅出生區 3×all×1）
        for (int cz = 0; cz < CeilDiv(initD, Chunk3D.Size); cz++)
        for (int cy = 0; cy < CeilDiv(H,     Chunk3D.Size); cy++)
        for (int cx = 0; cx < CeilDiv(initW, Chunk3D.Size); cx++)
            _generatedChunks.Add(new Vector3I(cx, cy, cz));

        return BuildSpawns(surfaceEntry);
    }

    // ── 懶加載：每幀由 Main._Process 呼叫 ──────────────────────────────────

    /// <summary>
    /// 確保玩家 chunk 座標附近的所有 chunk 都已生成。
    /// 每幀最多生成 maxPerCall 個，避免卡幀。
    /// </summary>
    public void EnsureChunksGenerated(
        TileWorld3D world, int cx, int cy, int cz, int radius, int maxPerCall = 4)
    {
        if (_worldW == 0) return;  // 地形參數尚未初始化（Generate 或 InitTerrainParams 都未呼叫）
        int W = world.Width, H = world.Height, D = world.Depth;
        int maxCX = CeilDiv(W, Chunk3D.Size) - 1;
        int maxCY = CeilDiv(H, Chunk3D.Size) - 1;
        int maxCZ = CeilDiv(D, Chunk3D.Size) - 1;
        int generated = 0;

        // Chebyshev shell：先生成最近的 shell，確保各方向均勻擴展，不偏 Z 方向
        for (int shell = 0; shell <= radius && generated < maxPerCall; shell++)
        for (int dz = -shell; dz <= shell && generated < maxPerCall; dz++)
        for (int dy = -shell; dy <= shell && generated < maxPerCall; dy++)
        for (int dx = -shell; dx <= shell && generated < maxPerCall; dx++)
        {
            if (Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Abs(dz)) != shell) continue;
            var coord = new Vector3I(cx + dx, cy + dy, cz + dz);
            if (coord.X < 0 || coord.Y < 0 || coord.Z < 0) continue;
            if (coord.X > maxCX || coord.Y > maxCY || coord.Z > maxCZ) continue;
            if (!_generatedChunks.Add(coord)) continue; // 已生成或生成中
            if (_inFlight.Contains(coord)) continue;

            // G-3: 磁碟優先；若磁碟沒有才程序生成
            bool fromDisk = WorldDir.Length > 0
                && world.TryLoadChunk(coord.X, coord.Y, coord.Z, WorldDir);

            if (!fromDisk)
            {
                if (_noisePool.TryTake(out var noises))
                {
                    // 非同步路徑：噪音計算移到背景執行緒
                    _inFlight.Add(coord);
                    var c = coord;
                    Task.Run(() =>
                    {
                        try   { _readyChunks.Enqueue((c, ComputeChunkFlat(c, noises))); }
                        finally { _noisePool.Add(noises); } // 一定歸還 pool
                    });
                }
                else
                {
                    // pool 耗盡：同步 fallback
                    GenerateChunkLazy(world, coord);
                }
            }

            if (++generated >= maxPerCall) return;
        }
    }

    /// <summary>
    /// G-4: 卸載遠離玩家的 chunk（存磁碟 + 移出記憶體）。
    /// 同步清除 _generatedChunks，讓下次進入時能重新載入。
    /// 由 Main._Process 每 300 幀呼叫一次。
    /// </summary>
    public void EvictFarChunks(TileWorld3D world, int cx, int cy, int cz, int keepRadius)
    {
        if (WorldDir.Length == 0) return;
        foreach (var coord in world.EvictFarChunks(cx, cy, cz, keepRadius, WorldDir))
            _generatedChunks.Remove(coord);
    }

    /// <summary>
    /// W-2: 確定性地形高度查詢（FastNoiseLite FBm，無週期感）。
    /// 任意 (x,z) 皆可呼叫，chunk 邊界天衣無縫。
    /// </summary>
    public int GetHeightAt(int x, int z)
    {
        float n = _heightNoise!.GetNoise2D(x, z);  // [-1, 1]
        // mean 0.30 → 平均地表在 30% 深度；振幅 0.08 → ±128 tile = ±4 倍玩家身高
        float raw = _worldH * 0.30f + n * (_worldH * 0.08f);
        return Math.Clamp((int)raw, (int)(_worldH * 0.15f), (int)(_worldH * 0.45f));
    }

    /// <summary>
    /// W-2: 確定性 3D 洞穴判斷（FastNoiseLite，取代 sin 乘積）。
    /// 細蠕蟲隧道（near-isosurface）+ 大洞穴（高值區域）雙層結構。
    /// </summary>
    private bool IsCaveAt(int x, int y, int z, int surfaceH)
    {
        // 地表以下 PlayerH 格才有洞穴（確保地表有一整個玩家身高的實心「地殼」）
        if (y <= surfaceH + WorldScale.PlayerH || y >= _worldH - 8) return false;

        // 細蠕蟲隧道：兩個偏移 noise 場同時接近 0 → 形成隧道軸線
        float a = _caveThin!.GetNoise3D(x, y, z);
        float b = _caveThin!.GetNoise3D(x + 317, y + 131, z + 247);
        if (a * a + b * b < 0.020f) return true;

        // 大洞穴：noise 高值區域，Y 方向壓縮 0.6× 讓洞穴橫向更寬
        // 0.80 → 約 10% 密度（Minecraft 地下洞穴約 5-10%）
        float c = _caveWide!.GetNoise3D(x, y * 0.6f, z);
        if (c > 0.80f) return true;

        return false;
    }

    // 主執行緒同步版（世界初始化 + pool 耗盡時 fallback 用）
    private void GenerateChunkLazy(TileWorld3D world, Vector3I coord)
    {
        const int S = Chunk3D.Size;
        int wx0 = coord.X * S, wy0 = coord.Y * S, wz0 = coord.Z * S;
        int W = world.Width, H = world.Height, D = world.Depth;

        for (int lx = 0; lx < S; lx++)
        for (int lz = 0; lz < S; lz++)
        {
            int wx = wx0 + lx, wz = wz0 + lz;
            if ((uint)wx >= (uint)W || (uint)wz >= (uint)D) continue;
            int naturalH = GetHeightAt(wx, wz);

            // 查詢地形特徵覆寫（例如水池會加深地表、換材質）
            (int h, MaterialType mat)? terrainOv = null;
            foreach (var f in _terrainFeatures)
            {
                terrainOv = f.GetSurfaceOverride(wx, wz, naturalH);
                if (terrainOv.HasValue) break;
            }
            int effectiveH      = terrainOv?.h ?? naturalH;
            MaterialType? surfM = terrainOv?.mat;

            bool isWaterPool = surfM == MaterialType.Water;
            for (int ly = 0; ly < S; ly++)
            {
                int wy = wy0 + ly;
                if ((uint)wy >= (uint)H || wy < effectiveH) continue;
                if (isWaterPool && wy < naturalH)
                {
                    // 水池碗內：effectiveH（水面）到 naturalH-1 全填 Water
                    world.SetTile(wx, wy, wz, MaterialType.Water);
                    continue;
                }
                if (IsCaveAt(wx, wy, wz, effectiveH)) continue;
                var mat = wy <= effectiveH + 2 ? MaterialType.Dirt : MaterialType.Stone;
                if (surfM.HasValue && wy == effectiveH) mat = surfM.Value;
                world.SetTile(wx, wy, wz, mat);
            }
        }
    }

    // ── 非同步 chunk 生成（Direction B）───────────────────────────────────────

    // 背景執行緒：純計算，不碰 world，使用租借的 noise 實例
    private MaterialType[] ComputeChunkFlat(
        Vector3I coord,
        (FastNoiseLite hn, FastNoiseLite ct, FastNoiseLite cw) n)
    {
        const int S = Chunk3D.Size;
        int wx0 = coord.X * S, wy0 = coord.Y * S, wz0 = coord.Z * S;
        var flat = new MaterialType[S * S * S]; // 預設 Air(0)

        for (int lx = 0; lx < S; lx++)
        for (int lz = 0; lz < S; lz++)
        {
            int wx = wx0 + lx, wz = wz0 + lz;
            if ((uint)wx >= (uint)_worldW || (uint)wz >= (uint)_worldD) continue;

            float hn = n.hn.GetNoise2D(wx, wz);
            float raw = _worldH * 0.30f + hn * (_worldH * 0.08f);
            int naturalH = Math.Clamp((int)raw, (int)(_worldH * 0.15f), (int)(_worldH * 0.45f));

            // 查詢地形特徵覆寫（背景執行緒只讀 _terrainFeatures，初始化後不可變，無競態）
            (int h, MaterialType mat)? terrainOv = null;
            foreach (var f in _terrainFeatures)
            {
                terrainOv = f.GetSurfaceOverride(wx, wz, naturalH);
                if (terrainOv.HasValue) break;
            }
            int effectiveH      = terrainOv?.h ?? naturalH;
            MaterialType? surfM = terrainOv?.mat;
            bool isWaterPool = surfM == MaterialType.Water;

            for (int ly = 0; ly < S; ly++)
            {
                int wy = wy0 + ly;
                if ((uint)wy >= (uint)_worldH || wy < effectiveH) continue;

                if (isWaterPool && wy < naturalH)
                {
                    // 水池碗內：effectiveH（水面）到 naturalH-1 全填 Water
                    flat[lx * S * S + ly * S + lz] = MaterialType.Water;
                    continue;
                }

                // IsCaveAt（inline，使用租借 noise）
                if (wy > effectiveH + WorldScale.PlayerH && wy < _worldH - 8)
                {
                    float a = n.ct.GetNoise3D(wx, wy, wz);
                    float b = n.ct.GetNoise3D(wx + 317, wy + 131, wz + 247);
                    if (a * a + b * b < 0.020f) continue;
                    float c = n.cw.GetNoise3D(wx, wy * 0.6f, wz);
                    if (c > 0.80f) continue;
                }

                var mat = wy <= effectiveH + 2 ? MaterialType.Dirt : MaterialType.Stone;
                if (surfM.HasValue && wy == effectiveH) mat = surfM.Value;
                flat[lx * S * S + ly * S + lz] = mat;
            }
        }
        return flat;
    }

    // 主執行緒：把背景計算好的 flat buffer 寫入 world
    private static void ApplyChunkFlat(TileWorld3D world, Vector3I coord, MaterialType[] flat)
    {
        const int S = Chunk3D.Size;
        int wx0 = coord.X * S, wy0 = coord.Y * S, wz0 = coord.Z * S;
        int W = world.Width, H = world.Height, D = world.Depth;

        for (int lx = 0; lx < S; lx++)
        for (int ly = 0; ly < S; ly++)
        for (int lz = 0; lz < S; lz++)
        {
            var mat = flat[lx * S * S + ly * S + lz];
            if (mat == MaterialType.Air) continue;
            int wx = wx0 + lx, wy = wy0 + ly, wz = wz0 + lz;
            if ((uint)wx >= (uint)W || (uint)wy >= (uint)H || (uint)wz >= (uint)D) continue;
            world.SetTile(wx, wy, wz, mat);
        }
    }

    /// <summary>
    /// 每幀由 Main._Process 呼叫：將背景完成的 chunk 資料寫入世界（主執行緒）。
    /// </summary>
    public void ApplyPendingChunks(TileWorld3D world, int maxPerFrame = 4)
    {
        int applied = 0;
        while (applied < maxPerFrame && _readyChunks.TryDequeue(out var item))
        {
            _inFlight.Remove(item.coord);
            ApplyChunkFlat(world, item.coord, item.flat);
            applied++;
        }
    }

    /// <summary>
    /// 同步確保單一 chunk 已生成（供 MobSpawnController 按需使用）。
    /// </summary>
    public void EnsureChunkSync(TileWorld3D world, int cx, int cy, int cz)
    {
        if (_worldW == 0) return;
        var coord = new Vector3I(cx, cy, cz);
        if (!_generatedChunks.Add(coord)) return; // 已在清單（生成中或已完成）
        bool fromDisk = WorldDir.Length > 0 && world.TryLoadChunk(cx, cy, cz, WorldDir);
        if (!fromDisk) GenerateChunkLazy(world, coord);
    }

    // ≥2 cardinal neighbors shallower（h 較小 = 視覺上更高），
    // 或在這樣的盆地中心 Manhattan 距離 ≤2 且高度 ≤ 中心深度的擴散範圍內。

    private static int CeilDiv(int a, int b) => (a + b - 1) / b;

    // ════════════════════════════════════════════════════════════
    //  Step 1 — 全填石
    // ════════════════════════════════════════════════════════════

    private static void FillAll(TileWorld3D world, int W, int H, int D, MaterialType mat)
    {
        for (int y = 0; y < H; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
            world.SetTile(x, y, z, mat);
    }

    // ════════════════════════════════════════════════════════════
    //  Step 2 — 高度圖（XZ 2D，each column has one surface Y）
    // ════════════════════════════════════════════════════════════

    // G-1: 改為 instance method，直接呼叫 GetHeightAt（不再用 rng / smoothing）
    private int[,] GenerateHeightmap(int W, int D)
    {
        var heights = new int[W, D];
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
            heights[x, z] = GetHeightAt(x, z);
        return heights;
    }

    private static void ApplyHeightmap(TileWorld3D world, int[,] heights, int W, int H, int D)
    {
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
        {
            int sy = heights[x, z];
            for (int y = 0; y < sy; y++)
                world.SetTile(x, y, z, MaterialType.Air);
            world.SetTile(x, sy,     z, MaterialType.Dirt);
            if (sy + 1 < H) world.SetTile(x, sy + 1, z, MaterialType.Dirt);
            if (sy + 2 < H) world.SetTile(x, sy + 2, z, MaterialType.Dirt);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 3 — 3D CA 洞穴（3×3×3 Moore 鄰域，threshold 14/27）
    // ════════════════════════════════════════════════════════════

    private static bool[,,] GenerateCaCaves(int W, int H, int D, int[,] heights, Random rng)
    {
        int caveTop = MaxHeight(heights, W, D) + 3;
        var cells   = new bool[W, H, D]; // true = 空氣

        for (int y = caveTop; y < H - 8; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
            cells[x, y, z] = rng.NextSingle() < 0.55f;

        var buf = new bool[W, H, D];
        for (int step = 0; step < 4; step++)
        {
            SmoothCa3D(cells, buf, W, H, D, caveTop, threshold: 18);
            (cells, buf) = (buf, cells);
        }
        SmoothCa3D(cells, buf, W, H, D, caveTop, threshold: 15);
        return buf;
    }

    private static void SmoothCa3D(
        bool[,,] src, bool[,,] dst, int W, int H, int D, int caveTop, int threshold)
    {
        for (int y = caveTop; y < H - 8; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            int stoneN = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (nx < 0 || nx >= W || ny < 0 || ny >= H || ny < caveTop || nz < 0 || nz >= D)
                    stoneN++;
                else if (!src[nx, ny, nz])
                    stoneN++;
            }
            dst[x, y, z] = stoneN < threshold;
        }
    }

    private static void ApplyCaves(TileWorld3D world, bool[,,] caves, int[,] heights, int W, int H, int D)
    {
        for (int y = 0; y < H; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
            if (caves[x, y, z] && y > heights[x, z] + 2)
                world.SetTile(x, y, z, MaterialType.Air);
    }

    // 保證每個洞穴地板（air above solid）有 PlayerH+4 格垂直淨空，讓玩家能站立行走
    private static void EnsureWalkableCaves(TileWorld3D world, int[,] heights, int W, int H, int D)
    {
        const int minClear = WorldScale.PlayerH + 4;
        int bedrock = H - 8;

        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            int caveBase = heights[x, z] + 3;
            for (int y = caveBase; y < bedrock - 1; y++)
            {
                if (world.GetTile(x, y, z) != MaterialType.Air) continue;
                if (world.GetTile(x, y + 1, z) == MaterialType.Air) continue; // not a floor

                // Count air running upward from floor tile y
                int clear = 1;
                for (int up = y - 1; up >= caveBase && world.GetTile(x, up, z) == MaterialType.Air; up--)
                    clear++;

                if (clear < minClear)
                {
                    int topAir = y - (clear - 1); // smallest-Y air tile in segment
                    int toCarve = minClear - clear;
                    for (int i = 1; i <= toCarve; i++)
                    {
                        int ty = topAir - i;
                        if (ty < caveBase) break;
                        world.SetTile(x, ty, z, MaterialType.Air);
                    }
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  出生點：世界中心地表，確保對應 chunk 已生成
    // ════════════════════════════════════════════════════════════

    private GridPos ComputeWorldCenterSpawn(TileWorld3D world)
    {
        int spawnX = _worldW / 2;
        int spawnZ = _worldD / 2;

        int spawnCX  = spawnX / Chunk3D.Size;
        int spawnCZ  = spawnZ / Chunk3D.Size;
        int maxCX    = CeilDiv(_worldW, Chunk3D.Size) - 1;
        int maxCZ    = CeilDiv(_worldD, Chunk3D.Size) - 1;
        int totalCY  = CeilDiv(_worldH, Chunk3D.Size);

        // 預生成出生點周圍 3×3 chunk 範圍（防止玩家出生後四周全是空氣）
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int pcx = spawnCX + dx, pcz = spawnCZ + dz;
            if ((uint)pcx > (uint)maxCX || (uint)pcz > (uint)maxCZ) continue;
            for (int cy = 0; cy < totalCY; cy++)
            {
                var coord = new Vector3I(pcx, cy, pcz);
                if (!_generatedChunks.Add(coord)) continue;
                bool fromDisk = WorldDir.Length > 0
                    && world.TryLoadChunk(pcx, cy, pcz, WorldDir);
                if (!fromDisk) GenerateChunkLazy(world, coord);
            }
        }

        int h = GetHeightAt(spawnX, spawnZ);
        int spawnY = Math.Max(0, h - WorldScale.PlayerH);
        return new GridPos(spawnX, spawnY, spawnZ);
    }

    // ════════════════════════════════════════════════════════════
    //  Step 4 — 連通性保證（6-鄰接 FloodFill，以 initD 為 Z 邊界）
    // ════════════════════════════════════════════════════════════

    private static void EnsureConnectivity(
        TileWorld3D world, int[,] heights, int W, int H, int D, Random rng)
    {
        int midX = W / 2, midZ = 0;
        int spawnY   = Math.Max(0, heights[midX, midZ] - WorldScale.PlayerH);
        var start    = new GridPos(midX, spawnY, midZ);
        var visited  = FloodFill3D(world, start, W, H, D); // D = initD，邊界安全
        int caveDeep = MaxHeight(heights, W, D) + 8;

        int zFrom = Math.Max(0, midZ - D / 4);
        int zTo   = Math.Min(D, midZ + D / 4);
        for (int x = midX - W / 4; x < midX + W / 4; x++)
        for (int z = zFrom; z < zTo; z++)
        {
            for (int y = caveDeep; y < H - 10; y++)
            {
                if (world.GetTile(x, y, z) == MaterialType.Air &&
                    !visited.Contains(new GridPos(x, y, z)))
                {
                    for (int sy = heights[x, z]; sy <= y; sy++)
                    for (int dx = -2; dx <= 2; dx++)
                        world.SetTile(Math.Clamp(x + dx, 0, W - 1), sy, z, MaterialType.Air);
                    break;
                }
            }
        }
    }

    private static HashSet<GridPos> FloodFill3D(TileWorld3D world, GridPos start, int W, int H, int D)
    {
        var visited = new HashSet<GridPos>();
        if (world.GetTile(start.X, start.Y, start.Z) != MaterialType.Air) return visited;

        var queue = new Queue<GridPos>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            foreach (var (dx, dy, dz) in _dirs6)
            {
                var n = new GridPos(pos.X + dx, pos.Y + dy, pos.Z + dz);
                // 使用傳入的 W/H/D 做邊界（initD 時 D < world.Depth，防止洪水漫入未生成區）
                if ((uint)n.X >= (uint)W || (uint)n.Y >= (uint)H || (uint)n.Z >= (uint)D) continue;
                if (world.GetTile(n.X, n.Y, n.Z) != MaterialType.Air) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    // ════════════════════════════════════════════════════════════
    //  Step 5 — 底部岩床
    // ════════════════════════════════════════════════════════════

    private static void SealBedrock(TileWorld3D world, int W, int H, int D)
    {
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
        for (int y = H - 8; y < H; y++)
            world.SetTile(x, y, z, MaterialType.Stone);
    }

    // ════════════════════════════════════════════════════════════
    //  Step 6 — 礦脈（6-鄰接 BFS blob）
    // ════════════════════════════════════════════════════════════

    private static void PlaceOreVeins(TileWorld3D world, int[,] heights, int W, int H, int D, Random rng)
    {
        int surfaceBase = MaxHeight(heights, W, D) + 5;

        var configs = new (MaterialType Mat, int YMin, int YMax, int Count, int MaxSize)[]
        {
            (MaterialType.CoalOre,         Math.Max(surfaceBase, (int)(H * 0.28f)), (int)(H * 0.62f), 180, 9),
            (MaterialType.CopperOre,       (int)(H * 0.44f), (int)(H * 0.78f), 120, 6),
            (MaterialType.IronOre,         (int)(H * 0.58f), (int)(H * 0.90f),  80, 5),
            (MaterialType.MagicCrystalOre, (int)(H * 0.74f), (int)(H * 0.95f),  40, 3),
        };

        foreach (var (mat, yMin, yMax, count, maxSize) in configs)
        {
            for (int i = 0; i < count; i++)
            {
                int sx = rng.Next(1, W - 1);
                int sy = rng.Next(yMin, Math.Min(yMax, H - 2));
                int sz = rng.Next(1, D - 1);
                if (world.GetTile(sx, sy, sz) == MaterialType.Stone)
                    PlaceOreBlob3D(world, sx, sy, sz, mat, maxSize, W, H, D, rng);
            }
        }
    }

    private static void PlaceOreBlob3D(TileWorld3D world, int sx, int sy, int sz,
        MaterialType mat, int maxSize, int W, int H, int D, Random rng)
    {
        world.SetTile(sx, sy, sz, mat);
        int placed = 1;
        var queue = new Queue<GridPos>();
        queue.Enqueue(new GridPos(sx, sy, sz));

        while (queue.Count > 0 && placed < maxSize)
        {
            var pos = queue.Dequeue();
            foreach (var (dx, dy, dz) in _dirs6)
            {
                if (placed >= maxSize) break;
                int nx = pos.X + dx, ny = pos.Y + dy, nz = pos.Z + dz;
                if (!world.InBounds(nx, ny, nz)) continue;
                if (world.GetTile(nx, ny, nz) != MaterialType.Stone) continue;
                if (rng.NextSingle() < 0.65f)
                {
                    world.SetTile(nx, ny, nz, mat);
                    queue.Enqueue(new GridPos(nx, ny, nz));
                    placed++;
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 7 — 裝飾（石鐘乳 + 水坑）
    // ════════════════════════════════════════════════════════════

    private static void AddDecor(TileWorld3D world, int[,] heights, int W, int H, int D, Random rng)
    {
        int caveTop = MaxHeight(heights, W, D) + 3;

        for (int y = caveTop + 1; y < H - 8; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            if (world.GetTile(x, y,     z) != MaterialType.Air)  continue;
            if (world.GetTile(x, y - 1, z) != MaterialType.Stone) continue;
            if (rng.NextSingle() >= 0.04f) continue;

            int len = rng.Next(1, 4);
            for (int i = 0; i < len; i++)
            {
                if (y + i >= H - 8) break;
                if (world.GetTile(x, y + i, z) != MaterialType.Air) break;
                world.SetTile(x, y + i, z, MaterialType.Stone);
            }
        }

        for (int y = caveTop; y < H - 9; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            if (world.GetTile(x, y,     z) != MaterialType.Air)   continue;
            if (world.GetTile(x, y + 1, z) != MaterialType.Stone)  continue;
            if (rng.NextSingle() >= 0.06f) continue;

            int poolR = rng.Next(1, 3);
            for (int pz = z - poolR; pz <= z + poolR; pz++)
            for (int px = x - poolR; px <= x + poolR; px++)
            {
                if (!world.InBounds(px, y, pz)) continue;
                if (world.GetTile(px, y, pz) == MaterialType.Air)
                    world.SetTile(px, y, pz, MaterialType.Water);
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 8 — 生成點（敵人改由 MobSpawnController 動態生成）
    // ════════════════════════════════════════════════════════════

    private static SpawnData BuildSpawns(GridPos surfaceEntry)
        => new SpawnData { PlayerSpawn = surfaceEntry, EnemySpawns = [] };

    // ── 輔助 ──────────────────────────────────────────────────────────────

    private static int MaxHeight(int[,] heights, int W, int D)
    {
        int max = 0;
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
            if (heights[x, z] > max) max = heights[x, z];
        return max;
    }
}
