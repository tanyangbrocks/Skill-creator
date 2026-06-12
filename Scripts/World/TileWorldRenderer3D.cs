namespace SkillCreator.World;

using System.Threading.Tasks;
using Godot;
using SkillCreator.World.Materials;

/// <summary>
/// Phase 2-A：3D voxel 渲染器（ArrayMesh + Greedy Meshing）。
/// Phase 3 多執行緒優化：
///   • Greedy Meshing 在 Task.Run 工作執行緒完成（純 C# 陣列，不碰 Godot API）
///   • 主執行緒只負責 snapshot 快照 + 套用已完成 ArrayMesh → GPU
///   • 任務啟動前快照 chunk.Cells + 邊界 Air 狀態，不存在 race condition
///   • [ThreadStatic] maskBuf/mergedBuf：每個 worker 有獨立緩衝，無鎖
///   • 雙材質 pass：不透明 CullMode=Back + 透明 CullMode=Disabled+AlphaBlend
///   • LOD：超出 viewRadius+1 的 mesh 自動 Visible=false
/// </summary>
public partial class TileWorldRenderer3D : Node3D
{
    private TileWorld3D?       _world;
    private StandardMaterial3D _matOpaque      = null!;
    private StandardMaterial3D _matTransparent = null!;

    // 每 chunk 兩個 MeshInstance3D（不透明 O + 半透明 T）
    private readonly Dictionary<Vector3I, (MeshInstance3D O, MeshInstance3D T)> _meshes   = new();
    // 正在執行的任務（coord → Task）；僅主執行緒存取
    private readonly Dictionary<Vector3I, Task<ChunkTaskResult>>                 _inFlight = new();

    // 並行度：最多 ProcessorCount-1 個 worker，保留主執行緒 CPU，上限 8
    // 注意：用 System.Environment 避免與 Godot.Environment 衝突
    private static readonly int MaxConcurrent = Math.Clamp(System.Environment.ProcessorCount - 1, 2, 8);

    // ── 面方向常數 ────────────────────────────────────────────────────────────
    private static readonly (int na, int ns)[] s_faces =
        { (1,+1),(1,-1),(0,+1),(0,-1),(2,+1),(2,-1) };
    private static readonly (int ua, int va)[] s_perp  = { (1,2),(0,2),(0,1) };
    private static readonly int[]              s_hand  = { +1,-1,+1 };
    private static readonly Vector3[]          s_normals =
    {
        new(0,1,0), new(0,-1,0), new(1,0,0), new(-1,0,0), new(0,0,1), new(0,0,-1),
    };

    // ThreadStatic：每個執行緒獨立，消除競爭。注意不可用 readonly（static 初始值只跑一次）
    [ThreadStatic] private static (MaterialType t, byte v)[]? s_maskBuf;
    [ThreadStatic] private static bool[]?                      s_mergedBuf;

    private const int S = Chunk3D.Size; // 16

    // ── 任務結果型別（純 C# 資料，不含 Godot 物件）──────────────────────────
    private record struct MeshSurface(
        Vector3[] Verts, Vector3[] Norms, Color[] Colors, int[] Indices);

    private record struct ChunkTaskResult(
        Vector3I Coord, MeshSurface? Opaque, MeshSurface? Transp);

    // ── 公開 API ─────────────────────────────────────────────────────────────

