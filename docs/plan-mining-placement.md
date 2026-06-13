# plan-mining-placement.md — Step 10 R 系列・採掘放置深化實作計畫

> **文件來源**：`origin text setting/dig & place.txt` + `plan-scale-world.md §R series`
> **建立日期**：2026-06-12
> **依賴計畫**：P 系列（WorldScale 尺度）✅ · W 系列（世界生成）✅ · E 系列（GPU CA）✅

---

## 一、設計文件 vs 現有系統：差距總覽

### 現有系統現況（快照）

| 系統 | 現有程式碼 | 說明 |
|------|-----------|------|
| 採掘 | `PlayerController.TickMining()` → `TileWorld3D.DestroyTile()` → `OnTileDestroyed` | 工具 Tier 檢查、Hardness 累積、完成後觸發掉落 |
| 掉落物 | `DroppedItemManager.Spawn(pos, mat)` | MaterialRegistry drop table、2 格內自動拾取 |
| 放置 | `ItemData.IsPlaceable + PlaceAs` + `Main.cs` 右鍵 | 單 tile 放置，無形狀、無面法線 |
| Raycast | `TileWorld3D.Raycast(start, dx,dy,dz, maxDist)` | DDA 算法，回傳 `(Hit, MatId, DidHit)`，**無面法線** |
| 物品系統 | `ItemRegistry`（enum flat table）、MaxStack=99 | 無 Fragment 類型 |
| 放置規則 | 無（任何非 Air 格均拒絕，但水亦可放） | 缺少放置地點驗證器 |

---

## 二、評估總覽表

| dig & place 設計項目 | 現有 | 缺少 | 難度 | 優先 |
|----------------------|------|------|------|------|
| **1. 挖掘留坑（tile level）** | TickMining ✓ | — | — | ✅ 已完成 |
| 坑洞邊緣不規則像素 | 無 | sub-tile 噪音渲染（V 系列） | Hard | 🔵 V 系列 |
| 相鄰坑洞視覺連通 | Greedy Meshing 已合併相同材質 ✓ | — | — | ✅ 視覺已完成 |
| 掉落物 Minecraft 式拾取 | DroppedItemManager ✓ | — | — | ✅ 已完成 |
| **2. 放置形狀（球/立方體/錐/柱）** | 單 tile PlaceAs ✓（尺度錯誤）| PlacementShape 系統：每次動作 = 1 material unit（radius=5, ~520–1000 tiles）、形狀預覽 | Medium | 🟡 R-4 |
| 相鄰形狀材質視覺無縫 | Greedy Meshing 已處理相鄰同材質 ✓ | 接合幾何補填（Junction Fill） | Medium | 🔵 V 系列（見 R-4-RENDER 分析） |
| 重塑動作（推拉塑形） | 無 | 體素雕刻系統（Blender 概念） | Hard | 🔴 未來方向 |
| 放置地點規則（水不可放等） | 無驗證器 | `PlacementValidator` 靜態類別 | Easy | 🟢 R-3+ |
| **3. 碎片系統（外力爆裂→碎塊）** | 無（爆炸直接 Set→Air 不觸發事件） | `DestroyReason`、爆炸掉落碎片 | Medium | 🟡 R-5 |
| Fragment 物品類型 | 無 FragmentXxx ItemId | 新 ItemId 變體 + ItemData | Medium | 🟡 R-5 |
| 碎片 1% 質量門檻（= 10 tiles）¹ | 無 | 整數 tile 表示即可，無次 tile 問題² | Easy | 🟢 R-5 同批 |
| 100 碎片 → 1 材質合成 | 無 | 合成系統 | Hard | 🔴 合成系統範疇 |
| **4. 堆疊上限 99999** | MaxStack=99 | 改常數 + UI 標籤寬度 | Easy | 🟢 R-1 同批 |
| **R-1 Raycast 面法線** | 回傳 `(Hit, MatId, bool)` | `FaceNormal` 第三返回值 | Easy | 🟢 R-1 |
| **R-2 TPS/FPS 採掘改 Raycast** | MouseGridPos 螢幕投影 | `GetCenterRay()` + `MouseFaceNormal` | Easy | 🟢 R-2 |
| **R-3 face-aligned 放置** | 放置在 MouseGridPos | `MouseGridPos + MouseFaceNormal` 偏移 | Easy | 🟢 R-3 |

