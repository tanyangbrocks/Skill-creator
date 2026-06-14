# 3D 化實作計畫

> 撰寫日期：2026-06-11  
> 分析基礎：完整 codebase 掃描（16 個受影響檔案）+ 業界研究（Noita、Teardown、Godot 4 voxel 生態）

---

## 零、總結一頁版

| 面向 | 評估 |
|------|------|
| **技術可行性** | ✅ 高 — 架構無根本障礙，演算法可直接延伸 |
| **最大工作量** | 渲染管線重寫（80-120h）+ Chunk 系統（60-100h） |
| **最大風險** | CA 物理平衡重調、效能（無 Chunk 系統則 3D 不可行） |
| **機械性修改量** | ~120 個 GridPos 替換點，~35% 可自動化腳本處理 |
| **現在動 vs 以後動** | **現在動**：技能內容越多，以後改越貴 |
| **AAA 視覺路線** | 雙層架構（物理 Voxel 隱形 + 視覺 Mesh 獨立）是務實路線 |
| **總估算** | 300–500 工時（依選擇的渲染方案差距大） |

---

## 一、五個關鍵決策

在開始任何程式碼之前必須確認的設計決策，後面所有事情都依賴這些。

### 決策 1：重力軸方向

| 選項 | 影響 |
|------|------|
| **保持 Y 向下（推薦）** | CA 重力邏輯幾乎不變，`_vy` 負值仍代表「向上」 |
| 改 Z 向下（Godot 標準） | 需翻轉整個 CA 流向邏輯，工作量×2 |

**決策：保持 Y 向下（Godot 場景 Y 軸朝下）**

---

### 決策 2：CA 鄰接方式

| 選項 | CA 流動 | 效能 |
|------|--------|------|
| **6-鄰接（推薦）** | ±X、±Y、±Z 各一個方向，液體/粉末擴散合理 | 節省 77%（vs 26-鄰接） |
| 26-鄰接 | 對角線也擴散，更「自然」但難以控制 | 最慢 |

**決策：6-鄰接**（沙往下 + 水平 4 方向擴散，與現有 2D 行為最接近）

---

### 決策 3：渲染架構

| 方案 | 開發時間 | 視覺品質 | 動態破壞 | 適用時期 |
|------|----------|----------|----------|----------|
| **A. GridMap**（Godot 內建）| 2-3 週 | 中 | ❌ 困難 | 不推薦 |
| **B. ArrayMesh + Greedy Meshing**（推薦近期）| 6-8 週 | 中高 | ✅ | Phase 1-2 |
| **C. Compute Shader + DDA Raymarching**（Teardown 風格）| 16-24 週 | AAA | ✅ | Phase 3-4 |
| **D. 引入 zylann/godot_voxel**（GDExtension）| 4-5 週（學習）| 高 | ✅ | 備選 |

**決策：B 方案作為起點，預留 C 方案的升級路徑**

---

### 決策 4：Chunk 大小

| 大小 | 記憶體/chunk | LOD | Tick 邊界複雜度 |
|------|-------------|-----|----------------|
| 8³ = 512 格 | ~2 KB | ✅ 優 | 複雜 |
| **16³ = 4096 格（推薦）** | ~16 KB | ✅ 良 | 中等 |
| 32³ = 32768 格 | ~128 KB | 中 | 簡單 |

**決策：16³ chunks**

---

### 決策 5：CA 計算路徑

| 路線 | Phase 1 | Phase 3 |
|------|---------|---------|
| **CPU → GPU 升級路線（推薦）** | C# Array + dirty AABB，先驗證邏輯 | Godot 4 Compute Shader + Margolus Block CA |
| 直接 GPU | 複雜度高，難以除錯 | - |

**決策：先 CPU 路徑，Phase 2 後移 GPU**

---

## 二、架構設計

### 2-1 三層架構（長期目標）

