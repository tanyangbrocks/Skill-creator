using Godot;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.Elemental;
using SkillCreator.Snapshot;
using SkillCreator.World.Materials;

namespace SkillCreator.World;

/// <summary>
/// Phase 1：完整 3D CA 世界（純邏輯，無渲染）。
/// 以 16³ Chunk 字典管理空間，Tick 只處理 Dirty Chunk。
/// Phase 2 接渲染時：在 Tick 後呼叫 RebuildDirtyMeshes。
/// </summary>
public sealed class TileWorld3D : IWorldInterface
{
    public int Width  { get; }
    public int Height { get; }
    public int Depth  { get; }

    private readonly Dictionary<Vector3I, Chunk3D> _chunks = new();
    private readonly HashSet<(int x, int y, int z)> _occupied = new();
    private readonly Random _rng = new(42);
    private int _frame;

    private static readonly (int dx, int dy, int dz)[] _neighbors6 =
    {
        ( 0, +1,  0), // 下（Y+）
        ( 0, -1,  0), // 上
        (-1,  0,  0), // 左
        ( 1,  0,  0), // 右
        ( 0,  0, -1), // 前
        ( 0,  0, +1), // 後
    };

    // ── IWorldInterface 事件 ─────────────────────────────────────────────
    public event Action<WorldEntity, WorldEntity, float>? OnEntityHit;
    public event Action<GridPos, MaterialType>?          OnTileDestroyed;
    public event Action<WorldEntity>?                    OnEntityDied;
#pragma warning disable CS0067
    public event Action<string, object?>?                OnPlayerAction;
#pragma warning restore CS0067
    public event Action<GridPos, int>?                   OnExplosion;

    // ── 實體層 ────────────────────────────────────────────────────────────
    private readonly List<WorldEntity> _entities = new();
    private int _nextEntityId = 1;

    public TileWorld3D(int width, int height, int depth)
    {
        Width = width; Height = height; Depth = depth;
    }

    // ════════════════════════════════════════════════════════════
    //  實體佔用登記（Phase 2 由渲染層每幀呼叫）
    // ════════════════════════════════════════════════════════════

    public void ClearOccupied()  => _occupied.Clear();
    public void SetOccupied(int x, int y, int z) => _occupied.Add((x, y, z));

    // ════════════════════════════════════════════════════════════
    //  模擬 Tick（Dirty Chunk 優先，由底往上掃）
    // ════════════════════════════════════════════════════════════

    public void Tick()
    {
        _frame++;
        bool xFirst = (_frame % 2 == 0);
        bool zFirst = (_frame % 4 < 2);

        // 快照本幀 dirty chunk 並立即清除（Tick 期間新產生的 dirty = 下幀標記）
        var toProcess = new List<(Vector3I coord, Chunk3D chunk)>();
        foreach (var (coord, chunk) in _chunks)
        {
            if (!chunk.IsDirty) continue;
            chunk.ClearDirty();
            chunk.ClearUpdated();
            toProcess.Add((coord, chunk));
        }

        // 由底往上（高 chunkY = 底部）
        toProcess.Sort((a, b) => b.coord.Y.CompareTo(a.coord.Y));

        foreach (var (coord, chunk) in toProcess)
        {
            int wx0 = coord.X * Chunk3D.Size;
            int wy0 = coord.Y * Chunk3D.Size;
            int wz0 = coord.Z * Chunk3D.Size;

            // 由 chunk 底部往上（高 ly = 底部）
            for (int ly = Chunk3D.Size - 1; ly >= 0; ly--)
            for (int lzi = 0; lzi < Chunk3D.Size; lzi++)
            for (int lxi = 0; lxi < Chunk3D.Size; lxi++)
            {
                int lz = zFirst ? lzi : (Chunk3D.Size - 1 - lzi);
                int lx = xFirst ? lxi : (Chunk3D.Size - 1 - lxi);
                int wx = wx0 + lx, wy = wy0 + ly, wz = wz0 + lz;

                if (!InBounds(wx, wy, wz) || IsUpdated(wx, wy, wz)) continue;

                switch (MaterialRegistry.Get(GetTile(wx, wy, wz)).Physics)
                {
                    case PhysicsCategory.Powder: UpdatePowder(wx, wy, wz); break;
                    case PhysicsCategory.Liquid: UpdateLiquid(wx, wy, wz); break;
                    case PhysicsCategory.Gas:    UpdateGas(wx, wy, wz);    break;
                    case PhysicsCategory.Static: UpdateStatic(wx, wy, wz); break;
                }
            }
        }
    }