    public void Initialize(TileWorld3D world)
    {
        _world = world;
        // 不透明材質：CullMode=Back（預設值），每個面只渲染一次
        _matOpaque = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        // 半透明材質（水/火/蒸汽）：雙面 + AlphaBlend
        _matTransparent = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode               = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    /// <summary>
    /// 每幀 Tick 後呼叫。
    /// Pass 0：收集已完成任務 → 套用 GPU mesh。
    /// Pass 1：LOD — 隱藏超出 (viewRadius+1) 的 chunk。
    /// Pass 2：為視距內的 dirty chunk 啟動新 Task.Run（最多 maxPerFrame 個/幀）。
    /// </summary>
    public void RebuildDirtyMeshes(
        int maxPerFrame   = 30,
        bool sideScroll2D = false,
        int viewCX = -1, int viewCY = -1, int viewCZ = -1, int viewRadius = -1)
    {
        if (_world == null) return;

        // ── Pass 0：收集完成任務 ──────────────────────────────────────────────
        var done = new List<Vector3I>();
        foreach (var (coord, task) in _inFlight)
        {
            if (!task.IsCompleted) continue;
            if (task.IsFaulted)
                GD.PrintErr($"[Renderer] chunk {coord} build failed: {task.Exception?.InnerException?.Message}");
            else
                ApplyTaskResult(task.Result);
            done.Add(coord);
        }
        foreach (var c in done) _inFlight.Remove(c);

        // ── Pass 1：LOD ───────────────────────────────────────────────────────
        if (viewRadius >= 0 && viewCX >= 0)
        {
            int hideR = viewRadius + 1;
            foreach (var (coord, pair) in _meshes)
            {
                bool inRange =
                    Math.Abs(coord.X - viewCX) <= hideR &&
                    Math.Abs(coord.Y - viewCY) <= hideR &&
                    (viewCZ < 0 || Math.Abs(coord.Z - viewCZ) <= hideR);
                if (!inRange) { pair.O.Visible = false; pair.T.Visible = false; }
            }
        }

        // ── Pass 2：啟動新任務 ────────────────────────────────────────────────
        int launched = 0;
        foreach (var (coord, chunk) in _world.ActiveChunks)
        {
            if (!chunk.MeshNeedsRebuild) continue;
            if (sideScroll2D && coord.Z != 0) continue;  // 保留 dirty flag，切換 FP 模式時能重建
            if (viewRadius >= 0 && viewCX >= 0)
            {
                int dx = Math.Abs(coord.X - viewCX);
                int dy = Math.Abs(coord.Y - viewCY);
                int dz = viewCZ >= 0 ? Math.Abs(coord.Z - viewCZ) : 0;
                if (dx > viewRadius || dy > viewRadius || dz > viewRadius) continue;
            }
            if (_inFlight.ContainsKey(coord)) continue;   // 已在佇列中
            if (_inFlight.Count >= MaxConcurrent) break;  // 達到並行上限

            // ★ 主執行緒快照，任務執行緒只讀快照，不碰 live world
            var cells     = chunk.Cells.ToArray();          // TileCell 值型別深拷貝
            var borderAir = PrecomputeBorderAir(coord);     // 邊界鄰居 Air 狀態
            var captCoord = coord;
            chunk.MeshNeedsRebuild = false; // 清旗（CA 若再 dirty，會重設為 true）
            _inFlight[coord] = Task.Run(() => BuildMeshDataOffThread(captCoord, cells, borderAir));

            if (++launched >= maxPerFrame) break;
        }
    }

    // ── 主執行緒：套用任務結果 ────────────────────────────────────────────────

    private void ApplyTaskResult(ChunkTaskResult r)
    {
        if (!_meshes.TryGetValue(r.Coord, out var pair))
        {
            var miO = new MeshInstance3D { MaterialOverride = _matOpaque };
            var miT = new MeshInstance3D { MaterialOverride = _matTransparent };
            AddChild(miO); AddChild(miT);
            pair = (miO, miT);
            _meshes[r.Coord] = pair;
            if (_world!.ActiveChunks.TryGetValue(r.Coord, out var ch))
                ch.MeshNode = miO;
        }

        float T = TileWorldConstants.TileSize;
        var pos = new Vector3(r.Coord.X * S * T, r.Coord.Y * S * T, r.Coord.Z * S * T);
        pair.O.Position = pos;
        pair.T.Position = pos;

        if (r.Opaque != null) { pair.O.Mesh = MakeMesh(r.Opaque.Value); pair.O.Visible = true; }
        else pair.O.Visible = false;

        if (r.Transp != null) { pair.T.Mesh = MakeMesh(r.Transp.Value); pair.T.Visible = true; }
        else pair.T.Visible = false;
    }

    // ArrayMesh 只在主執行緒建立（Godot Rendering Server 限制）
    // 用 SurfaceTool 避免直接使用 PackedVector3Array/PackedColorArray/PackedInt32Array
    // 的泛型建構子（GodotSharp 4.6.x 無法從 C# 陣列直接建立）。
    // 頂點資料（最昂貴）已在 worker 執行緒完成；SurfaceTool 逐頂點呼叫的開銷可接受。
    private static ArrayMesh MakeMesh(MeshSurface s)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (int idx in s.Indices)
        {
            st.SetColor(s.Colors[idx]);
            st.SetNormal(s.Norms[idx]);
            st.AddVertex(s.Verts[idx]);
        }
        return st.Commit()!;
    }

