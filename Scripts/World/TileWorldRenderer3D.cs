namespace SkillCreator.World;

using Godot;
using SkillCreator.World.Materials;

/// <summary>
/// Phase 2-A：3D voxel 渲染器（ArrayMesh + Greedy Meshing）。
/// 每個 Chunk 一個 MeshInstance3D；只重建 MeshNeedsRebuild=true 的 Chunk。
/// 呼叫順序：每幀在 TileWorld3D.Tick() 後呼叫 RebuildDirtyMeshes()。
/// </summary>
public partial class TileWorldRenderer3D : Node3D
{
    private TileWorld3D? _world;
    private StandardMaterial3D _mat = null!;
    private readonly Dictionary<Vector3I, MeshInstance3D> _meshes = new();

    // 面方向設定：(normalAxis, normalSign)
    // normalAxis: 0=X, 1=Y, 2=Z；normalSign: +1 正向, -1 負向
    private static readonly (int na, int ns)[] s_faces =
    {
        (1, +1), (1, -1),   // +Y, -Y
        (0, +1), (0, -1),   // +X, -X
        (2, +1), (2, -1),   // +Z, -Z
    };

    // 每個 normalAxis 的兩個切面軸：(ua, va)
    // na=0(X): ua=1(Y), va=2(Z)  →  ua×va = Y×Z = +X  → 正向手性
    // na=1(Y): ua=0(X), va=2(Z)  →  ua×va = X×Z = -Y  → 負向手性
    // na=2(Z): ua=0(X), va=1(Y)  →  ua×va = X×Y = +Z  → 正向手性
    private static readonly (int ua, int va)[] s_perp     = { (1, 2), (0, 2), (0, 1) };
    private static readonly int[]              s_hand     = { +1, -1, +1 }; // 手性符號

    private static readonly Vector3[] s_normals =
    {
        new( 0,  1,  0), new( 0, -1,  0),
        new( 1,  0,  0), new(-1,  0,  0),
        new( 0,  0,  1), new( 0,  0, -1),
    };

    // ── 公開 API ─────────────────────────────────────────────────────────────

    public void Initialize(TileWorld3D world)
    {
        _world = world;
        _mat   = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
    }

    /// <summary>
    /// 每幀 Tick 後呼叫；重建 MeshNeedsRebuild=true 的 Chunk。
    /// maxPerFrame：每幀最多重建幾個，避免首幀 hang（預設 30）。
    /// sideScroll2D：只重建 chunk Z=0 的前排，其餘直接標為 clean 跳過。
    /// </summary>
    public void RebuildDirtyMeshes(int maxPerFrame = 30, bool sideScroll2D = false)
    {
        if (_world == null) return;
        int rebuilt = 0;
        foreach (var (coord, chunk) in _world.ActiveChunks)
        {
            if (!chunk.MeshNeedsRebuild) continue;
            // SideScroll2D：只有 Z=0 那排 Chunk 是可見的，其餘直接跳過
            if (sideScroll2D && coord.Z != 0)
            {
                chunk.MeshNeedsRebuild = false;
                continue;
            }
            RebuildChunk(coord, chunk);
            chunk.MeshNeedsRebuild = false;
            if (++rebuilt >= maxPerFrame) break;
        }
    }

    // ── Chunk 重建 ───────────────────────────────────────────────────────────

    private void RebuildChunk(Vector3I coord, Chunk3D chunk)
    {
        var  st     = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        bool filled = BuildGreedyMesh(st, coord, chunk);

        if (!_meshes.TryGetValue(coord, out var mi))
        {
            mi = new MeshInstance3D { MaterialOverride = _mat };
            AddChild(mi);
            _meshes[coord]    = mi;
            chunk.MeshNode    = mi;
        }

        if (filled)
        {
            st.Index();
            mi.Mesh    = st.Commit();
            mi.Visible = true;
        }
        else
        {
            mi.Visible = false;
        }

        mi.Position = new Vector3(
            coord.X * Chunk3D.Size,
            coord.Y * Chunk3D.Size,
            coord.Z * Chunk3D.Size);
    }

    // ── Greedy Meshing ───────────────────────────────────────────────────────