```
┌─────────────────────────────────────────────┐
│  視覺層 Visual Layer                          │
│  • 高多邊形 mesh（艾爾登法環品質）             │
│  • PBR 材質、法線貼圖、LOD                    │
│  • Node3D + MeshInstance3D                   │
│  • 不直接參與物理計算                         │
└──────────────┬──────────────────────────────┘
               │ 雙向鏈結：voxel 移除 ↔ mesh 遮罩更新
┌──────────────▼──────────────────────────────┐
│  物理 Voxel 層 Physics Layer                  │
│  • 16³ chunks，active zone 128³              │
│  • Dirty AABB 優化，只 Tick 有變動的區域      │
│  • TileWorld3D（CA 沙粒/液體/氣體）           │
│  • 不顯示（invisible）                        │
└──────────────┬──────────────────────────────┘
               │ 爆炸 → voxel 移除
┌──────────────▼──────────────────────────────┐
│  碎石層 Debris Layer                          │
│  • RigidBody3D 碎石（可撿起、可扔出）         │
│  • 由物理層 voxel 移除時 Spawn                │
│  • 數量限制（Pool），距離超過範圍自動回收      │
└─────────────────────────────────────────────┘
```

**近期目標**（Phase 1-2）：只實作物理層 + 簡單 ArrayMesh 渲染，不做雙層鏈結。  
**中期目標**（Phase 3）：加入視覺層雙層架構 + 碎石系統。  
**長期目標**（Phase 4）：GPU compute shader CA + DDA raymarching 渲染。

---

### 2-2 Chunk 系統設計

```csharp
// 核心資料結構（設計草稿）
public sealed class Chunk3D
{
    public const int Size = 16;

    public Vector3I ChunkCoord { get; }
    public TileCell[] Cells { get; } = new TileCell[Size * Size * Size];

    // Dirty AABB — 只更新有變動的子區域
    public bool  IsDirty      { get; set; }
    public int   DirtyMinX, DirtyMinY, DirtyMinZ;
    public int   DirtyMaxX, DirtyMaxY, DirtyMaxZ;

    // Mesh 狀態
    public bool  MeshNeedsRebuild { get; set; }
    public MeshInstance3D? MeshNode { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Idx(int x, int y, int z) => z * Size * Size + y * Size + x;

    public void MarkDirty(int x, int y, int z)
    {
        IsDirty   = true;
        DirtyMinX = Math.Min(DirtyMinX, x); DirtyMaxX = Math.Max(DirtyMaxX, x);
        DirtyMinY = Math.Min(DirtyMinY, y); DirtyMaxY = Math.Max(DirtyMaxY, y);
        DirtyMinZ = Math.Min(DirtyMinZ, z); DirtyMaxZ = Math.Max(DirtyMaxZ, z);
    }
}

public sealed class TileWorld3D
{
    public int Width  { get; }
    public int Height { get; }
    public int Depth  { get; }

    private readonly Dictionary<Vector3I, Chunk3D> _activeChunks = new();

    // 每幀只 Tick 玩家附近的 chunk（active radius）
    private const int ActiveRadius = 4;  // 單邊 4 chunk = 64 格範圍

    public void Tick(Vector3I playerChunkCoord)
    {
        foreach (var (coord, chunk) in _activeChunks)
        {
            if (!chunk.IsDirty) continue;
            if (ManhattanDist(coord, playerChunkCoord) > ActiveRadius) continue;
            TickChunk(chunk);
            chunk.IsDirty = false;
        }
    }
}
```

---

### 2-3 CA 更新管線（Dirty AABB）

Noita 的核心優化，3D 版本：

```
每幀：
  1. 從 Y 底部往上掃描（重力方向）
  2. Z 軸奇偶交錯（防止沙粒雙重移動 bug）
  3. 只遍歷 chunk.DirtyAABB 內的格子
  4. TryMove 移動後：
     - 標記源 chunk dirty（消除點）
     - 標記目標 chunk dirty（落點）
     - 若跨 chunk 邊界：標記鄰近 chunk dirty
  5. 幀末重設所有 dirty flag

邊界規則（chunk 跨越）：
  - 邊界格子的移動：需要原子操作或 sequential pass
  - 建議：先更新偶數 chunk，再更新奇數 chunk（Margolus 方法）
```