    // ── 粉末（沙，Y+ 向下重力 + 隨機斜向滑落）──────────────────────────

    private void UpdatePowder(int x, int y, int z)
    {
        if (TryMove(x, y, z, x, y + 1, z)) return;
        // 斜向：4 個斜下方向，隨機選序
        bool xb = _rng.Next(2) == 0, zb = _rng.Next(2) == 0;
        int x2 = xb ? x - 1 : x + 1, z2 = zb ? z - 1 : z + 1;
        if (TryMove(x, y, z, x2, y + 1, z))  return;
        if (TryMove(x, y, z, x,  y + 1, z2)) return;
        if (TryMove(x, y, z, x2, y + 1, z2)) return;
    }

    // ── 液體（水 / 岩漿）────────────────────────────────────────────────

    private void UpdateLiquid(int x, int y, int z)
    {
        var mat = GetTile(x, y, z);
        if (mat == MaterialType.Lava && _frame % 3 != 0) return;

        if (TryMove(x, y, z, x, y + 1, z)) return;
        if (TryMove(x, y, z, x - 1, y + 1, z)) return;
        if (TryMove(x, y, z, x + 1, y + 1, z)) return;
        if (TryMove(x, y, z, x, y + 1, z - 1)) return;
        if (TryMove(x, y, z, x, y + 1, z + 1)) return;

        // 水平擴散（6 方向 XZ 平面）
        bool lr = (_frame % 2 == 0), fb = (_frame % 4 < 2);
        int spread = (mat == MaterialType.Water) ? 3 : 1;
        for (int i = 1; i <= spread; i++)
        {
            if (TryMove(x, y, z, x + (lr ? -i : i), y, z))       return;
            if (TryMove(x, y, z, x,                 y, z + (fb ? -i : i))) return;
        }
        for (int i = 1; i <= spread; i++)
        {
            if (TryMove(x, y, z, x + (lr ? i : -i), y, z))       return;
            if (TryMove(x, y, z, x,                 y, z + (fb ? i : -i))) return;
        }

        if (mat == MaterialType.Lava) TryIgniteAround(x, y, z, 0.1f);
        CheckElementalCaReactions(x, y, z);
    }

    // ── 氣體（火 / 蒸汽，Y- 向上浮升）──────────────────────────────────

    private void UpdateGas(int x, int y, int z)
    {
        var chunkCoord = WorldToChunk(x, y, z);
        if (!_chunks.TryGetValue(chunkCoord, out var chunk)) return;
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        ref var cell = ref chunk.Cells[chunk.Idx(lx, ly, lz)];

        cell.Timer--;
        if (cell.Timer <= 0)
        {
            SetTile(x, y, z, cell.Type == MaterialType.Fire ? MaterialType.Ash : MaterialType.Air);
            return;
        }

        if (cell.Type == MaterialType.Fire)
        {
            if (!TryMove(x, y, z, x, y - 1, z))
            {
                bool lr = _rng.Next(2) == 0, fb = _rng.Next(2) == 0;
                TryMove(x, y, z, x + (lr ? -1 : 1), y - 1, z + (fb ? 0 : 0));
            }
            TryIgniteAround(x, y, z, 0.08f);
            if (HasAdjacent(x, y, z, MaterialType.Water)) { ExtinguishFire(x, y, z); return; }
        }
        else // Steam
        {
            if (!TryMove(x, y, z, x, y - 1, z))
            {
                bool lr = _rng.Next(2) == 0, fb = _rng.Next(2) == 0;
                if (!TryMove(x, y, z, x + (lr ? -1 : 1), y, z))
                     TryMove(x, y, z, x, y, z + (fb ? -1 : 1));
            }
        }
    }

    // ── 靜態（木燃燒）───────────────────────────────────────────────────

    private void UpdateStatic(int x, int y, int z)
    {
        var chunkCoord = WorldToChunk(x, y, z);
        if (!_chunks.TryGetValue(chunkCoord, out var chunk)) return;
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        ref var cell = ref chunk.Cells[chunk.Idx(lx, ly, lz)];
        if (cell.Type != MaterialType.Wood || cell.Timer <= 0) return;

        cell.Timer--;
        if (cell.Timer <= 0) { SetTile(x, y, z, MaterialType.Ash); return; }
        if (_rng.NextDouble() < 0.15 && InBounds(x, y - 1, z) && GetTile(x, y - 1, z) == MaterialType.Air)
            SetFire(x, y - 1, z);
    }

