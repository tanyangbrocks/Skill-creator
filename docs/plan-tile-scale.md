# plan-tile-scale.md — Tile 尺度縮放計畫

## 目標

在不動 CA 物理邏輯的前提下，藉由縮小「1 tile 對應的 Godot float 單位」，提升視覺細粒度：
- 挖掘能在地面留下細緻輪廓
- 放置球體時肉眼看不出鋸齒（需 R ≥ 16 tiles）
- 尺度感更貼近 Noita 風格

---

## 核心概念

```
目前：1 tile = 1.0 Godot unit（tile 整數座標直接等於世界座標）
改後：1 tile = TileSize Godot unit（所有可視座標乘上此係數）
```

**CA 物理、玩家物理、碰撞、礦石判定、敵人 AI——全部在 tile 整數空間運算，完全不受影響。**
只有「tile 座標 → Godot world 座標」的轉換點需要乘上 TileSize。

---

## 尺度比率建議

| TileSize | 1/tile Godot unit | 玩家高度（mesh 0.9u） | R=10 球體視感 |
|----------|-------------------|--------------------|--------------|
| 1/16     | 0.0625            | ~14 tiles           | 可識別，略鋸齒   |
| 1/32     | 0.03125           | ~29 tiles           | 平滑（遠視距）   |
| 1/64     | 0.015625          | ~57 tiles           | 平滑（近視距）✓  |

**建議起點：`TileSize = 1f / 32f`**（可先驗證效果，需要更細再改 `1/64`）。

「球體不可見鋸齒」門檻：球半徑 R ≥ 16 tiles。
以 1/32 為例，一顆高度 = 0.3 × 玩家身高 的球 → R ≈ 9 tiles（邊界）。
以 1/64 為例，同樣大小的球 → R ≈ 17 tiles（達標）。

---

## 影響地圖

### ✅ 完全不需改動

| 系統 | 理由 |
|------|------|
| `TileWorld3D` CA 模擬 | tile 整數空間 |
| `CaGpuSimulator` GPU CA | tile 整數空間 |
| `PlayerController` 物理 | Gravity/JumpSpeed 單位是 tiles/s |
| `PlayerController.MiningRange` | 單位是 tiles（`GridPos.DistanceTo()` 比較）|
| 敵人 AI | tile 整數移動 |
| 技能/投射物邏輯 | tile 整數空間 |
| 世界生成 `MapGenerator3D` | tile 整數空間 |
| Chunk 系統 | tile 整數空間 |
| 技能 VM | tile 整數空間 |

---

### ❗ 需要修改的地方（全部是「tile 座標 → Godot unit」的轉換點）

#### S-1：新增 TileSize 常數

**檔案**：新建 `Scripts/World/TileWorldConstants.cs`（或放進 `TileWorldRenderer3D.cs` 頂端）

```csharp
public static class TileWorldConstants
{
    public const float TileSize = 1f / 32f;
}
```

---

#### S-2：TileWorldRenderer3D — 頂點與 Chunk 位置

**檔案**：`Scripts/World/TileWorldRenderer3D.cs`

| 位置 | 目前 | 改後 |
|------|------|------|
| `ApplyTaskResult()` L158 | `new Vector3(r.Coord.X * S, r.Coord.Y * S, r.Coord.Z * S)` | `new Vector3(r.Coord.X * S * T, r.Coord.Y * S * T, r.Coord.Z * S * T)` |
| `V3()` L325（所有頂點出口）| `new(p.x, p.y, p.z)` | `new(p.x * T, p.y * T, p.z * T)` |

`T = TileWorldConstants.TileSize`。所有 greedy meshing 頂點整數乘上此係數，一行改完。

---

#### S-3：Main.cs — 實體視覺位置與尺寸

所有 `Position.X + 0.5f` 型的計算都需改成 `Position.X * T + T * 0.5f`。

| 位置 | 變數/說明 | 改法 |
|------|----------|------|
| L837-840 | `_playerMesh.Position` | 所有分量 × T，偏移 × T |
| L213-215 | `BoxMesh { Size = new Vector3(0.65f, 0.9f, 0.65f) }` | `× T` |
| L847-850 | `_camera3d.TargetPosition` | 所有分量 × T，偏移 × T |
| L1309 | `SyncEnemyMeshes()` 敵人位置 | 同玩家 × T |
| L1315-1316 | 敵人 mesh 尺寸 `w`, `h` | `× T` |
| L2063 | `SpawnDmgNum()` — `WorldPx` 傷害數字位置 | `pos.X * T + T * 0.5f`，Y 同 |

