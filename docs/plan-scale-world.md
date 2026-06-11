# plan-scale-world.md — 粒子尺度・世界擴張・效能・採掘 Raycast・次像素視覺層

## 設計核心

**每個 tile = 一個獨立物理粒子。**

球體由幾千個粒子組成，打碎後各自散落。
挖掘留下的坑由幾百個缺失粒子組成，形狀有機、細緻、無鋸齒。
這是 Noita 的核心設計。本計畫讓 SkillCreator 走向同一條路。

---

## 目標感知尺度

### 指標：螢幕像素/tile

```
目前：~18 px/tile → tile 明顯可見，世界是格子
目標：1~2 px/tile  → tile 幾乎不可辨識，打出來的形狀自然流暢
```

### 必要數字（以 1080p 為基準）

| 參數 | 目前 | 近期目標（Phase A）| 遠期目標（Phase B）|
|------|------|-------------------|-------------------|
| px/tile | ~18 | 3~5 | 1~2 |
| 相機可見 tile（垂直） | 60 | 200~350 | 500~1000 |
| **BodyH（玩家 tile 高）** | **1** | **24~32** | **64~96** |
| WorldH | 200 | 1600 | 4800 |
| WorldW / D | 600 | 3200 | 9600 |
| CA 主動區（WxHxD） | 64×200×64 | 128×400×128 | 256×800×256 |

「近期目標」可在現有硬體上跑，但 fps 需優化。
「遠期目標」等設備更強或架構升級後調整一個常數即可。

---

## 可擴縮架構（Scale-First Design）

### 核心原則：一個常數控制全部粒度

引入 `WorldScale.cs`，所有尺度從這裡派生：

```csharp
namespace SkillCreator.World;

public static class WorldScale
{
    // ── 唯一需要調整的旋鈕 ──────────────────────────────────────────
    /// <summary>
    /// 每「遊戲單位」包含幾個 tile（粒度）。
    /// 這個值越大 → tile 越細 → 效果越好 → 效能需求越高。
    /// 現在：Grain=16（過渡）→ 以後可改 32/64/128。
    /// </summary>
    public const int Grain = 16;

    // ── 派生常數（不要直接改這些）──────────────────────────────────
    public const float TileSize     = 1f / (float)Grain;   // Godot unit / tile
    public const int   PlayerH      = Grain * 2;            // 玩家高度（tiles）
    public const int   PlayerW      = Grain;                 // 玩家寬度（tiles）

    // 世界大小（依 PlayerH 比例）
    public const int   WorldH       = PlayerH * 50;         // 50 個玩家高度
    public const int   WorldW       = PlayerH * 100;        // 100 個玩家寬度
    public const int   WorldD       = WorldW;

    // 相機垂直可見 tile 數（玩家佔約 10% 視野高度）
    public const int   CamTilesV    = PlayerH * 10;
    public const float OrthoSize    = CamTilesV * TileSize * 0.5f;

    // GPU CA 主動區（以 chunk 為單位，chunk=16³）
    public const int   GpuZoneW     = 128;   // tiles，需為 2 的冪
    public const int   GpuZoneH     = WorldH; // 全高
    public const int   GpuZoneD     = 128;

    // CA 模擬半徑（chunk 數）
    public const int   SimRadiusChunks  = 8;
    public const int   MeshRadiusChunks = 7;
}
```

**調整粒度只改 `Grain`**：
| Grain | px/tile（1080p）| BodyH | WorldH | 效能需求 |
|-------|----------------|-------|--------|---------|
| 8 | ~7 px | 16 tiles | 800 | 可接受 |
| 16 | ~3 px | 32 tiles | 1600 | 需優化 |
| 32 | ~1.5 px | 64 tiles | 3200 | 高端 GPU |
| 64 | ~0.7 px | 128 tiles | 6400 | 未來硬體 |

---

## 物理粒子系統（已有，確認完整）

目前 CA 系統每個 tile 已是獨立物理粒子：
- **Powder**（砂、土）：重力下落，堆積
- **Liquid**（水、岩漿）：流動，填低處
- **Gas**（火、蒸汽）：上升，擴散
- **Static**（石、木）：靜止，可燃燒

**「打碎結構體」需要的額外機制：**