---

### 2-4 GridPos3D 設計

```csharp
public readonly record struct GridPos3D(int X, int Y, int Z)
{
    public static GridPos3D operator +(GridPos3D a, GridPos3D b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static GridPos3D operator -(GridPos3D a, GridPos3D b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public float DistanceTo(GridPos3D other)
    {
        int dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return MathF.Sqrt(dx*dx + dy*dy + dz*dz);
    }

    // 向下相容：2D 操作保留 Z
    public GridPos3D MoveX(int dx) => new(X + dx, Y, Z);
    public GridPos3D MoveY(int dy) => new(X, Y + dy, Z);
    public GridPos3D MoveZ(int dz) => new(X, Y, Z + dz);

    // 轉換到世界空間（用於 Godot 場景節點）
    public Vector3 ToWorldPos(float tileSize = 1f) =>
        new(X * tileSize, Y * tileSize, Z * tileSize);
}

// 6-鄰接偏移（CA 用）
public static readonly (int dx, int dy, int dz)[] Neighbors6 =
{
    ( 0, -1,  0),  // 下（重力方向）
    ( 0,  1,  0),  // 上
    (-1,  0,  0),  // 左
    ( 1,  0,  0),  // 右
    ( 0,  0, -1),  // 前
    ( 0,  0,  1),  // 後
};
```

---

## 三、各元件修改評估表

| 元件 | 檔案 | 修改量 | 風險 | 自動化率 | 工時估算 |
|------|------|--------|------|----------|---------|
| **GridPos → GridPos3D** | GridPos.cs + 120+ 處 | 中 | 中 | 70% | 10-15h |
| **TileWorld CA 核心** | TileWorld.cs | 大 | 高 | 10% | 40-60h |
| **Chunk 系統（新增）** | Chunk3D.cs（新）| 大 | 高 | 0% | 60-100h |
| **MapGenerator** | MapGenerator.cs | 中 | 中 | 20% | 15-25h |
| **渲染管線** | TileWorldRenderer.cs（重寫）| 超大 | 高 | 0% | 80-120h |
| **Camera 3D** | Main.cs | 小 | 低 | 0% | 3-5h |
| **滑鼠世界座標** | Main.cs | 中 | 中 | 0% | 5-8h |
| **PlayerController** | PlayerController.cs | 中 | 中 | 60% | 10-15h |
| **Enemy AI** | Enemy.cs + EnemyManager.cs | 中 | 中 | 50% | 10-15h |
| **SpellCaster 技能** | SpellCaster.cs | 中 | 中 | 60% | 10-15h |
| **IWorldInterface** | IWorldInterface.cs | 小 | 低 | 90% | 2-3h |
| **SpellRunner** | SpellRunner.cs | 小 | 低 | 60% | 3-5h |
| **SaveSystem** | SaveSystem.cs | 極小 | 低 | 80% | 2-3h |
| **SnapshotManager** | SnapshotManager.cs | 小 | 低 | 70% | 3-5h |
| **CA 物理平衡重調** | — | — | 高 | 0% | 20-40h |
| **測試 + 整合** | — | — | 中 | 0% | 40-60h |
| **總計** | **~16 檔案** | **大** | **高** | **~35%** | **≈317-494h** |

### 完全不需修改的部分

- AbilityEditorUI / ScratchCanvas（技能編輯器 UI）
- 物品系統（Inventory、ItemRegistry）
- 數值系統（CharacterStats、CharacterState）
- 事件系統（EventBus、ActionBus）
- 所有 UI HUD 元件
- BlockScript / SpellCompiler（技能積木邏輯）