    private bool BuildGreedyMesh(SurfaceTool st, Vector3I chunkCoord, Chunk3D chunk)
    {
        const int S  = Chunk3D.Size;
        int wx0 = chunkCoord.X * S;
        int wy0 = chunkCoord.Y * S;
        int wz0 = chunkCoord.Z * S;

        var  mask   = new (MaterialType t, byte v)[S, S];
        var  merged = new bool[S, S];
        bool any    = false;

        for (int fi = 0; fi < s_faces.Length; fi++)
        {
            var (na, ns) = s_faces[fi];
            var (ua, va) = s_perp[na];
            var  norm    = s_normals[fi];
            // frontFace=true 使用正序三角形，false 使用逆序
            bool front   = (ns > 0) == (s_hand[na] > 0);

            for (int d = 0; d < S; d++)
            {
                // ── 建立可見面遮罩 ─────────────────────────────────────────
                for (int i = 0; i < S; i++)
                for (int j = 0; j < S; j++)
                {
                    mask[i, j] = (MaterialType.Air, 0);

                    var (lx, ly, lz) = L3(na, ua, va, d, i, j);
                    var cell = chunk.Cells[chunk.Idx(lx, ly, lz)];
                    if (cell.Type == MaterialType.Air) continue;

                    // 鄰近格（法線方向）
                    var (nx, ny, nz) = L3(na, ua, va, d + ns, i, j);
                    bool nAir;
                    if ((uint)nx >= S || (uint)ny >= S || (uint)nz >= S)
                    {
                        // 跨 Chunk 或世界邊界：詢問 TileWorld3D
                        int wx = wx0 + lx + (na == 0 ? ns : 0);
                        int wy = wy0 + ly + (na == 1 ? ns : 0);
                        int wz = wz0 + lz + (na == 2 ? ns : 0);
                        nAir = _world!.GetTile(wx, wy, wz) == MaterialType.Air;
                    }
                    else
                    {
                        nAir = chunk.Cells[chunk.Idx(nx, ny, nz)].Type == MaterialType.Air;
                    }

                    if (nAir) mask[i, j] = (cell.Type, cell.Variant);
                }

                // ── Greedy 合併 ────────────────────────────────────────────
                Array.Clear(merged);
                int df = d + (ns > 0 ? 1 : 0); // 面在 voxel 的哪一側

                for (int i = 0; i < S; i++)
                for (int j = 0; j < S; j++)
                {
                    if (merged[i, j] || mask[i, j].t == MaterialType.Air) continue;
                    var fid = mask[i, j];

                    // 向 j 方向擴展寬度
                    int w = 1;
                    while (j + w < S && !merged[i, j + w] && mask[i, j + w] == fid) w++;

                    // 向 i 方向擴展高度
                    int h = 1, ok = 1;
                    while (ok == 1 && i + h < S)
                    {
                        for (int k = j; k < j + w; k++)
                            if (merged[i + h, k] || mask[i + h, k] != fid) { ok = 0; break; }
                        if (ok == 1) h++;
                    }

                    // 標記已合併
                    for (int di = 0; di < h; di++)
                    for (int dj = 0; dj < w; dj++)
                        merged[i + di, j + dj] = true;

                    var col = MaterialRegistry.GetColor(fid.t, fid.v);
                    EmitQuad(st, na, ua, va, df, i, j, h, w, norm, col, front);
                    any = true;
                }
            }
        }
        return any;
    }

    // ── 頂點發射 ─────────────────────────────────────────────────────────────

    private static void EmitQuad(SurfaceTool st,
        int na, int ua, int va, int df, int i0, int j0, int h, int w,
        Vector3 norm, Color col, bool front)
    {
        var v0 = V3(L3(na, ua, va, df, i0,     j0    ));
        var v1 = V3(L3(na, ua, va, df, i0 + h, j0    ));
        var v2 = V3(L3(na, ua, va, df, i0 + h, j0 + w));
        var v3 = V3(L3(na, ua, va, df, i0,     j0 + w));

        // front=true:  v0,v1,v2 + v0,v2,v3（法線 = (v1-v0)×(v2-v0) 為正方向）
        // front=false: v0,v3,v2 + v0,v2,v1（反序，法線為負方向）
        if (front)
        {
            Vtx(st, v0, norm, col); Vtx(st, v1, norm, col); Vtx(st, v2, norm, col);
            Vtx(st, v0, norm, col); Vtx(st, v2, norm, col); Vtx(st, v3, norm, col);
        }
        else
        {
            Vtx(st, v0, norm, col); Vtx(st, v3, norm, col); Vtx(st, v2, norm, col);
            Vtx(st, v0, norm, col); Vtx(st, v2, norm, col); Vtx(st, v1, norm, col);
        }
    }

    // ── 靜態輔助 ─────────────────────────────────────────────────────────────

    // (normalAxis, uAxis, vAxis, d, i, j) → (lx, ly, lz)，零配置版本
    private static (int, int, int) L3(int na, int ua, int va, int d, int i, int j) =>
        (na == 0 ? d : ua == 0 ? i : j,
         na == 1 ? d : ua == 1 ? i : j,
         na == 2 ? d : ua == 2 ? i : j);

    private static Vector3 V3((int x, int y, int z) p) => new(p.x, p.y, p.z);

    private static void Vtx(SurfaceTool st, Vector3 pos, Vector3 norm, Color col)
    {
        st.SetColor(col);
        st.SetNormal(norm);
        st.AddVertex(pos);
    }
}