---

## 三、分層實作計畫

---

### 🟢 第一層：立即可做（Step 10 主線）

#### R-1：Raycast 補充面法線

**問題**：DDA loop 未記錄最後步進的軸向。

**改動**（`TileWorld3D.cs`，~15 行）：
```csharp
// 在 DDA loop 中追蹤最後步進的軸（0=X, 1=Y, 2=Z）
int lastAxis = 1; // 預設 Y（往下射）
for (float dist = 0f; dist < maxDist;)
{
    if (sX < sY && sX < sZ) { dist = sX; sX += dX; tx += stepX; lastAxis = 0; }
    else if (sY < sZ)        { dist = sY; sY += dY; ty += stepY; lastAxis = 1; }
    else                     { dist = sZ; sZ += dZ; tz += stepZ; lastAxis = 2; }

    if (dist > maxDist || !InBounds(tx, ty, tz)) break;
    if (GetTile(tx, ty, tz) != MaterialType.Air)
    {
        // FaceNormal = 進入方向的反向（ray 從哪個方向射入就往那方向回報）
        var norm = lastAxis switch
        {
            0 => new GridPos(-stepX, 0, 0),
            1 => new GridPos(0, -stepY, 0),
            _ => new GridPos(0, 0, -stepZ),
        };
        return (new GridPos(tx, ty, tz), (int)mat, norm, true);
    }
}
```

**新 signature**：
```csharp
public (GridPos Hit, int MatId, GridPos FaceNormal, bool DidHit) Raycast(
    GridPos start, float dirX, float dirY, float dirZ, float maxDist)
```

**向後兼容**：2D 包裝器加第三返回值；舊呼叫處解構時補 `_` 佔位即可。

---

#### R-2：TPS/FPS 採掘改 Raycast

**改動 1**（`CameraController.cs`，~6 行）：
```csharp
public (Vector3 Origin, Vector3 Dir) GetCenterRay()
{
    var mid = GetViewport().GetVisibleRect().GetCenter();
    return (_cam.ProjectRayOrigin(mid), _cam.ProjectRayNormal(mid));
}
```

**改動 2**（`PlayerController.cs`，~1 行）：
```csharp
public GridPos MouseFaceNormal { get; set; } = new GridPos(0, -1, 0);
```

**改動 3**（`Main.cs _Process()`，~10 行）：
```csharp
if (_camera3d.Mode is not CameraController.CameraMode.SideScroll2D)
{
    float T = TileWorldConstants.TileSize;
    var (ro, rd) = _camera3d.GetCenterRay();
    var (hit, _, norm, ok) = _world3d.Raycast(
        new GridPos((int)(ro.X/T), (int)(ro.Y/T), (int)(ro.Z/T)),
        rd.X, rd.Y, rd.Z,
        maxDist: (int)(PlayerController.MiningRange * 2));
    if (ok) { _player.MouseGridPos = hit; _player.MouseFaceNormal = norm; }
}
```

---

#### R-3：放置使用面法線偏移

**改動**（`Main.cs`，放置邏輯區塊，~5 行）：
```csharp
// 計算放置目標 = 滑鼠指向格 + 面法線
var placeTarget = new GridPos(
    _player.MouseGridPos.X + _player.MouseFaceNormal.X,
    _player.MouseGridPos.Y + _player.MouseFaceNormal.Y,
    _player.MouseGridPos.Z + _player.MouseFaceNormal.Z);
// 用 placeTarget 取代原本的 target 做放置
```

#### R-3+：PlacementValidator — 水不可放置 + 規則基礎