    // ── 主執行緒：預計算邊界 Air 狀態（供 off-thread 使用）─────────────────

    private bool[] PrecomputeBorderAir(Vector3I coord)
    {
        // 6 faces × S×S：borderAir[fi*S*S + i*S + j] = 鄰居是否為 Air
        var result = new bool[6 * S * S];
        int wx0 = coord.X * S, wy0 = coord.Y * S, wz0 = coord.Z * S;
        for (int fi = 0; fi < s_faces.Length; fi++)
        {
            var (na, ns) = s_faces[fi];
            var (ua, va) = s_perp[na];
            int d = ns > 0 ? S - 1 : 0; // chunk 在此面方向的邊界層
            for (int i = 0; i < S; i++)
            for (int j = 0; j < S; j++)
            {
                var (lx, ly, lz) = L3(na, ua, va, d, i, j);
                // 鄰居的世界座標（緊鄰 chunk 外側）
                int wx = wx0 + lx + (na == 0 ? ns : 0);
                int wy = wy0 + ly + (na == 1 ? ns : 0);
                int wz = wz0 + lz + (na == 2 ? ns : 0);
                result[fi * S * S + i * S + j] = _world!.GetTile(wx, wy, wz) == MaterialType.Air;
            }
        }
        return result;
    }

    // ── 工作執行緒：Greedy Meshing（完全無 Godot API，可安全 off-thread）────

