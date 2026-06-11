# plan-scale-world.md — 玩家尺度・世界擴張・效能・採掘 Raycast

## 背景與目標

TileSize 座標系已正確（1 tile = 1/32 Godot unit）。
視覺上仍無改善，因為玩家物理體仍是 **1×1×1 tile**，相機視野也跟著等比縮放。

要讓 tile 「看起來細」，需要：
1. 玩家碰撞體高度從 1 tile → **3 tiles**，視覺 mesh 隨之更高，tile 相對顯小
2. 世界擴張（更大、更有探索感）
3. 效能保障（擴張才不爆幀）
4. 3D 採掘 Raycast（可測試凹坑細節，本計畫最終目標）

---

## P 系列 — 玩家碰撞體擴大

> **優先最高；可獨立實作，不依賴其他系列。**

### 目前狀態

`PlayerController.TryMove / TryMoveDir / ApplyPhysics / IsOnGround` 全部只查
`(Position.X, Position.Y, Position.Z)` 這 **一個格子**。玩家在 tile 空間是 1×1×1。

### P-1：定義 BodyH 常數（`PlayerController.cs`）

```csharp
/// <summary>玩家碰撞體高度（tiles）。Y = head（最小 Y），Y+BodyH-1 = feet（最大 Y）。</summary>
public const int BodyH = 3;
```

**Position 語意不變**：Position.Y = 頭部格（最頂端），Y+1、Y+2 = 胸/腳。

---

### P-2：修改所有物理方法（`PlayerController.cs`）

| 方法 | 目前 | 改後 |
|------|------|------|
| `TryMove(dx, dy)` | 只查 `(nx, ny, nz)` | 掃描 `(nx, ny+row, nz)` for row in `[0, BodyH-1]`；爬坡：目標整列 air 且頭頂 `(nx, ny-1)` air → step-up |
| `TryMoveDir(dx, dz)` | 同上 | 同上（X / Z 兩軸分別掃整列） |
| `TryMoveDepth(dz)` | 只查 `(X, ny, nz)` | 掃描 `(X, ny+row, nz)` for row in `[0, BodyH-1]` |
| `ApplyPhysics` | 地面查 `Y+1`；天花板查 `Y-1` | 地面查 `Y+BodyH`；天花板查 `Y-1`（頭頂不變） |
| `IsOnGround` | `GetTile(X, Y+1, Z)` | `GetTile(X, Y+BodyH, Z)` |
| `UpdateEnvironment` | 查 `(X, Y, Z)` 1格 | 查腳部格 `(X, Y+BodyH-1, Z)` |

**爬坡邏輯**（修改後）：
```
若目標列有任一 row 阻擋:
  且 (nx, ny-1+row) for row in [0, BodyH-1] 全部 air（整個身體往上移一格後目標列通暢）
  且 (Position.X, ny-1, Position.Z) air（目前頭頂有空間讓身體上升）
  → ny -= 1，完成爬坡
```

---

### P-3：Main.cs 視覺 mesh（`Main.cs`）

```csharp
// BoxMesh 高度：BodyH × T
Mesh = new BoxMesh { Size = new Vector3(0.65f * T, PlayerController.BodyH * T, 0.65f * T) }

// playerMesh.Position Y 中心：腳底向上 BodyH/2
_playerMesh.Position = new Vector3(
    _player.Position.X * T + T * 0.5f,
    _player.Position.Y * T + PlayerController.BodyH * T * 0.5f,  // 身體中心
    _player.Position.Z * T + T * 0.5f);
```

FPS 眼睛位置 `CameraController.FpEyeY` 改為 `-(PlayerController.BodyH - 0.5f) * T`
（在頭部格子中心，即 `Position.Y * T + T * 0.5f` 相對於 mesh center）。

---

### P-4：MapGenerator 出生點修正（`MapGenerator3D.cs`）

出生點 Y 要往上移，保証腳底 `Y + BodyH - 1` 落在實體地面上：
```csharp
// spawn.Y -= (BodyH - 1)  // 頭頂對齊，往上提 BodyH-1 格
```

---

### 預期效果