放置的球體 / 牆壁 / 建築 → 材質為 `Stone`（Static）。
被爆炸/採掘 → `TileWorld3D.Explode()` 已有，半徑內 Stone → Air（移除）。
散落效果 → 邊緣格子改為 `Sand`（Powder，立即開始下落）。

這在 `SpellCaster.Explode` 或新增 `SmashSurface()` helper 即可實作，不需改底層 CA。

---

## 實作計畫（四個系列）

### P 系列 — 玩家碰撞體（第一優先）

#### P-0：引入 WorldScale.cs（Grain=16 起步）

```
新建 Scripts/World/WorldScale.cs（取代 TileWorldConstants.cs 中的 TileSize 常數）
Main.cs：WorldW/H/D 改為 WorldScale.WorldW/H/D
Main.cs：_orthoZoom 初值改為 WorldScale.OrthoSize
InitGpu 改為 WorldScale.GpuZoneW/H/D
```

**注意**：`TileWorldConstants.TileSize` 可改為 `=> WorldScale.TileSize` 轉發，保持現有引用不壞。

#### P-1：PlayerController 碰撞體擴大

```csharp
// PlayerController.cs 最頂端加入
public const int BodyH = WorldScale.PlayerH;  // = Grain * 2
public const int BodyW = 1;                   // 寬度保持 1 tile，物理簡單

// Position.Y = 頭頂（最小 Y）；Position.Y + BodyH - 1 = 腳底
```

**需修改的方法**（全部改掃整列 `Y+0` 到 `Y+BodyH-1`）：
- `TryMove(dx, dy)` — 水平 + 垂直移動
- `TryMoveDir(dx, dz)` — 相機相對移動
- `TryMoveDepth(dz)` — Z 軸移動
- `ApplyPhysics` — 地面查 `Y+BodyH`，天花板查 `Y-1`
- `IsOnGround` — 查 `Y+BodyH`
- `UpdateEnvironment` — 查腳底 `Y+BodyH-1`

爬坡規則（Grain=16，BodyH=32）：最多爬 1 tile 不夠 — 需允許爬 2~3 tiles（避免地形鋸齒卡死）：
```
// 爬坡高度上限 = max(1, BodyH / 16)  → Grain=16 時 = 2 tiles
```

#### P-2：Main.cs 視覺 mesh

```csharp
// mesh 尺寸
float T = WorldScale.TileSize;
new BoxMesh { Size = new Vector3(WorldScale.BodyW * T, WorldScale.BodyH * T, WorldScale.BodyW * T) }

// mesh 位置（中心 = 頭頂 + BodyH/2）
_playerMesh.Position = new Vector3(
    _player.Position.X * T + T * 0.5f,
    _player.Position.Y * T + WorldScale.BodyH * T * 0.5f,
    _player.Position.Z * T + T * 0.5f);

// 相機 target（同 mesh 中心）
_camera3d.TargetPosition = new Vector3(
    _player.Position.X * T + T * 0.5f,
    _player.Position.Y * T + WorldScale.BodyH * T * 0.5f,
    _cam2D ? 0f : _player.Position.Z * T + T * 0.5f);

// 相機 OrthoSize（由 WorldScale 控制）
_camera3d.SetOrthoSize(WorldScale.OrthoSize);
```

#### P-3：MapGenerator3D — 洞穴與出生點

洞穴至少要有 `BodyH + 4` 高度讓玩家行走：
- 目前 CA 洞穴挖空半徑 ~4 tiles → 需改為 `Grain + 2` tiles
- FloodFill 通道寬度同樣需提升
- 出生點 Y 上移：`spawnY -= (BodyH - 1)`（頭在地表格）

#### P-4：敵人碰撞體同步（`Enemy.cs`）

敵人高度改為 `WorldScale.Grain`（玩家的一半），以保持視覺比例合理。

---

### E 系列 — 效能優化

> 大世界必須先保障幀率，才有測試意義。

#### E-1：GPU Upload GC 修正 ⚠️ 最緊急

**問題**：目前 `MemoryMarshal.AsBytes(...).ToArray()` 每幀配置 3.2 MB 新陣列。
Grain=16 後 zone=128×1600×128 = 26M tiles → 每幀 104 MB GC 壓力，直接崩潰。