---

## 四、隱藏的 2D 假設（必須手動處理）

這些是最容易被遺漏的「陷阱」：

| 假設 | 位置 | 修正方式 |
|------|------|---------|
| 爆炸範圍 `dx²+dy²≤r²` | EnemyManager, TileWorld | 改 `dx²+dy²+dz²≤r²` |
| 滑鼠座標 `canvasMouse / TilePixels` | Main.cs | 改為從 Camera3D 向 world plane 射線投影 |
| 敵人 Manhattan 距離 `Math.Abs(X-px) + Math.Abs(Y-py)` | Enemy.cs | 改 3D 歐氏距離或加 Z 分量 |
| Camera 跟隨 `Vector2 cameraPos` | Main.cs | 改 `Vector3` + Camera3D |
| `Facing = new GridPos(Math.Sign(dx), 0)` | PlayerController | 決定 Facing 是否需要 Z 分量 |
| 射線施放沿 XY 平面 | SpellCaster Raycast 呼叫 | 加 dirZ 參數（近期可默認 0） |

---

## 五、分階段實作計畫

### Phase 0 — 基礎設施（2-3 週）
**目標：確認技術決策正確，建立可測試的最小原型**

- [ ] 建立 `GridPos3D` struct（含運算子、DistanceTo、轉換方法）
- [ ] 建立 `Chunk3D` class（16³，含 Dirty AABB 標記）
- [ ] 建立最小版 `TileWorld3D`（無渲染，純邏輯）：
  - 沙粒（Powder）單一材質
  - 6-鄰接移動
  - 8³ 小世界驗證
- [ ] 單元測試：驗證索引公式、邊界處理
- [ ] **驗收標準**：8³ 世界的沙粒在重力下正確堆積，無邊界 bug

---

### Phase 1 — CA 核心 3D 化（6-8 週）
**目標：完整 CA 系統可在 64³ active 範圍跑 30+ FPS**

- [ ] `TileWorld` → `TileWorld3D` 完整移植
  - 索引公式：`z * W * H + y * W + x`
  - `UpdatePowder` / `UpdateLiquid` / `UpdateGas` 改 6-鄰接
  - `Explode` 改球形 `dx²+dy²+dz²≤r²`
  - `Raycast` 改 Amanatides-Woo 3D DDA
  - `SnapshotRegion` 改球形
- [ ] Chunk 系統實作（Dirty AABB + Active Zone）
- [ ] `GridPos` → `GridPos3D` 全專案替換
- [ ] `IWorldInterface` 方法簽名更新
- [ ] `MapGenerator` 基礎 3D 化
  - Heightmap 保留（基礎地表層，Z=0）
  - CA 洞穴改 3D（`bool[,,]` + 3D 平滑）
  - FloodFill 改 6-鄰接
  - 礦脈 BFS 改 6-鄰接
- [ ] 執行腳本自動替換（見第六章）
- [ ] **驗收標準**：能生成 3D 地圖，沙/水/氣體行為正確

---

### Phase 2 — 渲染 + 玩家/敵人（6-8 週）
**目標：可以跑起來看到效果**

#### 渲染 / 鏡頭
- [x] `TileWorldRenderer3D`（ArrayMesh + Greedy Meshing per chunk）
- [x] Camera3D 四視角（TPS / FPS / Isometric / SideScroll2D，Tab 循環）
- [x] 滑鼠 → 世界座標（Camera3D 射線投影，SideScroll2D 模式）
- [x] WASD 相機相對方向移動（斜向支援，Camera.Yaw 計算 fwd/right 向量）
- [x] 第一人稱 FPS：相機置於眼部往外看；切入 FPS 隱藏玩家 mesh

#### 玩家
- [x] `PlayerController` 改 3D 座標（X/Y/Z，`TryMoveDepth`，跳躍物理）
- [x] `Facing` 改 3D（X/Y/Z，`TryMoveDir` 更新 XZ 面向）
- [x] 地形跨坡：一格爬升邏輯（`TryMoveDir`）