**新檔案**（`Scripts/World/PlacementValidator.cs`，~30 行）：
```csharp
public static class PlacementValidator
{
    public static bool CanPlace(TileWorld3D world, GridPos pos, MaterialType mat)
    {
        if (!world.InBounds(pos.X, pos.Y, pos.Z)) return false;
        var existing = world.GetTile(pos.X, pos.Y, pos.Z);
        // 目前只有 Air 可放（水上不可放；未來擴充白名單）
        return existing == MaterialType.Air;
    }
}
```

以後每增加一種「可放在水上的物品」，在此類別擴充，不需修改 Main.cs 主邏輯。

#### 堆疊上限 99999（1 行修改）

`ItemRegistry.cs`：所有方塊/礦石的 `MaxStack` 參數由 `99` 改為 `99999`。
UI 注意：`_hotbarCounts[i].Text = $"×{stack.Count}"` 標籤需確認不超出熱鍵欄格寬（建議壓縮字體或縮寫 `×99k`）。

---

### 🟡 第二層：需先備條件（Step 10 後續 / R-4、R-5）

#### R-4：多形狀放置（PlacementShape 系統）

**先備**：R-3 face-aligned 放置完成。

---

##### R-4-UI：形狀切換介面（N 鍵選單）

**互動流程**：
1. 玩家按下 `N` → 螢幕中央彈出「放置形狀選單」面板
2. 面板顯示所有形狀選項（圖示 + 文字），滑鼠懸停高亮，左鍵點選確認
3. 選擇後面板關閉；再按 `N` 或點選同一項也關閉
4. HUD 左下角顯示目前形狀指示器（如 `⬜ 立方體` / `● 球體`）
5. **預設**：`PlacementShape.Cube`（radius=5，1 次放置 ≈ 1 material unit ≈ 1000 tiles）

**形狀選項清單**：

| 顯示名稱 | enum 值 | 說明 |
|----------|---------|------|
| 單格 | `Single` | 固定 1 tile，等同 R-3 face-aligned 放置（精細修補用） |
| 立方體（預設） | `Cube` | 2r+1 邊長的正立方體，radius=5 → 11³ ≈ 1331 tiles |
| 球體 | `Sphere` | `dx²+dy²+dz² ≤ r²` |
| 圓柱 | `Cylinder` | XZ 圓、Y 方向延伸 |
| 圓錐 | `Cone` | Y 向尖頂，底面圓形 |
| 三角錐 | `TriPyramid` | 三稜錐，適合地形裝飾 |

半徑：預設 radius=5（對應 PlayerH/3 ≈ 10 tiles 直徑，1 次動作 = 1 material unit）；可後續加熱鍵 `[` / `]` 調整。

**UI 實作要點**（`Main.cs` 加 `_shapePanel`）：
```csharp
// N 鍵切換
if (Input.IsActionJustPressed("shape_menu"))
    _shapePanel.Visible = !_shapePanel.Visible;

// 各選項按鈕
btn.Pressed += () => {
    _activeShape = shape;
    UpdateShapeIndicator();
    _shapePanel.Visible = false;
};
```

---

##### R-4-RENDER：不同形狀相鄰的視覺無縫問題

**問題描述**：玩家先放一顆石頭球體，再放一個石頭圓錐貼在球體旁，兩者接觸邊是否有「縫隙感」？

**分析（三種案例）**：

| 案例 | 結果 | 原因 |
|------|------|------|
| 同材質、形狀連通 | ✅ 無縫 | Greedy Meshing 合併相鄰同材質面，渲染層完全無縫 |
| 同材質、形狀相切但有 1-tile 空隙 | ❌ 有空隙 | 空隙處是 Air tile，本來就該空著（設計問題，非 bug） |
| 不同材質相鄰 | 顯示材質邊界 | 符合預期（石頭 ≠ 泥土） |