**修正**：
```csharp
// CaGpuSimulator.cs - 初始化時預配置
private byte[] _stagingBytes = null!;

// Initialize()
_stagingBytes = new byte[aw * ah * ad * sizeof(uint)];

// Upload()
Buffer.BlockCopy(_staging, 0, _stagingBytes, 0, _stagingBytes.Length);
_rd.BufferUpdate(_buffer, 0, _bufferByteSize, _stagingBytes); // 不再 ToArray
```

#### E-2：Upload 跳過全 Air chunk

`Chunk3D` 新增 `bool IsAllAir`（在 SetTile 時維護）：
```csharp
// Upload 改以 chunk 為單位
for (int cz ...) for (int cy ...) for (int cx ...)
{
    if (world.TryGetChunk(cx, cy, cz)?.IsAllAir != false)
        { /* 用 AirPackValue 批量填充 */ continue; }
    // ... 正常 Pack 迴圈
}
```
在稀疏世界（大量 Air）中可節省 60~80% Upload 時間。

#### E-3：分幀 CA 模擬（大 zone 必須）

Zone=128×1600×128 無法每幀全算。改為每幀只算 1/4 zone（輪替）：
```csharp
// TileWorld3D.Tick() 中
// _simFrame % 4 決定本幀算哪個 Z 象限
int zOffset = (_simFrame % 4) * (GpuZoneD / 4);
_gpuSim.SimulateSubzone(zOffset, GpuZoneD / 4, rng);
```
效果：CA 更新頻率降為 15fps（物理），但視覺仍 60fps 渲染。對粉末/液體物理可接受。

#### E-4：Mesh rebuild 視距動態

```csharp
// Main.cs
_renderer3d.RebuildDirtyMeshes(
    maxPerFrame: 60,            // 30 → 60
    viewCX: pCX, viewCY: pCY, viewCZ: in2D ? -1 : pCZ,
    viewRadius: WorldScale.MeshRadiusChunks);  // 動態，由 WorldScale 控制
```

#### E-5：CPU CA 限縮（大 BodyH 時省 CPU）

CA Tick 的 CPU 部分（Gas / Static / 元素反應）目前 simRadius=6 chunks。
Grain=16 時玩家高 32 tiles，模擬半徑維持 8 chunks（128 tiles）足夠，不需改。

---

### W 系列 — 世界擴張

> **依賴 P-0 + E-1 完成後才做。**

#### W-1：世界尺寸由 WorldScale 驅動（`Main.cs`）

```csharp
// 改為
_world3d = new TileWorld3D(WorldScale.WorldW, WorldScale.WorldH, WorldScale.WorldD);
_world3d.InitGpu(WorldScale.GpuZoneW, WorldScale.GpuZoneH, WorldScale.GpuZoneD);
```

Grain=16 時：WorldW=3200, WorldH=1600, WorldD=3200。
記憶體：lazy chunk loading，實際載入約 MeshRadius³ 個 chunk ≈ 15³ = 3375 chunk × 5KB = 17MB。

#### W-2：MapGenerator3D 大世界地形

- Heightmap noise：改用 simplex noise（`FastNoiseLite` 或手寫 gradient noise），避免 `Math.Sin` 週期感
- 洞穴 CA threshold：調高（3D 26鄰居，threshold 從 14→18），使洞穴更寬
- 洞穴高度放大：連通 FloodFill 通道改為 `Grain + 4` tiles 寬

#### W-3：MapGenerator 懶加載加速

```csharp
// 已有 EnsureChunksGenerated，改參數
_mapGen.EnsureChunksGenerated(_world3d, pCX, pCY, pCZ,
    radius: WorldScale.SimRadiusChunks,
    maxPerCall: 12);  // 4 → 12，更快補生成
```

---

### R 系列 — 3D 採掘 Raycast

> **P + W 完成後，視覺尺度有意義，才值得接入採掘。**

#### R-1：TileWorld3D.Raycast 確認 face normal

現有 3D DDA Raycast（Phase 3 VM Group 5）確認回傳：
```csharp
public (GridPos HitTile, GridPos FaceNormal, bool DidHit) Raycast(
    float ox, float oy, float oz,
    Vector3 dir, int maxDist)
```
若缺少 `FaceNormal`，補入六面法線查表。

#### R-2：TPS/FPS 模式採掘目標改 Raycast（`Main.cs`、`CameraController.cs`）

