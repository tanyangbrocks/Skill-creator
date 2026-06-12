using System;
using System.Runtime.InteropServices;
using Godot;
using Godot.Collections;
using SkillCreator.World.Materials;

namespace SkillCreator.World;

/// <summary>
/// Phase 3 渲染優化 #2：GPU Compute Shader CA 模擬器。
/// 把 TileWorld3D 的 Powder / Liquid 重力 CA 移至 GPU（Margolus Block CA）。
/// Gas（火/蒸汽）、Static（木燃燒）、元素反應仍在 CPU。
/// </summary>
public sealed class CaGpuSimulator : IDisposable
{
    // ── 參數 ──────────────────────────────────────────────────────────────
    /// <summary>GPU 主動模擬區域的大小（世界格數）。</summary>
    public int AW { get; private set; }
    public int AH { get; private set; }
    public int AD { get; private set; }

    public bool IsAvailable => _rd != null && _buffer.IsValid;

    // ── GPU 資源 ──────────────────────────────────────────────────────────
    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _buffer;
    private Rid _uniformSet;
    private uint _bufferByteSize;

    // ── CPU staging 緩衝 ──────────────────────────────────────────────────
    private uint[]?  _staging;
    private byte[]?  _stagingBytes;   // pre-allocated; avoids per-frame ToArray GC

    // ── Push constant 大小（bytes）───────────────────────────────────────
    // struct Params { int W, H, D, phase; uint rng; int p0, p1, p2; } = 32 bytes
    private const uint PushSize = 32;

    // ── Cell bit layout ──────────────────────────────────────────────────
    //   bits  0- 7 : MaterialType (byte)
    //   bits  8- 9 : physCompact  (0=static/empty, 1=powder, 2=liquid)
    //   bits 10-13 : density4     (0-15, scaled from MaterialData.Density×1.5)
    //   bit   14   : dirty flag   (shader 在格子移動時設置)
    //   bits 15    : 保留
    //   bits 16-23 : Timer        (由 CPU 維護，GPU 搬運時原封不動)
    //   bits 24-30 : Variant      (同上)
    //   bit  31    : 保留
    private const uint DirtyBit = 1u << 14;

    // ── 每材質的 phys 位元預計算表 ──────────────────────────────────────
    private static readonly uint[] _physBits;

    static CaGpuSimulator()
    {
        var mats = Enum.GetValues<MaterialType>();
        _physBits = new uint[mats.Length];
        foreach (MaterialType mat in mats)
        {
            var d = MaterialRegistry.Get(mat);
            uint p = d.Physics switch {
                PhysicsCategory.Powder => 1u,
                PhysicsCategory.Liquid => 2u,
                _                     => 0u,
            };
            uint den = (uint)Math.Clamp((int)(d.Density * 1.5f), 0, 15);
            _physBits[(int)mat] = (p << 8) | (den << 10);
        }
    }

    // ── Cell packing ──────────────────────────────────────────────────────

    private static uint Pack(TileCell c) =>
        (uint)c.Type
        | _physBits[(int)c.Type]
        | ((uint)(byte)Math.Clamp((int)c.Timer, 0, 255) << 16)
        | ((uint)c.Variant << 24);

    private static TileCell Unpack(uint v) => new()
    {
        Type    = (MaterialType)(v & 0xFF),
        Timer   = (short)((v >> 16) & 0xFF),
        Variant = (byte)(v >> 24),
    };

    // ════════════════════════════════════════════════════════════
    //  初始化
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 初始化 GPU 資源。失敗時 IsAvailable = false，Tick 自動回退 CPU。
    /// </summary>
    /// <param name="aw">模擬區域寬（X，世界格）</param>
    /// <param name="ah">模擬區域高（Y，世界格）</param>
    /// <param name="ad">模擬區域深（Z，世界格）</param>
    public void Initialize(int aw, int ah, int ad)
    {
        AW = aw; AH = ah; AD = ad;

        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null)
        {
            GD.PrintErr("[CaGpu] RenderingDevice 不可用（非 Vulkan 後端？）");
            return;
        }