    // ════════════════════════════════════════════════════════════
    //  CA 移動核心
    // ════════════════════════════════════════════════════════════

    private bool TryMove(int fx, int fy, int fz, int tx, int ty, int tz)
    {
        if (!InBounds(tx, ty, tz)) return false;
        var from     = GetTile(fx, fy, fz);
        var to       = GetTile(tx, ty, tz);
        var fromData = MaterialRegistry.Get(from);
        var toData   = MaterialRegistry.Get(to);

        if (to == MaterialType.Air && _occupied.Contains((tx, ty, tz))) return false;
        if (to != MaterialType.Air &&
            !(toData.Physics == PhysicsCategory.Liquid && fromData.Density > toData.Density))
            return false;

        // 交換格子
        var fc = GetCell(fx, fy, fz);
        var tc = GetCell(tx, ty, tz);
        WriteCell(tx, ty, tz, fc);
        WriteCell(fx, fy, fz, tc);
        MarkUpdated(tx, ty, tz);
        return true;
    }

    // ── 元素 CA ───────────────────────────────────────────────────────────

    private const double CaChanceSlow = 0.005;
    private const double CaChanceFast = 0.03;

    private void CheckElementalCaReactions(int x, int y, int z)
    {
        var mat     = GetTile(x, y, z);
        var matData = MaterialRegistry.Get(mat);
        if (matData.NativeElement == ElementType.None) return;

        foreach (var (dx, dy, dz) in _neighbors6)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!InBounds(nx, ny, nz)) continue;
            var nMat     = GetTile(nx, ny, nz);
            var nMatData = MaterialRegistry.Get(nMat);
            if (nMatData.NativeElement == ElementType.None) continue;

            if (matData.NativeElement == ElementType.Water
                && nMat == MaterialType.Dirt
                && _rng.NextDouble() < CaChanceSlow)
            {
                SetTile(nx, ny, nz, MaterialType.Sand);
                MarkUpdated(nx, ny, nz);
            }

