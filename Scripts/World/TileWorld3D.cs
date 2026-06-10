using Godot;
using SkillCreator.World.Materials;

namespace SkillCreator.World;

/// <summary>
/// Phase 0：純邏輯驗證用的 3D 細胞自動機世界，無渲染。
/// 以 16³ Chunk 字典管理空間，支援沙粒重力（Y+ 向下）。
/// Phase 2 接渲染時在 Tick 後呼叫 RebuildDirtyMeshes。
/// </summary>
public sealed class TileWorld3D
{
    public int Width  { get; }
    public int Height { get; }
    public int Depth  { get; }

    private readonly Dictionary<Vector3I, Chunk3D> _chunks = new();

    public TileWorld3D(int width, int height, int depth)
    {
        Width = width; Height = height; Depth = depth;
    }

    // ── 格子讀寫 ──────────────────────────────────────────────────────────

    public MaterialType GetTile(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return MaterialType.Air;
        var coord = WorldToChunk(x, y, z);
        if (!_chunks.TryGetValue(coord, out var chunk)) return MaterialType.Air;
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        return chunk.Cells[chunk.Idx(lx, ly, lz)].Type;
    }

    public void SetTile(int x, int y, int z, MaterialType type, byte variant = 0)
    {
        if (!InBounds(x, y, z)) return;
        var coord = WorldToChunk(x, y, z);
        var chunk = GetOrCreateChunk(coord);
        var (lx, ly, lz) = WorldToLocal(x, y, z);
        ref var cell = ref chunk.Cells[chunk.Idx(lx, ly, lz)];
        cell.Type    = type;
        cell.Variant = variant;
        chunk.MarkDirty(lx, ly, lz);
    }

    // ── CA：一步重力（沙粒向 Y+ 下落）───────────────────────────────────

    public void Tick()
    {
        // 由底往上掃（高 Y → 低 Y），使沙粒能在單次 Tick 內連鎖下落
        for (int y = Height - 2; y >= 0; y--)
        for (int z = 0; z < Depth;  z++)
        for (int x = 0; x < Width;  x++)
        {
            if (GetTile(x, y, z) != MaterialType.Sand) continue;
            if (GetTile(x, y + 1, z) == MaterialType.Air)
            {
                SetTile(x, y + 1, z, MaterialType.Sand);
                SetTile(x, y,     z, MaterialType.Air);
            }
        }
    }

    // ── 邊界與 Chunk 工具 ─────────────────────────────────────────────────

    public bool InBounds(int x, int y, int z) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height && (uint)z < (uint)Depth;

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

    // 支援負座標的整除與取模（Phase 1 無限世界需要）
    private static int FloorDiv(int a, int b) =>
        a >= 0 ? a / b : (a + 1) / b - 1;

    private static int PosMod(int a, int b) =>
        ((a % b) + b) % b;
}