        // ── 編譯 Compute Shader ──────────────────────────────
        var src = new RDShaderSource { SourceCompute = ShaderGlsl };
        var spirv = _rd.ShaderCompileSpirVFromSource(src);
        if (spirv == null || spirv.CompileErrorCompute != string.Empty)
        {
            GD.PrintErr("[CaGpu] Shader 編譯失敗：", spirv?.CompileErrorCompute ?? "null");
            _rd = null;
            return;
        }
        _shader   = _rd.ShaderCreateFromSpirV(spirv);
        _pipeline = _rd.ComputePipelineCreate(_shader);

        // ── 建立 Storage Buffer ──────────────────────────────
        _bufferByteSize = (uint)(aw * ah * ad) * 4u;
        _buffer = _rd.StorageBufferCreate(_bufferByteSize, new byte[_bufferByteSize]);

        // ── 建立 Uniform Set ─────────────────────────────────
        var u = new RDUniform();
        u.UniformType = RenderingDevice.UniformType.StorageBuffer;
        u.Binding     = 0;
        u.AddId(_buffer);
        _uniformSet = _rd.UniformSetCreate(new Array<RDUniform> { u }, _shader, 0);

        _staging      = new uint[aw * ah * ad];
        _stagingBytes = new byte[aw * ah * ad * sizeof(uint)];
        GD.Print($"[CaGpu] 初始化完成：{aw}×{ah}×{ad} 格，buffer={_bufferByteSize / 1024} KB");
    }

    // ════════════════════════════════════════════════════════════
    //  Upload  CPU → GPU
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 把 TileWorld3D 的主動區域上傳到 GPU buffer。
    /// ox/oy/oz = 主動區域在世界座標的起點。
    /// </summary>
    public void Upload(TileWorld3D world, int ox, int oy, int oz)
    {
        if (_rd == null || _staging == null || _stagingBytes == null) return;

        for (int z = 0; z < AD; z++)
        for (int y = 0; y < AH; y++)
        for (int x = 0; x < AW; x++)
            _staging[z * AH * AW + y * AW + x] = Pack(world.GetCell(ox + x, oy + y, oz + z));

        Buffer.BlockCopy(_staging, 0, _stagingBytes, 0, _stagingBytes.Length);
        _rd.BufferUpdate(_buffer, 0, _bufferByteSize, _stagingBytes);
    }

    // ════════════════════════════════════════════════════════════
    //  Simulate  GPU Dispatch
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 執行 2 Phase Margolus Block CA。rngSeed 供 shader 做方向亂數。
    /// </summary>
    public void Simulate(uint rngSeed)
    {
        if (_rd == null) return;

        // dispatch groups = ceil( ceil(A/2) / 4 )
        uint gx = ((uint)((AW + 1) / 2) + 3) / 4;
        uint gy = ((uint)((AH + 1) / 2) + 3) / 4;
        uint gz = ((uint)((AD + 1) / 2) + 3) / 4;

        var pushData = new byte[PushSize];
        BitConverter.GetBytes(AW).CopyTo(pushData,  0);
        BitConverter.GetBytes(AH).CopyTo(pushData,  4);
        BitConverter.GetBytes(AD).CopyTo(pushData,  8);

        for (int phase = 0; phase < 2; phase++)
        {
            BitConverter.GetBytes(phase).CopyTo(pushData, 12);
            BitConverter.GetBytes(rngSeed ^ (uint)(phase * 0xDEAD_BEEF)).CopyTo(pushData, 16);
            // bytes 20-31 = 0 (padding)

            var list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
            _rd.ComputeListSetPushConstant(list, pushData, PushSize);
            _rd.ComputeListDispatch(list, gx, gy, gz);
            _rd.ComputeListEnd();
            _rd.Submit();
            _rd.Sync();
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Download  GPU → CPU
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 讀回 GPU 結果，把有 dirty flag 的格子寫回 TileWorld3D。
    /// 回傳變動格數。
    /// </summary>
    public int Download(TileWorld3D world, int ox, int oy, int oz)
    {
        if (_rd == null || _staging == null) return 0;

        var rawBytes = _rd.BufferGetData(_buffer);
        MemoryMarshal.Cast<byte, uint>(rawBytes).CopyTo(_staging);

        int changed = 0;
        for (int z = 0; z < AD; z++)
        for (int y = 0; y < AH; y++)
        for (int x = 0; x < AW; x++)
        {
            uint packed = _staging[z * AH * AW + y * AW + x];
            if ((packed & DirtyBit) == 0) continue;
            world.SetCellFromGpu(ox + x, oy + y, oz + z, Unpack(packed));
            changed++;
        }
        return changed;
    }

    // ════════════════════════════════════════════════════════════
    //  Dispose
    // ════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_rd == null) return;
        if (_uniformSet.IsValid) _rd.FreeRid(_uniformSet);
        if (_buffer.IsValid)    _rd.FreeRid(_buffer);
        if (_pipeline.IsValid)  _rd.FreeRid(_pipeline);
        if (_shader.IsValid)    _rd.FreeRid(_shader);
        _rd           = null;
        _staging      = null;
        _stagingBytes = null;
    }

    // ════════════════════════════════════════════════════════════
    //  GLSL Compute Shader（內嵌）
    // ════════════════════════════════════════════════════════════

    // 注：移除 Godot 特定的 #[compute] 前綴，SourceCompute 屬性已隱含是 compute stage。
    private const string ShaderGlsl = """
#version 450

// 每個 invocation 負責一個 2×2×2 Margolus block。
// 每個 workgroup = 4×4×4 invocations = 64 blocks = 8×8×8 格。
layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

// ── Push constants ─────────────────────────────────────────────────────────
layout(push_constant) uniform Params {
    int W, H, D;   // 主動區域尺寸（格）
    int phase;     // 0 或 1（Margolus 偏移）
    uint rng;      // per-frame 亂數種子
    int p0, p1, p2; // padding
} pc;

// ── World buffer ───────────────────────────────────────────────────────────
// bits  0- 7 : MaterialType
// bits  8- 9 : physCompact (0=static/empty, 1=powder, 2=liquid)
// bits 10-13 : density4 (0-15)
// bit  14    : dirty flag
// bits 16-23 : Timer（GPU 不修改，交換時整體搬運）
// bits 24-30 : Variant（同上）
layout(set = 0, binding = 0, std430) buffer World {
    uint cells[];
};

// ── Helper functions ───────────────────────────────────────────────────────

uint cellMat(uint c)   { return c & 0xFFu; }
uint cellPhys(uint c)  { return (c >> 8) & 0x3u; }   // 0=靜 1=粉末 2=液體
uint cellDens(uint c)  { return (c >> 10) & 0xFu; }

bool isEmpty(uint c)   { return cellMat(c) == 0u; }
bool isPowder(uint c)  { return cellPhys(c) == 1u; }
bool isLiquid(uint c)  { return cellPhys(c) == 2u; }
bool isMobile(uint c)  { uint p = cellPhys(c); return p == 1u || p == 2u; }

// mover 能否取代 target？
bool canDisplace(uint mover, uint target) {
    if (isEmpty(target)) return isMobile(mover);
    if (isLiquid(mover) && isLiquid(target)) return cellDens(mover) > cellDens(target);
    return false;
}

int wIdx(int x, int y, int z) {
    return z * pc.H * pc.W + y * pc.W + x;
}

bool inBounds(int x, int y, int z) {
    return x >= 0 && x < pc.W && y >= 0 && y < pc.H && z >= 0 && z < pc.D;
}

// OOB 視為不透明靜態（matType=1, physCompact=0 → 不可移動，不可被取代）
const uint SOLID_CELL = 1u;

uint readCell(int x, int y, int z) {
    return inBounds(x, y, z) ? cells[wIdx(x, y, z)] : SOLID_CELL;
}

// ── main ───────────────────────────────────────────────────────────────────

void main() {
    ivec3 gid = ivec3(gl_GlobalInvocationID);

    // Block 起點（世界格座標）
    int bx = gid.x * 2 + pc.phase;
    int by = gid.y * 2 + pc.phase;
    int bz = gid.z * 2 + pc.phase;

    if (bx >= pc.W || by >= pc.H || bz >= pc.D) return;

    // 讀取 2×2×2 block 的 8 個格子到本地陣列
    // 索引: c[lx][ly][lz]，ly=0=頂（低 Y 索引），ly=1=底（高 Y，重力方向 Y+）
    uint c[2][2][2];
    uint orig[2][2][2];
    for (int lx = 0; lx < 2; lx++)
    for (int ly = 0; ly < 2; ly++)
    for (int lz = 0; lz < 2; lz++) {
        uint v = readCell(bx+lx, by+ly, bz+lz) & ~(1u << 14); // 清除舊 dirty
        c[lx][ly][lz]    = v;
        orig[lx][ly][lz] = v;
    }

    // ── Per-block 亂數：決定 XZ 處理順序 ─────────────────────────────────
    uint r = uint(bx * 1619 + by * 31337 + bz * 6473) ^ pc.rng;

    // Fisher-Yates shuffle，4 個 XZ 位置 (0,0)(1,0)(0,1)(1,1)
    int order[4] = int[4](0, 1, 2, 3);
    {
        int t;
        int i0 = int(r & 3u); r >>= 2;
        if (i0 > 0) { t = order[0]; order[0] = order[i0]; order[i0] = t; }
        int i1 = 1 + int(r & 1u); r >>= 1;
        if (i1 != 1) { t = order[1]; order[1] = order[i1]; order[i1] = t; }
        if ((r & 1u) != 0u) { t = order[2]; order[2] = order[3]; order[3] = t; }
        r >>= 1;
    }

    // ── Pass 1：重力（頂層 ly=0 → 底層 ly=1）────────────────────────────

    for (int oi = 0; oi < 4; oi++) {
        int lx = order[oi] & 1;
        int lz = order[oi] >> 1;
        uint top = c[lx][0][lz];
        if (!isMobile(top)) continue;

        // 直接下落
        if (canDisplace(top, c[lx][1][lz])) {
            uint tmp = c[lx][0][lz];
            c[lx][0][lz] = c[lx][1][lz];
            c[lx][1][lz] = tmp;
            continue;
        }

        // 粉末：對角線下落（在 2×2 XZ 範圍內嘗試）
        if (isPowder(top)) {
            int dlx = 1 - lx, dlz = 1 - lz;

            if (canDisplace(top, c[dlx][1][lz])) {
                uint tmp = c[lx][0][lz];
                c[lx][0][lz] = c[dlx][1][lz];
                c[dlx][1][lz] = tmp;
                continue;
            }
            if (canDisplace(top, c[lx][1][dlz])) {
                uint tmp = c[lx][0][lz];
                c[lx][0][lz] = c[lx][1][dlz];
                c[lx][1][dlz] = tmp;
                continue;
            }
        }
    }

    // ── Pass 2：液體水平擴散（底層 XZ）───────────────────────────────────
    // 只對「這幀未被重力移動過」的液體格做擴散，防止震盪。
    {
        bool xFirst = (r & 1u) != 0u;
        bool zFirst = (r & 2u) != 0u;
        for (int xi = 0; xi < 2; xi++)
        for (int zi = 0; zi < 2; zi++) {
            int lx = xFirst ? xi : (1-xi);
            int lz = zFirst ? zi : (1-zi);
            // 只擴散「本幀 ly=1 格子未被替換」的液體
            if (!isLiquid(c[lx][1][lz])) continue;
            if (c[lx][1][lz] != orig[lx][1][lz]) continue; // 剛被重力填入，跳過

            int dlx = 1 - lx;
            // 嘗試 X 方向擴散
            if (isEmpty(c[dlx][1][lz])) {
                uint tmp = c[lx][1][lz];
                c[lx][1][lz] = c[dlx][1][lz];
                c[dlx][1][lz] = tmp;
                continue;
            }
            // 嘗試 Z 方向擴散
            int dlz = 1 - lz;
            if (isEmpty(c[lx][1][dlz])) {
                uint tmp = c[lx][1][lz];
                c[lx][1][lz] = c[lx][1][dlz];
                c[lx][1][dlz] = tmp;
            }
        }
    }

    // ── 回寫變動格子（設 dirty flag）────────────────────────────────────────

    for (int lx = 0; lx < 2; lx++)
    for (int ly = 0; ly < 2; ly++)
    for (int lz = 0; lz < 2; lz++) {
        if (c[lx][ly][lz] == orig[lx][ly][lz]) continue;
        int wx = bx+lx, wy = by+ly, wz = bz+lz;
        if (inBounds(wx, wy, wz))
            cells[wIdx(wx, wy, wz)] = c[lx][ly][lz] | (1u << 14);
    }
}
""";
}
