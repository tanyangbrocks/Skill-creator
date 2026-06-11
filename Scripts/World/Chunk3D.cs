using System.Runtime.CompilerServices;
using Godot;

namespace SkillCreator.World;

public sealed class Chunk3D
{
    public const int Size        = 16;
    private const int SizeSquared = Size * Size;
    private const int SizeCubed   = Size * Size * Size;

    public Vector3I ChunkCoord { get; }
    public TileCell[] Cells   { get; } = new TileCell[SizeCubed];
    public bool[]     Updated { get; } = new bool[SizeCubed];   // 同幀防重複更新

    // Dirty AABB — 只重建有變動的子區域
    public bool IsDirty          { get; set; }
    public bool MeshNeedsRebuild { get; set; }
    public int  DirtyMinX, DirtyMinY, DirtyMinZ;
    public int  DirtyMaxX, DirtyMaxY, DirtyMaxZ;

    // Phase 2+ 補：public MeshInstance3D? MeshNode { get; set; }

    public Chunk3D(Vector3I chunkCoord)
    {
        ChunkCoord = chunkCoord;
        ResetDirtyBounds();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Idx(int x, int y, int z) => z * SizeSquared + y * Size + x;

    public void MarkDirty(int x, int y, int z)
    {
        IsDirty      = true;
        DirtyMinX    = Math.Min(DirtyMinX, x); DirtyMaxX = Math.Max(DirtyMaxX, x);
        DirtyMinY    = Math.Min(DirtyMinY, y); DirtyMaxY = Math.Max(DirtyMaxY, y);
        DirtyMinZ    = Math.Min(DirtyMinZ, z); DirtyMaxZ = Math.Max(DirtyMaxZ, z);
    }

    public void ClearDirty()
    {
        IsDirty = false;
        MeshNeedsRebuild = false;
        ResetDirtyBounds();
    }

    public void ClearUpdated() => Array.Clear(Updated, 0, SizeCubed);

    private void ResetDirtyBounds()
    {
        DirtyMinX = DirtyMinY = DirtyMinZ = Size;
        DirtyMaxX = DirtyMaxY = DirtyMaxZ = -1;
    }
}
