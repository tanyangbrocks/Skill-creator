# 放置物件系統（PlacedObjectRegistry）實作計畫

> 對應實作進度待辦：R-6a ~ R-6e

---

## 設計動機

世界中的材質分兩類，從根本上應當區分：

| 類型 | 說明 | 範例 |
|------|------|------|
| **世界原生材質** | 程序生成、不具備放置身份 | 地形的泥土、石頭、洞穴 |
| **玩家放置物件** | 由玩家主動放置、具有 PlacedUnit 身份 | 玩家建造的牆壁、地板、結構 |

有了這個區分：
- 採掘可以依物件身份做「完美移除」（整組取回），而非逐格破壞
- 未來的拉伸系統、鏡像、複製都以 PlacedUnit 為操作目標
- 爆炸等外力破壞後，殘餘 tiles 仍保有身份，可繼續被操作

---

## 核心資料結構（R-6a）

### `PlacedUnit`

```csharp
public class PlacedUnit
{
    public int              Id       { get; }
    public MaterialType     Mat      { get; }
    public HashSet<GridPos> Tiles    { get; }   // 現存 tiles（隨破壞縮減）
    public HashSet<GridPos> Original { get; }   // 放置當下的完整 tiles（唯讀快照）
}
```

- `Tiles`：動態，tile 被破壞後從此集合移除
- `Original`：靜態快照，用於計算損壞度與返還比例
- Unit 的 `Tiles` 歸零時，Unit 自動從 Registry 刪除

### `PlacedObjectRegistry`

```csharp
public class PlacedObjectRegistry
{
    Dictionary<GridPos, int>    _tileToUnit;   // 格 → UnitId
    Dictionary<int, PlacedUnit> _units;        // UnitId → Unit
}
```

**主要方法：**
- `Register(IEnumerable<GridPos> tiles, MaterialType mat) → PlacedUnit`
- `TryGetUnit(GridPos pos, out PlacedUnit unit) → bool`
- `NotifyDestroyed(GridPos pos)`：從 `_tileToUnit` 移除，Unit.Tiles 縮減，Unit 空則清
- `Save(string worldDir)`、`Load(string worldDir)`

---

## 放置流程接入（R-6b 前置）

Main.cs 放置邏輯目前：
```
SetTile × N → 消耗 1 物品
```

修改後：
```
SetTile × N → PlacedObjectRegistry.Register(tiles, mat) → 消耗 1 物品
```

---

## 採掘分流（R-6b）

挖中的 tile 若在 Registry 中，分流：

```
TickMining 完成
    ├─ TryGetUnit(target) = true
    │   ├─ 設定「完美移除」= ON  → 整組移除 Unit.Tiles，返還 1 物品（依損壞度比例）
    │   └─ 設定「完美移除」= OFF → 走現有形狀採掘路線（無視 Registry）
    └─ TryGetUnit(target) = false → 世界原生採掘（現有路線）
```

設定面板新增 toggle「完美移除放置物件」（預設 ON）。

---

## 殘餘格處理（R-6c）⚠️ 遊戲核心機制

當 PlacedUnit 被部分破壞（爆炸、敵人攻擊等）：

- **Unit 不消滅**：只要 `Tiles.Count > 0`，Unit 繼續存在
- **損壞度**：`Damage = 1 - (Tiles.Count / Original.Count)`
- **完美移除返還**：`返還數量 = ceil(1 × (1 - Damage))`（最少 0，最多 1）
  - 例：Unit 原 1000 tiles，被炸掉 400，剩 600 → 返還 ceil(0.6) = 1 個物品
  - 例：Unit 原 1000 tiles，被炸掉 800，剩 200 → 返還 ceil(0.2) = 1 個物品（最低 1？待設計）
- **殘餘格仍可操作**：完美移除、未來拉伸系統的操作目標都是剩餘的 `Tiles`

> ⚠️ **待設計**：損壞超過閾值（例如 > 80%）是否讓 Unit 自動「解體」成普通 tiles？或永遠保有身份？這影響拉伸系統的語義，需要在 R-6e 確定前決定。

---

## 跨 Session 持久化（R-6d）

**與 R-6a~c 同批實作，不可延後。**

若只做 R-6a~c 不做 R-6d：重開遊戲後 Registry 清空，所有放置物件變回「世界原生」，殘餘格機制失效。

### 儲存格式

世界資料夾新增 `placed-registry.json`：

```json
[
  {
    "id": 1,
    "mat": "Stone",
    "tiles":    [{"x":100,"y":50,"z":100}, ...],
    "original": [{"x":100,"y":50,"z":100}, ...]
  },
  ...
]
```

### 存取時機

- **Save**：`Main.cs` 存檔時（目前每 300 幀 EvictFarChunks，一起觸發）
- **Load**：`StartGameplay` 載入世界後，`TileWorld3D` 初始化完成後讀入

### 資料量估算

1 個 Cube(r=5) Unit = 1331 tiles = 每個 `{x,y,z}` 約 30 bytes → 1 Unit ≈ 40KB。  
100 個 Unit ≈ 4MB，合理範圍。大量建造時可考慮二進位格式（Phase 2 優化）。

---

## 拉伸系統（R-6e）未來

> 以 PlacedUnit 為操作目標，執行類 Blender 操作。  
> 細節待 R-6a~d 完成後另開計畫文件。

初步設想：
- 選取 PlacedUnit（點擊已放置 tile 進入選取模式）
- 顯示包圍盒（AABB）與操作控制點
- 拉伸：沿軸延伸 Unit 的 tiles，新增 tiles 也登記進同一 Unit
- 縮減：沿軸裁切，被裁切的 tiles 從 Unit 移除（不返還物品）

---

## 實作順序

```
R-6a  PlacedUnit + PlacedObjectRegistry 核心（含 Save/Load）
R-6b  放置接入 + 採掘分流 + 設定面板 toggle
R-6c  殘餘格損壞度計算 + 返還比例（需先確認設計決策）
R-6d  ← 與 R-6a 同步完成，不單獨排
R-6e  拉伸系統（另開文件）
```