```csharp
// CameraController.cs 新增
public (Vector3 Origin, Vector3 Dir) GetCenterRay()
{
    var mid = GetViewport().GetVisibleRect().GetCenter();
    return (_cam.ProjectRayOrigin(mid), _cam.ProjectRayNormal(mid));
}

// Main.cs _Process() 中
if (_camera3d.Mode is TPS or FPS)
{
    float T = WorldScale.TileSize;
    var (ro, rd) = _camera3d.GetCenterRay();
    var (hit, face, ok) = _world3d.Raycast(ro.X/T, ro.Y/T, ro.Z/T, rd,
        maxDist: (int)(PlayerController.MiningRange * 2));
    if (ok) { _player.MouseGridPos = hit; _player.MouseFaceNormal = face; }
}
```

#### R-3：放置接 face normal

```csharp
// 右鍵放置：目標格 = MouseGridPos + MouseFaceNormal（貼緊挖掘面）
var placeTarget = new GridPos(
    _player.MouseGridPos.X + _player.MouseFaceNormal.X,
    _player.MouseGridPos.Y + _player.MouseFaceNormal.Y,
    _player.MouseGridPos.Z + _player.MouseFaceNormal.Z);
```

---

## 實作順序

```
Phase A（近期，Grain=16）：
  Step 1   P-0：WorldScale.cs（Grain=16）
  Step 2   E-1：GPU Upload byte[] 預配置（消除 GC 崩潰）
  Step 3   P-1：PlayerController BodyH=32
  Step 4   P-3：MapGen 洞穴放大 + 出生點修正
  Step 5   P-2：Main.cs mesh + 相機
           → build + 遊戲內確認：玩家 32 tiles 高，tile ≈ 3px，洞穴可行走
  Step 6   W-1：世界擴張（3200×1600×3200）
  Step 7   W-2：MapGen simplex noise + 大洞穴
  Step 8   E-2：Upload 跳過 Air chunk
  Step 9   E-3：分幀 CA 模擬
  Step 10  R 系列：3D Raycast 採掘
           → 完整測試：粒子破碎、球形放置、凹坑細緻度

Phase B（遠期，Grain=32 或更大）：
  → 只改 WorldScale.Grain = 32
  → 其餘代碼自動跟進（所有數值從 WorldScale 派生）
  → 確認效能是否可接受；不可接受則優化 E-3/4/5
```

---

## 效能展望

| Grain | CA 主動格數 | 相對目前 | 估計幀時間（GPU CA）|
|-------|------------|---------|-------------------|
| 4（測試基準）| 64×200×64 ≈ 820K | ×1 | ~2ms |
| 8 | 64×400×64 ≈ 1.6M | ×2 | ~4ms |
| **16（Phase A）** | 128×1600×128 ≈ 26M | ×32 | ~64ms（需 E-3 分幀）|
| 32（Phase B） | 256×3200×256 ≈ 210M | ×256 | 需 Sparse GPU |
| 64（未來） | 512×6400×512 ≈ 1.68B | ×2048 | 未來硬體 |

Grain=16 配合 E-3 分幀模擬（每幀只算 1/4 zone），GPU CA 降回 ~16ms，可接受。
Grain=32 以上需要 Sparse CA（只計算非空 chunk 的 GPU 任務），是獨立優化項目。

---

## 物件破碎（未來可加）

基礎已就緒：每個 tile 已是獨立物理粒子。
「破碎效果」只需：
1. 球體 / 牆壁材質用 `Stone`（Static）
2. 命中時呼叫 `TileWorld3D.Explode(center, r)` → 邊緣 tile 改為 `Sand`（Powder）
3. Sand 粒子立即開始 CA 重力下落 → 自然散落

這不需要任何物理引擎改動，CA 本身就是粒子物理。

---

## V 系列 — 次像素視覺層（Sub-pixel Particle Layer）

> 原始設計文件（`plan-roadmap.md §14`、`history/phase1-2.md`）已定案的雙層架構。
> **本系列依賴 R 系列完成後再啟動**；V-3 粒子沉積是最終目標。

### 架構概念

```
次像素浮點粒子（Sub-pixel，視覺層）← 本系列
         ↕  V-3：粒子沉積 → 改變 Tile 材質
中粒度 Tile CA（物理層）← P/E/W/R 系列
```

物理精度由 Grain 控制（P 系列），視覺豐富度由 Sub-pixel 補足。
兩層分離設計：視覺層壞掉不影響物理；物理層跑 15fps 但視覺仍 60fps。