#### 敵人 / AI
- [ ] `Enemy` / `EnemyManager` 改 3D 座標
  - 先用「平面追逐」（XZ 平面，保持現有邏輯）
  - 敵人 mesh Z 跟隨 enemy.Position.Z ← 已完成視覺部分，AI 邏輯待補

#### 技能系統
- [ ] `SpellCaster` / `SpellRunner` 投射物方向改用 3D Facing / Camera 方向
- [ ] 投射物在玩家實際 Z 層飛行（`EnemyProjectile` / `SpellProjectile` Z 同步）
- [ ] CA 特效在玩家實際 Z 層播放（爆炸、採礦粉塵、元素反應）

#### 採掘 / 互動
- [ ] 3D 視角下採掘目標定位：從相機方向做 3D DDA Raycast，取最近實體格
  - SideScroll2D 模式維持現有滑鼠 XY 投影邏輯

- [ ] **驗收標準**：玩家可在 3D 世界中自由移動、施放技能擊殺敵人、採掘方塊

---

### Phase 3 — 效能優化（4-6 週）
**目標：128³ active zone 穩定 60 FPS**

- [ ] Godot 4 Compute Shader CA（Margolus Block CA）
  - 把 CA Tick 移至 GPU：`RenderingDevice.ComputeListBegin`
  - 參考：[GelamiSalami/GPU-Falling-Sand-CA](https://github.com/GelamiSalami/GPU-Falling-Sand-CA) 的 3D 擴充版
  - 估算：GPU 比 CPU 快 12-38 倍，128³ zone 可達 60 FPS
- [ ] C# Array Pool（避免 GC 壓力）
- [ ] 多執行緒 Chunk Mesh 重建（`Task.Run` 非同步）
- [ ] LOD：遠處 chunk 降解析度或凍結
- [ ] **驗收標準**：256³ world，128³ active zone，穩定 60 FPS

---

---

## 未來方向（AAA 視覺，時機成熟再規劃）

> 以下為長期願景，不列入當前 Phase 排程。待 Phase 3 效能穩定後再評估優先序。

### 雙層架構 + 高精度視覺

- 高精度 Visual Mesh（城堡、角色模型等）+ 隱形 Physics Voxel Layer 雙層鏈結
- 碎石系統（Debris Pool，RigidBody3D，上限 200 個）
- 建築連通性分析（爆炸後整片牆倒塌成獨立 RigidBody）

### 渲染升級

- Godot 4 `SDFGI` / `VoxelGI` 接入（內建，啟用即可）
- DDA Raymarching Shader（Teardown 風格，完全選擇性）
  - 參考：[viktor-ferenczi/godot-voxel](https://github.com/viktor-ferenczi/godot-voxel)
- GPU path tracing（Godot 4.4+ Vulkan RT，實驗中）

### 飛行機制（與技能系統整合後）

- 玩家 / 投射物往立體空間任意方向移動（`TryMove3D(dx, dy, dz)`，浮點座標）
- Camera pitch 軸直接對應垂直飛行方向
- 重力可由技能 / 狀態動態切換

---

## 六、自動化腳本設計

分析後的結論：**建議寫腳本處理機械性替換，但不要嘗試自動化整個 3D 化流程**。

### 可自動化的部分（約 35%）

**腳本 1：GridPos → GridPos3D 型別替換**
```powershell
# 注意：只替換明確的型別宣告，不替換建構子（需手工確認 Z 來源）
$files = Get-ChildItem "Scripts\" -Recurse -Filter "*.cs"
foreach ($f in $files) {
    $content = Get-Content $f.FullName -Raw
    # 型別宣告替換
    $content = $content -replace '\bGridPos\b(?!\d)', 'GridPos3D'
    # 保留特例（如果有純 2D 用途的保留區）
    Set-Content $f.FullName $content -Encoding utf8
}
```

**腳本 2：GridPos 建構子插入 Z=0 佔位符（需人工審查後刪除佔位符）**
```powershell
# 把 new GridPos(x, y) 改為 new GridPos3D(x, y, /*Z=?*/0)
$content -replace 'new GridPos\(([^,)]+),\s*([^)]+)\)',
                  'new GridPos3D($1, $2, /*Z=?*/0)'
```

**腳本 3：IWorldInterface 方法簽名批次確認**
```powershell
# 找出所有需要加 Z 參數的方法，輸出清單供人工處理
Select-String -Path "Scripts\**\*.cs" -Pattern "GridPos" |
    Select-Object Filename, LineNumber, Line |
    Export-Csv "gridpos-migration.csv" -Encoding utf8
```

### 必須手工處理的部分（65%）

| 任務 | 原因 |
|------|------|
| `Idx(x, y)` 的 Z 來源 | Z 值要從 entity.Position.Z 取，不是永遠 0 |
| CA 演算法邏輯（UpdatePowder 等）| 需理解重力/流動語意 |
| Raycast DDA 3D 重寫 | 新演算法（見下方） |
| 渲染管線 | 完全重寫，無機械性替換 |
| 滑鼠 → 世界座標 | Camera3D 射線邏輯 |
| CA 物理參數重調 | 需實驗 |

### Raycast 3D DDA 標準實作（直接使用）

```csharp
public (GridPos3D Hit, int MatId, bool DidHit) Raycast(
    GridPos3D start, float dirX, float dirY, float dirZ, float maxDist)
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
        var mat = TypeAt(tx, ty, tz);
        if (mat != MaterialType.Air)
            return (new GridPos3D(tx, ty, tz), (int)mat, true);
    }
    return (start, 0, false);
}
```

---

## 七、長期 AAA 視覺技術路線圖

### 可破壞城堡完整技術鏈

```
設計師製作城堡 →
  視覺 Mesh（Blender 高精度建模）
  + 對應的 Physics Voxel Map（工具自動生成或手刷）

遊戲執行時：
  城堡 = MeshInstance3D（高精度）+ invisible Chunk3D（物理層）

技能爆炸流程：
  1. SpellCaster: Explode(center, radius)
  2. TileWorld3D: 球形區域 voxel → Air
  3. VisualDestructionManager:
     a. 遍歷被移除的 voxel 座標
     b. 通知對應 visual mesh 更新 vertex mask（或拆分 sub-mesh）
     c. Spawn RigidBody3D 碎石（從 voxel 座標位置，隨機形狀/大小）
  4. ConnectivityAnalyzer:
     a. 分析剩餘結構連通性
     b. 斷裂的部分分離成獨立 RigidBody3D（整片牆倒塌）
  5. 碎石 Pool 管理（最多 200 個 RigidBody3D 同時存在）

玩家撿拾碎石：
  Interact → 碎石進物品欄（InventoryItem）
  扔出 → SpawnProjectile，走現有 SpellCaster 投射物邏輯
```

### 渲染升級路徑

```
近期（Phase 2）：
  ArrayMesh + Greedy Meshing
  → 標準 PBR 材質
  → Godot 4 SDFGI（軟 GI，內建免費）

中期（Phase 3）：
  GPU Compute Shader CA
  → 維持 ArrayMesh 渲染
  → Godot 4 VoxelGI（烘焙 GI）

長期（Phase 4，選擇性）：
  DDA Raymarching Shader（Teardown 風格）
  → 整個 world 改成 voxel volume texture
  → GPU path tracing（Godot 4.4 Vulkan RT，實驗中）
  → SVDAG 大世界（參考 Aokana, arxiv 2505.02017）
```

**重要提醒**：不需要在 Phase 4 複現完整的 Teardown — Teardown 是 Dennis Gustafsson 一個人花 5 年自訂引擎的成果。雙層架構可以用較少工時達到視覺上「接近 AAA」的效果（城堡有精美建模，破壞時掉落碎石），而且開發難度低 5-10 倍。

---

## 八、效能預測

### 各方案可行規模

| 配置 | CA Active 格數 | Tick 耗時估算 | 是否可行 |
|------|---------------|--------------|---------|
| 3D，無 Chunk，CPU | 6,000,000 | ~300ms | ❌ 不可行 |
| 3D + Chunk（CPU）| ~32,768（32 chunk）| ~18ms | ⚠️ 勉強 |
| 3D + Chunk + dirty AABB | ~4,000（active）| ~5ms | ✅ 可用 |
| 3D + Chunk + GPU（Margolus）| ~524,288（128³）| ~3ms | ✅ 理想 |

**結論：Chunk 系統是先決條件，不做就不能 3D。**

### 記憶體估算（16³ chunks，128³ active zone）

```
TileCell = 4 bytes（MaterialType: 1 byte, Variant: 1 byte, Flags: 2 bytes）
Active zone: 128³ = 2,097,152 格 × 4 bytes = 8 MB（可接受）
Full world (512×256×512): 67M 格 × 4 bytes = 268 MB（未壓縮）
→ 需要 chunk 壓縮（空氣格 run-length encoding，可減少 50-80%）
```

---

## 九、開始動作的第一步

**本週：**
1. 建立 `Scripts/World/GridPos3D.cs`（直接用第二章的設計）
2. 建立 `Scripts/World/Chunk3D.cs`（minimal version）
3. 建立 `Scripts/World/TileWorld3D.cs`（8³ 小世界，沙粒，不接渲染）
4. 寫 unit test：放一塊沙粒，驗證它在重力下正確下落

**下週：**
5. 執行腳本（`gridpos-migration.csv`），整理 120 個替換點
6. 決定渲染方案（ArrayMesh Greedy Meshing 的學習成本評估）

**第 3-4 週：**
7. 接通渲染，看到第一個 3D 世界畫面
8. 把 `MapGenerator` 的 Heightmap 生成接入 3D 世界

**不要在 Phase 0/1 碰渲染以外的部分。先讓 CA 邏輯跑起來，再把畫面接上去。**

---

## 十、參考資源

| 資源 | 用途 |
|------|------|
| [ccrock4t/3DCellularWorld](https://github.com/ccrock4t/3DCellularWorld) | 最接近 Noita 精神的 3D CA，Unity/C# 可參考 |
| [io12/Sandvox](https://github.com/io12/Sandvox) | 輕量 3D falling-sand（Rust，邏輯清晰） |
| [GelamiSalami/GPU-Falling-Sand-CA](https://github.com/GelamiSalami/GPU-Falling-Sand-CA) | Margolus Block CA GPU 實作（2D，可 3D 化）|
| [Zylann/godot_voxel](https://github.com/Zylann/godot_voxel) | Godot 4 最完整的 voxel GDExtension |
| [viktor-ferenczi/godot-voxel](https://github.com/viktor-ferenczi/godot-voxel) | DDA Raymarching shader（Teardown 風格） |
| [Teardown 技術解析 (Acko.net)](https://acko.net/blog/teardown-frame-teardown/) | Teardown 渲染架構最詳細的逆向分析 |
| [Voxagon Blog](https://blog.voxagon.se/) | Dennis Gustafsson（Teardown 開發者）技術 blog |
| [GDC 2019 Noita](https://www.youtube.com/watch?v=prXuyMCgbTc) | Noita 作者講 CA + dirty chunk 的原始演講 |
| [Aokana arxiv 2505.02017](https://arxiv.org/abs/2505.02017) | 2025 最新 SVDAG 大世界 voxel 渲染研究 |
| [Greedy Meshing (nickmcd.me)](https://nickmcd.me/2021/04/04/high-performance-voxel-engine/) | Greedy Meshing + Vertex Pooling 詳解 |