    private static ChunkTaskResult BuildMeshDataOffThread(
        Vector3I coord, TileCell[] cells, bool[] borderAir)
    {
        List<Vector3>? oV = null, oN = null, tV = null, tN = null;
        List<Color>?   oC = null, tC = null;
        List<int>?     oI = null, tI = null;

        // ThreadStatic 緩衝：每個執行緒各自持有，不共用，不需鎖
        var maskBuf   = s_maskBuf   ??= new (MaterialType, byte)[S * S];
        var mergedBuf = s_mergedBuf ??= new bool[S * S];

        for (int fi = 0; fi < s_faces.Length; fi++)
        {
            var (na, ns) = s_faces[fi];
            var (ua, va) = s_perp[na];
            var  norm    = s_normals[fi];
            bool front   = (ns > 0) == (s_hand[na] > 0);

            for (int d = 0; d < S; d++)
            {
                // 是否為 chunk 邊界層（需查 borderAir）
                bool border = ns > 0 ? d == S - 1 : d == 0;

                // ── 建立 mask ────────────────────────────────────────────────
                for (int i = 0; i < S; i++)
                for (int j = 0; j < S; j++)
                {
                    maskBuf[i * S + j] = (MaterialType.Air, 0);
                    var (lx, ly, lz) = L3(na, ua, va, d, i, j);
                    var cell = cells[lz * S * S + ly * S + lx];
                    if (cell.Type == MaterialType.Air) continue;

                    bool nAir;
                    if (border)
                    {
                        nAir = borderAir[fi * S * S + i * S + j];
                    }
                    else
                    {
                        var (nx, ny, nz) = L3(na, ua, va, d + ns, i, j);
                        nAir = cells[nz * S * S + ny * S + nx].Type == MaterialType.Air;
                    }
                    if (nAir) maskBuf[i * S + j] = (cell.Type, cell.Variant);
                }

                // ── Greedy 合併 ──────────────────────────────────────────────
                Array.Clear(mergedBuf, 0, S * S);
                int df = d + (ns > 0 ? 1 : 0);

                for (int i = 0; i < S; i++)
                for (int j = 0; j < S; j++)
                {
                    var fid = maskBuf[i * S + j];
                    if (mergedBuf[i * S + j] || fid.t == MaterialType.Air) continue;

                    int w = 1;
                    while (j + w < S && !mergedBuf[i * S + j + w] && maskBuf[i * S + j + w] == fid) w++;
                    int h = 1, ok = 1;
                    while (ok == 1 && i + h < S)
                    {
                        for (int k = j; k < j + w; k++)
                            if (mergedBuf[(i+h)*S+k] || maskBuf[(i+h)*S+k] != fid) { ok = 0; break; }
                        if (ok == 1) h++;
                    }
                    for (int di = 0; di < h; di++)
                    for (int dj = 0; dj < w; dj++)
                        mergedBuf[(i+di)*S + j+dj] = true;

                    var col    = MaterialRegistry.GetColor(fid.t, fid.v);
                    bool transp = MaterialRegistry.Get(fid.t).IsTransparent;

                    // 選擇對應的 List（lazy 建立）
                    if (transp) { tV ??= new(); tN ??= new(); tC ??= new(); tI ??= new(); }
                    else        { oV ??= new(); oN ??= new(); oC ??= new(); oI ??= new(); }

                    var V = transp ? tV! : oV!;
                    var N = transp ? tN! : oN!;
                    var C = transp ? tC! : oC!;
                    var I = transp ? tI! : oI!;

                    // 4 頂點 + 6 索引（indexed quad）
                    int b = V.Count;
                    V.Add(V3(L3(na,ua,va,df,i,  j  )));
                    V.Add(V3(L3(na,ua,va,df,i+h,j  )));
                    V.Add(V3(L3(na,ua,va,df,i+h,j+w)));
                    V.Add(V3(L3(na,ua,va,df,i,  j+w)));
                    N.Add(norm); N.Add(norm); N.Add(norm); N.Add(norm);
                    C.Add(col);  C.Add(col);  C.Add(col);  C.Add(col);
                    if (front)
                    { I.Add(b); I.Add(b+1); I.Add(b+2); I.Add(b); I.Add(b+2); I.Add(b+3); }
                    else
                    { I.Add(b); I.Add(b+3); I.Add(b+2); I.Add(b); I.Add(b+2); I.Add(b+1); }
                }
            }
        }

        MeshSurface? opaque = oV != null
            ? new(oV.ToArray(), oN!.ToArray(), oC!.ToArray(), oI!.ToArray()) : null;
        MeshSurface? transp2 = tV != null
            ? new(tV.ToArray(), tN!.ToArray(), tC!.ToArray(), tI!.ToArray()) : null;

        return new ChunkTaskResult(coord, opaque, transp2);
    }

    // ── 靜態輔助 ─────────────────────────────────────────────────────────────

    private static (int, int, int) L3(int na, int ua, int va, int d, int i, int j) =>
        (na == 0 ? d : ua == 0 ? i : j,
         na == 1 ? d : ua == 1 ? i : j,
         na == 2 ? d : ua == 2 ? i : j);

    private static Vector3 V3((int x, int y, int z) p) =>
        new(p.x * TileWorldConstants.TileSize,
            p.y * TileWorldConstants.TileSize,
            p.z * TileWorldConstants.TileSize);
}