---

#### S-4：Main.cs — 滑鼠→世界→格座標 轉換

**目前**（L742-744）：
```csharp
_player.MouseGridPos = new GridPos(
    Math.Clamp((int)worldPos3.X, 0, _world3d.Width  - 1),
    Math.Clamp((int)worldPos3.Y, 0, _world3d.Height - 1));
```

**改後**：
```csharp
float T = TileWorldConstants.TileSize;
_player.MouseGridPos = new GridPos(
    Math.Clamp((int)(worldPos3.X / T), 0, _world3d.Width  - 1),
    Math.Clamp((int)(worldPos3.Y / T), 0, _world3d.Height - 1));
```

世界座標已是縮小後的 float，除以 TileSize 才能還原成 tile 整數。
**這一個不改，採掘/放置會完全失效。**

---

#### S-5：Main.cs — OrthoZoom 初始值與範圍

**目前**：
```csharp
private float _orthoZoom = 30f;
private const float ZoomMin = 8f;
private const float ZoomMax = 80f;
```

Camera3D.Size 單位是 Godot unit。TileSize 縮小後，30 Godot unit 會涵蓋到 `30/T` tiles（非常多），需等比縮小：

```csharp
private static readonly float T = TileWorldConstants.TileSize;
private float _orthoZoom = 30f * T;
private const float ZoomMinBase = 8f;
private const float ZoomMaxBase = 80f;
// 實際使用時：ZoomMin * T、ZoomMax * T
```

---

#### S-6：CameraController — 臂長與距離預設值

`TpsArmLength`, `IsoArmLength`, `SideDist` 都是 Godot unit。TileSize 縮小後，原本「12 unit 臂長」等於離玩家 12/T = 384 tiles 遠——TPS 畫面會很遠。

**最簡作法**：在 `Main.cs` 的 `StartGameplay()` 初始化後直接覆寫：
```csharp
float T = TileWorldConstants.TileSize;
_camera3d.TpsArmLength = 12f * T;
_camera3d.IsoArmLength = 25f * T;
_camera3d.SideDist     = 40f * T;
```
不需改 `CameraController.cs` 本身（Export 欄位保留彈性）。

---

#### S-7：投射物視覺（如有獨立 3D mesh）

若 `Projectile` 有自己的 `MeshInstance3D` 且位置以 tile float 設定，同樣需 `Position * T`。
目前 projectile 邏輯在 tile 空間，若其視覺節點是 sprite/label overlay 則無影響；
需在實作時確認是否存在獨立 3D mesh。

---

#### S-8：掉落物 `DroppedItemWorld`

若有視覺 mesh 跟隨 tile 位置，同樣 `Position * T`。
需在實作時確認 `DroppedItemWorld.cs` 的座標設定點。

---

## 這個計畫不解決的事

| 問題 | 獨立計畫 |
|------|---------|
| 世界太小（tile 總數不足） | 需要先做效能優化（GPU zone AH 動態窗口、Upload 向量化）才能安全擴大 |
| 背景虛空感 | 天空盒 / 霧效 — 獨立視覺改善 |
| Powder/Liquid 細粒度擴散感 | TileSize 縮小後粒子視覺已更細，但物理步數不變；可後續討論 |

---

## 實作順序

```
Step 1  新增 TileWorldConstants.cs（S-1）
Step 2  TileWorldRenderer3D V3() + ApplyTaskResult（S-2）
        → dotnet build，場景不崩潰但所有東西縮成一個點（正常）
Step 3  Main.cs 實體視覺位置＋尺寸（S-3）
        → 玩家/敵人出現在正確位置
Step 4  Main.cs 滑鼠轉換（S-4）
        → 採掘/放置恢復正確
Step 5  OrthoZoom + Camera 臂長（S-5, S-6）
        → 視野恢復正常
Step 6  確認 DroppedItems、Projectile 視覺（S-7, S-8）
Step 7  dotnet build 0 errors；遊戲內測試採掘凹坑形狀
Step 8  調整 TileSize 值（1/32 or 1/64），確認球體平滑度
Step 9  更新 實作進度.md
```

---

*最後更新：2026-06-12*