**關鍵結論**：從**渲染層**來看，「形狀工具」只是一次放置多個 tile 的快捷鍵。一旦 tile 落地，世界不記得它是球還是錐——全部都只是普通 tile。Greedy Meshing 處理的對象是 tile，不是形狀，因此**相同材質、tile 相鄰 → 自動無縫**，不需要額外程式碼。

**真正的接合問題（幾何層）**：

當玩家把一個球體（半徑 3）與一個圓錐底部（底面半徑 5）相接時：
- 球體的「頂點」截面只有約 1 tile 寬
- 圓錐底面卻有 5×5 tile 寬
- 結果：圓錐底面外圈的 tile 懸在球體外側，只有中心 1 tile 連接

這是**幾何設計問題**，不是渲染 bug。解法：

1. **玩家自主選型**（預設）：文件說明「圓弧形狀的接合面需半徑匹配」；圓柱-圓柱同半徑接合天然無縫，球-錐接合則視半徑決定外觀
2. **接合補填（Junction Fill）**（V 系列預留）：放置形狀時，偵測面法線方向的接觸面，自動補上接合 tile；實作為 `PlacementShape.cs` 中的 `FillJunction(world, from, to, mat)` 靜態方法，待 V 系列再加
3. **雕刻工具（R-6 Sculpt）**：讓玩家用 Push/Pull 工具手動補坑，更靈活

**計畫登記**：R-4 實作時**不需要**處理接合問題（幾何問題歸使用者設計），但在 `PlacementShape.cs` 內預留 `// TODO: R-6 FillJunction` 注釋。若未來 V 系列視覺需求明確，再補實作。

---

##### R-4 架構

**新檔案**（`Scripts/World/PlacementShape.cs`）：
```csharp
public enum PlacementShape { Single, Cube, Sphere, Cylinder, Cone, TriPyramid }

public static class ShapeVoxels
{
    // 預設 radius 從 WorldScale 衍生：PlayerH / 6 = 半徑（直徑 = PlayerH/3 ≈ 1 unit 邊長）
    // Grain=16 → PlayerH=32 → radius=5；Grain=32（Phase B）→ PlayerH=64 → radius=10
    // 只要 WorldScale.Grain 一改，此處自動跟進，不需要手動搜改 magic number
    public static IEnumerable<(int dx, int dy, int dz)> GetOffsets(
        PlacementShape shape, int radius = -1)  // -1 = 用 WorldScale 自動算；外部傳入則覆蓋
    {
        if (radius < 0) radius = WorldScale.PlayerH / 6;  // auto: PlayerH/6 → Grain 無關
        return shape switch
        {
            PlacementShape.Sphere    => SphereOffsets(radius),
            PlacementShape.Cube      => CubeOffsets(radius),
            PlacementShape.Cylinder  => CylinderOffsets(radius),
            PlacementShape.Cone      => ConeOffsets(radius),
            PlacementShape.TriPyramid => TriPyramidOffsets(radius),
            _                        => [(0, 0, 0)],  // Single
        };
    }
    private static IEnumerable<(int,int,int)> SphereOffsets(int r)
    {
        for (int dz=-r; dz<=r; dz++)
        for (int dy=-r; dy<=r; dy++)
        for (int dx=-r; dx<=r; dx++)
            if (dx*dx + dy*dy + dz*dz <= r*r)
                yield return (dx, dy, dz);
    }
    private static IEnumerable<(int,int,int)> CubeOffsets(int r)
    {
        for (int dz=-r; dz<=r; dz++)
        for (int dy=-r; dy<=r; dy++)
        for (int dx=-r; dx<=r; dx++)
            yield return (dx, dy, dz);
    }
    // … CylinderOffsets / ConeOffsets / TriPyramidOffsets 同理
    // TODO: R-6 FillJunction — 偵測接觸面並自動補填接合 tile
}
```