---

### V-1：純裝飾 VFX（GPUParticles3D）⬜ 成本：很低

**觸發點 → 粒子效果**：

| 觸發 | 效果 | 粒子數 / 壽命 |
|------|------|-------------|
| `Explode()` 爆炸 | 碎片飛散（石屑、塵埃） | 50~200 顆 / 0.8s |
| Sand/Powder 落地 | 微塵揚起 | 5~20 顆 / 0.3s |
| Lava 接觸 Water | 蒸汽爆裂 | 30~80 顆 / 1.0s |
| 採掘命中 | 火花 + 碎屑 | 10~30 顆 / 0.5s |

實作：`Main.cs` 在 `OnTileDestroyed` / `OnExplosion` 事件裡呼叫 `SpawnVfx(pos, type)`；
`VfxManager.cs`（新）管理粒子池（`GPUParticles3D` 預建實例，避免每次 `new`）。

---

### V-2：液體表面 Shader ⬜ 成本：很低

Water tile 的 mesh 加一層視覺擾動，不新增粒子：

```csharp
// MaterialRegistry 中 Water 材質的 StandardMaterial3D
mat.DetailEnabled = true;
// 或直接用 ShaderMaterial：uv 偏移隨時間 sin 波動，模擬液面漣漪
```

實作位置：`TileWorldRenderer3D.cs` 建立 Water mesh 時改用 `ShaderMaterial`（`.gdshader` 內嵌）。

---

### V-3：粒子沉積 → 改變 Tile 材質 ⬜ 成本：中等（最終目標）

**設計**：次像素粒子有「重量」，落地後累積，達到閾值時在對應 Tile 格寫入材質。

```
應用場景：
  灰燼粒子從燃燒處飄落 → 累積 → 地面 Tile 變 Ash
  水花粒子落地 → 累積 → 乾地 Tile 變濕（WeakWater 材質）
  岩漿碎片飛濺 → 落地冷卻 → 生成 Stone Tile
```

**實作架構**：

```csharp
// SubpixelParticle（純 CPU 結構體，非 Godot Node）
public record struct SubpixelParticle
{
    public Vector3   Pos;          // float 世界座標
    public Vector3   Vel;          // 速度（含重力）
    public MaterialType Payload;   // 落地時寫入的材質
    public float     Mass;         // 累積閾值貢獻
    public float     Life;         // 剩餘壽命（秒）
}

// DepositMap：tile 座標 → 累積量（Dictionary 或稀疏陣列）
// 每幀：移動粒子 → 落地 → 累積 DepositMap → 超過閾值 → SetTile
```

**效能管控**：
- 同時存活粒子上限：`MaxDepositParticles = 2000`（可調）
- DepositMap 每 N 幀 flush 一次（不需每幀寫 Tile）
- 粒子壽命到期但未沉積 → 直接消滅（不強制寫 Tile）

---

### V 系列實作順序

```
V-1（VFX 粒子池）  → 任何時候可插入，不阻塞其他系列
V-2（液體 Shader） → 任何時候可插入
V-3（粒子沉積）    → R 系列完成後，Grain 穩定後
```

---

## 完整實作順序（含 V 系列）

```
Phase A（近期，Grain=16）：
  Step 1   P-0：WorldScale.cs
  Step 2   E-1：GPU Upload GC 修正 ⚠️
  Step 3   P-1/P-3/P-2：玩家 BodyH=32 + MapGen + mesh
  Step 4   W-1/W-2/W-3：世界擴張 3200×1600×3200
  Step 5   E-2/E-3：Upload 跳過 Air + 分幀 CA
  Step 6   R-1/R-2/R-3：3D Raycast 採掘
           → 里程碑：粒子尺度可見，挖掘凹坑細緻

  Step 7   V-1：VFX 粒子池（爆炸/採掘視覺）
  Step 8   V-2：液體表面 Shader
           → 里程碑：世界視覺豐富

Phase B（遠期，Grain=32）：
  → 改 WorldScale.Grain = 32，其餘自動跟進

  Step 9   V-3：粒子沉積 → 改變 Tile 材質
           → 最終里程碑：灰燼堆積、水痕殘留、岩漿冷卻成石
```

---

*最後更新：2026-06-12*