            if (mat == MaterialType.Lava
                && nMatData.NativeElement == ElementType.Water
                && _rng.NextDouble() < CaChanceFast)
            {
                SetTile(x, y, z, MaterialType.Stone);
                SetTile(nx, ny, nz, MaterialType.Steam);
                MarkUpdated(nx, ny, nz);
                return;
            }
        }
    }

    /// <summary>技能命中時套用元素 CA 反應（與 TileWorld.ApplyElementalImpact 相同語意）。</summary>
    public void ApplyElementalImpact(int x, int y, int z, ElementType impactElement)
    {
        if (!InBounds(x, y, z) || impactElement == ElementType.None) return;
        var tileData = MaterialRegistry.Get(GetTile(x, y, z));
        var tileElem = tileData.NativeElement;
        if (tileElem == ElementType.None) return;

        var reaction = ElementalReactionTable.Lookup(impactElement, tileElem);
        if (reaction == null) return;

        switch (reaction.Name)
        {
            case "沸騰": if (tileElem == ElementType.Water) SetTile(x, y, z, MaterialType.Steam);  break;
            case "流沙": if (GetTile(x, y, z) == MaterialType.Dirt)  SetTile(x, y, z, MaterialType.Sand); break;
            case "燃燒": IgniteMaterial(x, y, z); break;
        }
    }

    // ── 輔助方法 ─────────────────────────────────────────────────────────

    private void TryIgniteAround(int x, int y, int z, float chance)
    {
        foreach (var (dx, dy, dz) in _neighbors6)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!InBounds(nx, ny, nz)) continue;
            var mat = GetTile(nx, ny, nz);
            if (MaterialRegistry.Get(mat).IsFlammable && _rng.NextDouble() < chance)
                IgniteMaterial(nx, ny, nz);
        }
    }

    private void IgniteMaterial(int x, int y, int z)
    {
        var chunkCoord = WorldToChunk(x, y, z);
        if (!_chunks.TryGetValue(chunkCoord, out var chunk)) return;
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        ref var cell = ref chunk.Cells[chunk.Idx(lx, ly, lz)];
        var data = MaterialRegistry.Get(cell.Type);
        if (!data.IsFlammable || cell.Timer > 0) return;
        cell.Timer = (short)_rng.Next(data.BurnDurationMin, data.BurnDurationMax + 1);
        chunk.MarkDirty(lx, ly, lz);
    }

    private void SetFire(int x, int y, int z)
    {
        var chunkCoord = WorldToChunk(x, y, z);
        var chunk      = GetOrCreateChunk(chunkCoord);
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        ref var cell = ref chunk.Cells[chunk.Idx(lx, ly, lz)];
        cell.Type    = MaterialType.Fire;
        cell.Variant = (byte)_rng.Next(256);
        cell.Timer   = (short)_rng.Next(30, 90);
        chunk.MarkDirty(lx, ly, lz);
    }

    private void ExtinguishFire(int x, int y, int z)
    {
        SetTile(x, y, z, MaterialType.Air);
        foreach (var (dx, dy, dz) in _neighbors6)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!InBounds(nx, ny, nz) || GetTile(nx, ny, nz) != MaterialType.Water) continue;
            var chunkCoord = WorldToChunk(nx, ny, nz);
            var chunk      = GetOrCreateChunk(chunkCoord);
            var (lx, ly, lz) = WorldToLocal(nx, ny, nz);
            ref var wc = ref chunk.Cells[chunk.Idx(lx, ly, lz)];
            wc.Type  = MaterialType.Steam;
            wc.Timer = (short)_rng.Next(60, 120);
            chunk.MarkDirty(lx, ly, lz);
        }
    }

    private bool HasAdjacent(int x, int y, int z, MaterialType mat)
    {
        foreach (var (dx, dy, dz) in _neighbors6)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (InBounds(nx, ny, nz) && GetTile(nx, ny, nz) == mat) return true;
        }
        return false;
    }

    // ── Updated 追蹤（同幀防重複）────────────────────────────────────────

    private bool IsUpdated(int x, int y, int z)
    {
        var chunkCoord = WorldToChunk(x, y, z);
        if (!_chunks.TryGetValue(chunkCoord, out var chunk)) return false;
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        return chunk.Updated[chunk.Idx(lx, ly, lz)];
    }

    private void MarkUpdated(int x, int y, int z)
    {
        var chunkCoord = WorldToChunk(x, y, z);
        var chunk      = GetOrCreateChunk(chunkCoord);
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        chunk.Updated[chunk.Idx(lx, ly, lz)] = true;
    }

    // ════════════════════════════════════════════════════════════
    //  公開格子讀寫 API
    // ════════════════════════════════════════════════════════════

    public MaterialType GetTile(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return MaterialType.Air;
        var coord = WorldToChunk(x, y, z);
        if (!_chunks.TryGetValue(coord, out var chunk)) return MaterialType.Air;
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        return chunk.Cells[chunk.Idx(lx, ly, lz)].Type;
    }

    public TileCell GetCell(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return default;
        var coord = WorldToChunk(x, y, z);
        if (!_chunks.TryGetValue(coord, out var chunk)) return default;
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        return chunk.Cells[chunk.Idx(lx, ly, lz)];
    }

    public void SetTile(int x, int y, int z, MaterialType type, byte variant = 0)
    {
        if (!InBounds(x, y, z)) return;
        var coord  = WorldToChunk(x, y, z);
        var chunk  = GetOrCreateChunk(coord);
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        ref var cell = ref chunk.Cells[chunk.Idx(lx, ly, lz)];
        cell.Type    = type;
        cell.Variant = variant != 0 ? variant : (byte)_rng.Next(256);
        cell.Timer   = type switch
        {
            MaterialType.Fire  => (short)_rng.Next(30, 90),
            MaterialType.Steam => (short)_rng.Next(60, 120),
            _                  => (short)0,
        };
        chunk.MarkDirty(lx, ly, lz);
    }

    private void WriteCell(int x, int y, int z, TileCell cell)
    {
        var coord = WorldToChunk(x, y, z);
        var chunk = GetOrCreateChunk(coord);
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        chunk.Cells[chunk.Idx(lx, ly, lz)] = cell;
        chunk.MarkDirty(lx, ly, lz);
    }

    // ════════════════════════════════════════════════════════════
    //  爆炸 / Raycast / Snapshot
    // ════════════════════════════════════════════════════════════

    /// <summary>球形爆炸：dx²+dy²+dz² ≤ r²</summary>
    public void Explode(int cx, int cy, int cz, int radius)
    {
        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
            if (dx*dx + dy*dy + dz*dz <= r2)
                SetTile(cx + dx, cy + dy, cz + dz, MaterialType.Air);
        OnExplosion?.Invoke(new GridPos(cx, cy, cz), radius);
    }

    /// <summary>3D DDA 射線（Amanatides-Woo）</summary>
    public (GridPos Hit, int MatId, bool DidHit) Raycast(
        GridPos start, float dirX, float dirY, float dirZ, float maxDist)
    {
        float len = MathF.Sqrt(dirX*dirX + dirY*dirY + dirZ*dirZ);
        if (len < 1e-4f) return (start, 0, false);
        dirX /= len; dirY /= len; dirZ /= len;

        int tx = start.X, ty = start.Y, tz = start.Z;
        int stepX = dirX >= 0 ? 1 : -1;
        int stepY = dirY >= 0 ? 1 : -1;
        int stepZ = dirZ >= 0 ? 1 : -1;

        float dX = MathF.Abs(dirX) < 1e-4f ? float.MaxValue : 1f / MathF.Abs(dirX);
        float dY = MathF.Abs(dirY) < 1e-4f ? float.MaxValue : 1f / MathF.Abs(dirY);
        float dZ = MathF.Abs(dirZ) < 1e-4f ? float.MaxValue : 1f / MathF.Abs(dirZ);

        float sX = dirX >= 0 ? (tx + 1f - (start.X + 0.5f)) * dX : ((start.X + 0.5f) - tx) * dX;
        float sY = dirY >= 0 ? (ty + 1f - (start.Y + 0.5f)) * dY : ((start.Y + 0.5f) - ty) * dY;
        float sZ = dirZ >= 0 ? (tz + 1f - (start.Z + 0.5f)) * dZ : ((start.Z + 0.5f) - tz) * dZ;

        for (float dist = 0f; dist < maxDist;)
        {
            if (sX < sY && sX < sZ) { dist = sX; sX += dX; tx += stepX; }
            else if (sY < sZ)        { dist = sY; sY += dY; ty += stepY; }
            else                     { dist = sZ; sZ += dZ; tz += stepZ; }

            if (dist > maxDist || !InBounds(tx, ty, tz)) break;
            var mat = GetTile(tx, ty, tz);
            if (mat != MaterialType.Air)
                return (new GridPos(tx, ty, tz), (int)mat, true);
        }
        return (start, 0, false);
    }

    /// <summary>2D Raycast 向後相容包裝（Z=0 平面）</summary>
    public (GridPos Hit, int MatId, bool DidHit) Raycast(
        GridPos start, float dirX, float dirY, float maxDist) =>
        Raycast(start, dirX, dirY, 0f, maxDist);

    /// <summary>球形快照（鍵 = z*W*H + y*W + x）</summary>
    public TileWorldSnapshot SnapshotRegion(GridPos center, int radius)
    {
        var cells = new Dictionary<int, TileCell>();
        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            if (dx*dx + dy*dy + dz*dz > r2) continue;
            int x = center.X + dx, y = center.Y + dy, z = center.Z + dz;
            if (!InBounds(x, y, z)) continue;
            int key = z * Width * Height + y * Width + x;
            cells[key] = GetCell(x, y, z);
        }
        return new TileWorldSnapshot(center, radius, cells);
    }

    public void RestoreRegion(TileWorldSnapshot snap)
    {
        foreach (var (key, cell) in snap.Cells)
        {
            int z = key / (Width * Height);
            int rem = key % (Width * Height);
            int y = rem / Width, x = rem % Width;
            WriteCell(x, y, z, cell);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  IWorldInterface 實作
    // ════════════════════════════════════════════════════════════

    public WorldEntity? GetEntityAt(GridPos pos) =>
        _entities.FirstOrDefault(e => e.IsAlive && e.Position == pos);

    public MaterialType GetMaterialAt(GridPos pos) => GetTile(pos.X, pos.Y, pos.Z);

    public List<WorldEntity> GetEntitiesNear(GridPos pos, float radius) =>
        _entities.Where(e => e.IsAlive && e.Position.DistanceTo(pos) <= radius).ToList();

    public object? GetEntityProperty(WorldEntity entity, string property) => property switch
    {
        "hp"       => entity.Hp,
        "maxHp"    => entity.MaxHp,
        "faction"  => entity.Faction,
        "position" => entity.Position,
        _          => null,
    };

    public void DestroyTile(GridPos pos)
    {
        if (!InBounds(pos.X, pos.Y, pos.Z)) return;
        var mat = GetTile(pos.X, pos.Y, pos.Z);
        SetTile(pos.X, pos.Y, pos.Z, MaterialType.Air);
        OnTileDestroyed?.Invoke(pos, mat);
    }

    public void ApplyForce(WorldEntity entity, float dx, float dy)
    {
        var np = entity.Position + new GridPos(Math.Sign((int)dx), Math.Sign((int)dy));
        if (GetTile(np.X, np.Y, np.Z) == MaterialType.Air)
            entity.Position = np;
    }

    public void SpawnEffect(string type, GridPos pos, Dictionary<string, object?> parameters)
    {
        switch (type)
        {
            case "fire":
                int fr = parameters.TryGetValue("radius", out var fr_) ? Convert.ToInt32(fr_) : 1;
                for (int dy = -fr; dy <= fr; dy++)
                for (int dx = -fr; dx <= fr; dx++)
                for (int dz = -fr; dz <= fr; dz++)
                    if (GetTile(pos.X+dx, pos.Y+dy, pos.Z+dz) == MaterialType.Air)
                        SetFire(pos.X+dx, pos.Y+dy, pos.Z+dz);
                break;

            case "explosion":
                int er = parameters.TryGetValue("radius", out var er_) ? Convert.ToInt32(er_) : 3;
                Explode(pos.X, pos.Y, pos.Z, er);
                break;

            case "water":
                int wr = parameters.TryGetValue("radius", out var wr_) ? Convert.ToInt32(wr_) : 2;
                int wr2 = wr * wr;
                for (int dy = -wr; dy <= wr; dy++)
                for (int dx = -wr; dx <= wr; dx++)
                for (int dz = -wr; dz <= wr; dz++)
                    if (dx*dx+dy*dy+dz*dz <= wr2 && GetTile(pos.X+dx, pos.Y+dy, pos.Z+dz) == MaterialType.Air)
                        SetTile(pos.X+dx, pos.Y+dy, pos.Z+dz, MaterialType.Water);
                break;
        }
    }

    public void SetEntityProperty(WorldEntity entity, string property, object? value)
    {
        if (property != "hp") return;
        float old = entity.Hp;
        entity.Hp = Math.Max(0f, Convert.ToSingle(value));
        if (entity.Hp < old) OnEntityHit?.Invoke(entity, entity, old - entity.Hp);
        if (entity.Hp <= 0f && old > 0f) OnEntityDied?.Invoke(entity);
    }

    public WorldEntity? CreateEntity(string type, GridPos pos, Dictionary<string, object?> parameters)
    {
        float maxHp   = parameters.TryGetValue("maxHp",    out var hp) ? Convert.ToSingle(hp) : 50f;
        string faction = parameters.TryGetValue("faction", out var f)  ? f?.ToString() ?? "enemy" : "enemy";
        var e = new WorldEntity(_nextEntityId++, pos, maxHp, faction);
        _entities.Add(e);
        return e;
    }

    // ════════════════════════════════════════════════════════════
    //  邊界與 Chunk 工具
    // ════════════════════════════════════════════════════════════

    public bool InBounds(int x, int y, int z) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height && (uint)z < (uint)Depth;

    // Phase 2-B：2D 相容性 shim（SideScroll2D = Z=0 平面）
    public MaterialType TypeAt(int x, int y) => GetTile(x, y, 0);
    public bool InBoundsPublic(int x, int y) => InBounds(x, y, 0);
    public void Set(int x, int y, MaterialType type) => SetTile(x, y, 0, type);
    public void ApplyElementalImpact(int x, int y, ElementType elem) => ApplyElementalImpact(x, y, 0, elem);
    public void Explode(int cx, int cy, int r) => Explode(cx, cy, 0, r);

    public IReadOnlyDictionary<Vector3I, Chunk3D> ActiveChunks => _chunks;

    private Chunk3D GetOrCreateChunk(Vector3I coord)
    {
        if (!_chunks.TryGetValue(coord, out var chunk))
            _chunks[coord] = chunk = new Chunk3D(coord);
        return chunk;
    }

    private static Vector3I WorldToChunk(int x, int y, int z) =>
        new(FloorDiv(x, Chunk3D.Size),
            FloorDiv(y, Chunk3D.Size),
            FloorDiv(z, Chunk3D.Size));

    private static (int lx, int ly, int lz) WorldToLocal(int x, int y, int z) =>
        (PosMod(x, Chunk3D.Size),
         PosMod(y, Chunk3D.Size),
         PosMod(z, Chunk3D.Size));

    private static int FloorDiv(int a, int b) =>
        a >= 0 ? a / b : (a + 1) / b - 1;

    private static int PosMod(int a, int b) =>
        ((a % b) + b) % b;
}