OrthoZoom = 30 tiles 視野，玩家高度 3 tiles → 玩家佔視野 10%，tile 明顯「比玩家小」。
BodyH=4 或 5 可加強效果（改一個常數即可）。

---

## E 系列 — 效能優化

> **E-1 必須在 W-1 之前完成；E-2/E-3 可在 W-1 後陸續補上。**

### E-1：GPU Upload GC 壓力修正（`CaGpuSimulator.cs`）⚠️ 嚴重

**目前問題**：
```csharp
var bytes = MemoryMarshal.AsBytes(_staging.AsSpan()).ToArray(); // 每幀 3.2 MB 新配置！
_rd.BufferUpdate(_buffer, 0, _bufferByteSize, bytes);
```
64×200×64 = 819,200 格 × 4 bytes = **3.2 MB / 幀**，GC 壓力巨大。

**修正方案**：改用 pinned staging byte 陣列，避免 ToArray：
```csharp
// 宣告第二個 staging buffer（bytes），與 _staging (uint[]) 共存
private byte[] _stagingBytes = null!;

// Initialize 時：
_stagingBytes = new byte[aw * ah * ad * sizeof(uint)];

// Upload 時：
Buffer.BlockCopy(_staging, 0, _stagingBytes, 0, _stagingBytes.Length);
_rd.BufferUpdate(_buffer, 0, _bufferByteSize, _stagingBytes);
```
完全消除每幀 3.2 MB 配置。

---

### E-2：Upload 跳過 Air chunk（`CaGpuSimulator.cs`）

Upload 的三重迴圈可先查 Chunk3D 是否為全 Air（`Chunk3D.IsAllAir` 新增標記），
跳過全空 chunk（16³ = 4096 格），大幅減少 Pack 呼叫數。

```csharp
// 以 chunk 為單位跳躍
for (int cz = czMin; cz < czMax; cz++)
for (int cy = cyMin; cy < cyMax; cy++)
for (int cx = cxMin; cx < cxMax; cx++)
{
    if (world.TryGetChunk(cx, cy, cz)?.IsAllAir == true)
    {
        // fill staging with Air pack value (fast memset)
        Array.Fill(_staging, AirPack, offset, Chunk3D.Volume);
        continue;
    }
    // ... normal pack loop for this chunk
}
```

---

### E-3：Mesh rebuild 視距動態調整（`Main.cs`）

世界擴張後，玩家可能走到未生成區域，建議：
- `maxPerFrame` 從 30 → 50（更快補 mesh）
- `viewRadius` 從 5 → **7**（更大視野）
- 但 2D 模式 viewRadius 維持 5（Z 軸看不到）

---

## W 系列 — 世界擴張

> **依賴 E-1 先完成；W-1 最先，W-2 視玩法需求。**

### W-1：世界尺寸擴大（`Main.cs`）

```csharp
private const int WorldW = 2400;  // 600 → 2400（4×）
private const int WorldH =  300;  // 200 → 300（1.5×）
private const int WorldD = WorldW;
```

世界總格數：2400×300×2400 = 17.28 億格，但 **lazy loading** 確保記憶體只用已生成 chunk：
- 視距 7 chunks radius ≈ 15³ = 3375 chunks 同時在記憶體
- 每 chunk 16³ 格 = 4096 格，uint8 材質 + bitmap ≈ ~5 KB
- 總計約 **17 MB**（可接受）

`EnsureChunksGenerated` radius 改 `8`，`maxPerCall` 改 `8`（更快預加載）。

---

### W-2：GPU CA 主動區擴大（`Main.cs`、`TileWorld3D.cs`）

GPU zone 已跟隨玩家（`centerCX * Size - AW/2`）。
擴大 zone size 讓更大範圍受 GPU CA 模擬：

```csharp
_world3d.InitGpu(128, WorldH, 128);  // 64→128（每幀 Upload 量 ×4，先做 E-1 再調）
```

若 E-1 + E-2 完成後幀率仍可接受，可進一步擴到 192。

---

### W-3：MapGenerator3D 大世界支援（`MapGenerator3D.cs`）

目前生成邏輯以 XZ 高度圖 + CA 洞穴，本身就是按 chunk 懶加載，理論上不需改。
但大世界需確認：
- Heightmap noise 函數在 X=2400 不循環（目前用 `Math.Sin`，週期性問題）
- 建議改成 simplex noise 或以 chunk coord 為 seed 的偽隨機，避免週期感