**Main.cs 批量放置**：
```csharp
// _shapeRadius 從 WorldScale 衍生，不要寫死
private int _shapeRadius = WorldScale.PlayerH / 6;  // Grain=16→5, Grain=32→10
foreach (var (dx, dy, dz) in ShapeVoxels.GetOffsets(_activeShape, _shapeRadius))
{
    var p = new GridPos(placeTarget.X + dx, placeTarget.Y + dy, placeTarget.Z + dz);
    if (PlacementValidator.CanPlace(_world3d, p, mat))
        _world3d.SetTile(p.X, p.Y, p.Z, mat);
}
```

**Blender 式重塑動作**：先預留介面，標記為 `// TODO: R-6 Sculpt`，不急著實作。

---

#### R-5：碎片系統（Fragment Items + 爆炸事件分類）

**先備**：無強制依賴，但建議在 R-1~3 穩定後開始。

**設計澄清（關於 sub-tile 精度）**：
設計文件 PS2 提到「模擬粒子的演算法和模擬應力與破壞的演算法可以是分開的」。
這已在 `plan-roadmap.md` 中有架構分層：
- **Tile 層（中粒度）**：CA 物理（Powder/Liquid/Gas）——現有 GPU CA
- **次像素視覺層**：GPUParticles3D 飛散效果（V 系列）
- **應力/破壞**：不需要 sub-tile 精度，而是 **tile-group 結構完整性判斷**

因此碎片系統的正確詮釋是：

> **1 material unit（≈ 1000 tiles 體積）= 100 碎片**（整數轉換，不需 sub-tile 浮點模擬）
> 外力摧毀 1 material unit → 產生 N 個碎片掉落物（N 由破壞方式決定，如爆炸=20~80，採掘=100）

**步驟 A：`DestroyReason` enum**（`TileWorld3D.cs`）：
```csharp
public enum DestroyReason { Mining, Explosion, Slash, Crush }
event Action<GridPos, MaterialType, DestroyReason>? OnTileDestroyed;
```
破壞 API：
```csharp
public void DestroyTile(GridPos pos, DestroyReason reason = DestroyReason.Mining)
```

**步驟 B：Fragment ItemId 變體**（可腳本自動生成，見第五節）：
```
FragmentDirt, FragmentStone, FragmentSand, FragmentWood, FragmentAsh,
FragmentCoal, FragmentCopper, FragmentIron, FragmentMagicCrystal
```
在 ItemRegistry 中每個 Fragment 記錄：
```csharp
// MaxStack=99999，無 PlaceAs（不可直接放置），DisplayName="石塊碎片"
Reg(new ItemData(ItemId.FragmentStone, "石塊碎片", false, null, false, 0, 1f, 99999));
```

**步驟 C：MaterialRegistry.DefaultFragmentDrop**（在 MaterialData 新增）：
```csharp
public ItemId FragmentItem { get; init; } = ItemId.None;
```
例如 `Stone.FragmentItem = ItemId.FragmentStone`。

**步驟 D：DroppedItemManager 依 Reason 分流**：

注意：OnTileDestroyed 目前是**每個 tile 單獨觸發**，但 R-5 的碎片邏輯以 **material unit（~1000 tiles）為單位**計算掉落數。
實作時需在 Main.cs（或 SpellRunner）的爆炸邏輯層面統計「本次爆炸共摧毀幾個 tile」→ 換算成幾個 material unit → 掉落對應碎片數；**不能**每個 tile 觸發 OnTileDestroyed 時都個別掉落 20~80 碎片（否則 1000 tiles × 50 碎片 = 50,000 碎片）。

```csharp
// 爆炸處理端（Main.cs 或 SpellRunner）
// 1. 計算摧毀的總 tile 數
int tilesDestroyed = destroyedPositions.Count;
// 2. 換算為 material unit（1 unit ≈ 1000 tiles）
float units = tilesDestroyed / 1000f;
// 3. 依爆炸類型產生碎片（挖掘=全量，爆炸=20~80%損耗）
int fragments = reason == DestroyReason.Mining
    ? (int)(units * 100)              // 挖掘：100 碎片/unit（全回收）
    : (int)(units * _rng.Next(20, 81)); // 爆炸：20~80 碎片/unit（損耗）
if (fragments > 0 && fragItem != ItemId.None)
    _droppedItems.Spawn(center, new ItemStack(fragItem, fragments));
```

---

#### 名詞注解

**¹ 碎片 1% 質量門檻（= 10 tiles）**：1 個「完整材質單位」= 1 次挖/放動作的體積，邊長 ≈ PlayerH/3 ≈ 10 tiles，體積約 1000 tiles（立方體）。1% 即 **10 tiles** 的材質量，仍是整數 tile，無任何次 tile 精度需求。「門檻」意為：比 1 個碎片（10 tiles 當量）更小的量直接丟棄不記錄。

**² 次 tile 精度物理（與舊解釋的更正）**：舊注解誤用「1 tile = 1 unit」尺度，導致 1% ≈ 0.01 tile 而需要次 tile 精度。正確尺度下（1 unit ≈ 1000 tiles），1% = 10 tiles，**完全整數，無次 tile 衝突**。次 tile 物理問題只有在嘗試讓碎片以連續浮點座標飛散（如粒子效果）時才會出現，這屬於 V 系列視覺特效範疇，非 R-5 碎片物品系統的範疇。

---

### 🔴 第三層：未來方向（預留架構點，不急著做）

| 項目 | 說明 | 預留方式 |
|------|------|---------|
| 坑洞不規則像素邊緣 | 需要 V 系列 sub-tile 噪音貼圖 / GPUParticles3D 飛散 | V-1 VfxManager 預留介面 |
| 重塑動作（Blender 式） | 體素雕刻 Push/Pull，需 sub-tile 變形或 SDF 近似 | `PlacementShape.Sculpt` 預留枚舉值 |
| 100 碎片 → 1 材質 | 合成系統範疇，需合成 UI + 配方 registry | Fragment MaxStack 已設 99999 |
| 應力/崩塌模擬 | 結構完整性（懸空大區塊崩塌），可用 tile-group FloodFill 判斷 | `DestroyReason.Collapse` 預留 |

---

## 四、架構重構建議

### 4-1：`OnTileDestroyed` 簽名統一

目前三個 interface 各自定義事件：
- `TileWorld3D.cs` line 41
- `IWorldInterface.cs` line 23
- `TileWorld.cs` line 28（舊 2D 世界）
- `MockWorld.cs` line 19（測試用）

**建議**：加入 `DestroyReason` 參數。這是 Breaking Change，需同步更新所有訂閱者（`Main.cs` ×2 訂閱）。

```csharp
// before
event Action<GridPos, MaterialType>? OnTileDestroyed;
// after
event Action<GridPos, MaterialType, DestroyReason>? OnTileDestroyed;
```

如需向後相容，可用 optional overload：Main.cs 訂閱的 lambda 加第三參數。

### 4-2：`PlacementValidator` 集中放置邏輯

目前 `Main.cs` 內散落多處放置條件判斷（InBounds、Air check、冷卻）。
建議所有判斷集中至 `PlacementValidator.CanPlace(world, pos, mat, player)` 單一入口，方便未來加「不同物品不同規則」。

### 4-3：`ItemRegistry` fragment 自動生成（見 §五腳本方案）

目前 ItemRegistry 是手寫 flat table。Fragment 會有 9+ 新項目，手寫易出錯。
建議用腳本從 MaterialRegistry 資料自動派生 Fragment 條目。

---

## 五、腳本自動化方案

### 腳本 A：批量生成 Fragment ItemId + ItemData

**輸入**：讀取 `MaterialRegistry.cs` 中所有 `IsMineable = true` + `FragmentItem = ItemId.None` 的材質。