---

## R 系列 — 3D 採掘 Raycast

> **依賴 P + W 先完成；這是整個計畫的最終測試目標。**

### R-1：TileWorld3D DDA Raycast（已有 3D DDA，確認參數）

`TileWorld3D` 已有 `Raycast(origin, dir, maxDist)`（Phase 3 VM Group 5 遺留）。
確認其回傳：`(GridPos hitTile, GridPos face, bool hit)`。

若沒有 face normal 回傳，補上（六面法線查表）。

---

### R-2：TPS/FPS 模式滑鼠格座標改用 Raycast（`Main.cs`）

```csharp
// 目前（S-4）：只在 SideScroll2D / Iso 有效（Z=0 平面投影）
// 改後：TPS/FPS 走 Raycast，SideScroll2D/Iso 維持平面投影

if (_camera3d.Mode is CameraMode.ThirdPerson or CameraMode.FirstPerson)
{
    var ray    = _camera3d.GetRayFromCenter();           // origin + dir，Godot unit
    float T    = TileWorldConstants.TileSize;
    var tileOrigin = new Vector3(ray.Origin.X / T, ray.Origin.Y / T, ray.Origin.Z / T);
    var (hit, face, didHit) = _world3d.Raycast(
        (int)tileOrigin.X, (int)tileOrigin.Y, (int)tileOrigin.Z,
        ray.Dir,
        maxDist: (int)(PlayerController.MiningRange * 2));
    if (didHit)
    {
        _player.MouseGridPos = hit;
        _player.MouseFaceNormal = face;  // 新增：放置時用法線確定放置面
    }
}
```

`CameraController` 新增：
```csharp
public (Vector3 Origin, Vector3 Dir) GetRayFromCenter()
{
    var vp   = GetViewport().GetVisibleRect();
    var mid  = vp.GetCenter();
    return (_cam.ProjectRayOrigin(mid), _cam.ProjectRayNormal(mid));
}
```

---

### R-3：採掘 / 放置接 Raycast 結果（`Main.cs`）

- 採掘（左鍵持續）：`_player.TickMining(_world3d, _player.MouseGridPos, dt)` 不需改
- 放置（右鍵）：目前是 `worldPos3.X / T`，改為 `_player.MouseGridPos + _player.MouseFaceNormal`

---

## 實作順序

```
Step 1  P-1 + P-2：PlayerController BodyH = 3（碰撞體）
Step 2  P-3：Main.cs mesh 高度與位置
Step 3  P-4：MapGen 出生點修正
        → build + 測試：玩家 3 tiles 高，能走路跳躍
Step 4  E-1：GPU Upload byte[] 不再 ToArray（消除 3.2MB/幀 GC）
        → dotnet build；Profile 驗證 GC alloc 降為 0
Step 5  W-1：WorldW/D 2400、WorldH 300
        → 遊戲中探索世界更大
Step 6  E-3：maxPerFrame/viewRadius 調整
Step 7  W-2：InitGpu zone 128×300×128（E-1 完成後才擴）
Step 8  E-2：Upload 跳過 Air chunk（進階優化，非阻塞）
Step 9  W-3：MapGen noise 去週期（大世界地形不循環）
Step 10 R-1：確認 TileWorld3D.Raycast 有 face normal 回傳
Step 11 R-2：TPS/FPS 滑鼠格 → Raycast
Step 12 R-3：放置接 face normal
        → 完整測試：挖掘凹坑、球形放置，驗證 TileSize 效果
```

---

## 完成後預期視覺效果

| 參數 | 改後 |
|------|------|
| 玩家高度 | 3 tiles（佔 30-tile 視野約 10%）|
| 世界寬度 | 2400 tiles（是玩家的 800 倍） |
| 可見細節 | 挖掘留下 1-tile 精度凹坑；球體 R=10 tile 肉眼無鋸齒 |

若 BodyH 調到 4-5，效果更明顯；OrthoSize 也可減少（顯示更少 tile = 放大效果）。

---

*最後更新：2026-06-12*