**輸出**：
1. 在 `ItemId.cs` 的 enum 末尾自動追加 `FragmentDirt, FragmentStone, ...`
2. 在 `ItemRegistry.cs` 靜態建構子末尾追加對應的 `Reg(new ItemData(...))` 行
3. 在 `MaterialRegistry.cs` 對應材質的 `MaterialData` 中填入 `FragmentItem = ItemId.FragmentXxx`

**可用 Claude Code 腳本執行**：解析 C# 原始碼（regex 或 Roslyn）→ 生成插入文字 → Edit tool 寫入。

### 腳本 B：MaxStack 批量從 99 改為 99999

**輸入**：ItemRegistry.cs 中所有方塊/礦石行的 `99)` 參數。
**輸出**：批量替換（僅工具 + 裝備保持 MaxStack=1；方塊類改 99999）。

此腳本只要 1 次 regex replace，Claude Code 的 Edit tool 即可完成，不需獨立腳本文件。

---

## 六、關於 PS2：應力與粒子算法分離（設計澄清）

設計文件 PS2：
> 「模擬粒子的演算法和模擬應力與破壞的演算法可以是分開的，還有給出建議方案，幫我確認。」

**確認**：`plan-roadmap.md` 已明確分層（引用）：

| 層級 | 算法 | 說明 |
|------|------|------|
| **Tile 物理層** | Margolus Block CA（GPU）| Powder/Liquid/Gas 移動，現已實作 |
| **次像素視覺層** | GPUParticles3D（V 系列） | 飛散碎片視覺、爆炸煙霧，純裝飾 |
| **應力/結構層** | Tile-group FloodFill | 懸空區塊崩塌判定（tile 單位，不需 sub-tile） |

三者完全獨立，互不影響。應力算法可在 `TileWorld3D.Tick()` 之後另開一個 `StructureStabilityCheck()` pass，僅在有 tile 被摧毀後觸發（非每幀）。

---

## 七、Step 10 R 系列實作順序建議

```
R-1  Raycast 面法線（TileWorld3D.cs ~15行）
  ↓
R-2  CameraController.GetCenterRay + PlayerController.MouseFaceNormal
  ↓
R-3  face-aligned 放置 + PlacementValidator（Main.cs ~20行）
  ↓
R-3+ MaxStack 99999 + UI 標籤寬度微調
  ↓
R-4  多形狀放置
       ├─ N 鍵選單 UI（_shapePanel 浮動選單，預設 Cube）
       ├─ PlacementShape.cs（enum + ShapeVoxels.GetOffsets）
       ├─ HUD 形狀指示器
       └─ [預留] TODO: R-6 FillJunction 接合補填
  ↓
[Breaking Change 確認] → R-5 DestroyReason + Fragment 系統
```

R-1 → R-3+ 為一次性 commit，無依賴衝突，約 3~4 小時工作量。
R-4 UI 方案已確認（N 鍵選單，預設 Cube），可直接實作，約 2~3 小時。
R-5 是 Breaking Change（event 簽名），需一次完整更新所有訂閱者。

---

## 八、尺度說明（設計文件 vs 現有系統）

設計文件描述「1 單位坑洞 ≈ 玩家高度的 1/3」。

**確認詮釋**（已與設計者對齊）：這是對的，**詮釋 2 正確**。

| 參數 | 值 |
|------|----|
| PlayerH | 32 tiles |
| 1 material unit 邊長 | PlayerH / 3 ≈ **10–11 tiles** |
| 立方體體積 | 10³ ≈ **1000 tiles** |
| 球體體積（radius=5） | (4/3)π(5³) ≈ **524 tiles** |

**現有系統差距**：目前放置/採掘仍是 1 tile 單位（R-3 完成前）。R-4 實作完成後，預設 radius=5 的「立方體」即對齊設計文件的「1 material unit」。

**碎片系統尺度推算**：
- 1 material unit ≈ 1000 tiles（立方體）
- 1% 碎片 = 10 tiles 當量 → 整數，無次 tile 問題
- 100 碎片（= 1000 tiles）→ 合成 1 material unit ✓
