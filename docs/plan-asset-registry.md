# 素材包系統（Asset Pack Registry）實作計畫

> 撰寫日期：2026-06-14
> 狀態：設計已確認，等待實作
> 目標：素材路徑不寫死，支援預設包 + 外部素材包（類 Minecraft 材質包機制）

---

## 一、設計目標

1. **路徑不寫死** — 程式碼只使用邏輯鍵（Key），不直接寫 `res://` 或 `user://` 路徑
2. **可替換** — 只要換一份 JSON 對照表，所有素材同步切換
3. **層疊覆蓋** — 用戶素材包可只覆蓋部分鍵，其餘自動 fallback 到預設包
4. **易維護** — 新增素材只改 JSON，不動 C# 程式碼
5. **Godot 友善** — 正確處理 `res://`（已 import）與 `user://`（runtime 讀取）兩種路徑的差異

---

## 二、Key 命名規格

格式：`{分類}.{id}.{變體}`，全小寫，`.` 分隔。

| 分類前綴 | 對應素材類型 | 範例 Key |
|----------|------------|---------|
| `mob` | 生物模型 / 材質 | `mob.melee.model` / `mob.melee.texture` |
| `tile` | 世界磁磚外觀 | `tile.stone.albedo` / `tile.water.normal` |
| `material` | 材質球（`.tres`） | `material.stone.surface` |
| `placement` | 放置物模型 | `placement.chest.model` |
| `effect.ca` | CA 細胞自動機特效 | `effect.ca.fire.rule` |
| `effect.skill` | 技能特效場景 | `effect.skill.fireball.scene` |
| `effect.particle` | 粒子效果 | `effect.particle.hit.tres` |
| `ui` | UI 圖片 / 圖示 | `ui.icon.spell_slot` |

> `id` 和 `variant` 可以多段：`mob.heavy_armored.front.texture` 合法。  
> 對照計畫文件中「預設路徑」時，Key 就是那個路徑的邏輯名稱。

---

## 三、素材包格式（Pack）

每個素材包是一個資料夾，內含 `pack.json`：

```
res://assets/packs/default/      ← 專案內預設包
  pack.json
  mobs/
    melee.glb
  tiles/
    stone.png
    water.png
  ...

user://packs/my_pack/            ← 用戶外部包（不經 Godot importer）
  pack.json
  tiles/
    stone.png                    ← 覆蓋預設的石頭材質
```

**pack.json 結構：**

```json
{
  "id": "default",
  "name": "預設素材包",
  "version": "1.0.0",
  "priority": 0,
  "base_dir": "res://assets/packs/default/",
  "mappings": {
    "mob.melee.model":   "mobs/melee.glb",
    "tile.stone.albedo": "tiles/stone.png",
    "tile.water.albedo": "tiles/water.png",
    "material.stone.surface": "materials/stone.tres"
  }
}
```

- `base_dir` + mapping 值 = 完整路徑
- mapping 值也可以是完整的絕對路徑（`res://` 或 `user://` 開頭）
- `priority` 越大越優先；預設包 priority = 0，用戶包建議 10+

---

## 四、系統架構

### 新增檔案

| 檔案 | 說明 |
|------|------|
| `Scripts/Assets/AssetPack.cs` | 單一素材包的資料記錄（id / name / priority / mappings） |
| `Scripts/Assets/AssetRegistry.cs` | 靜態登記表，管理所有已載入的 Pack，提供 Resolve / Load |
| `Scripts/Assets/AssetLoader.cs` | 依路徑類型選擇正確讀法（`GD.Load` vs runtime `Image.LoadFromFile`） |
| `res://assets/packs/default/pack.json` | 預設包 JSON（素材路徑待後續填入） |

### 不新增

- 不動任何現有遊戲邏輯檔案（這一階段只建底層）
- 不建 UI（Pack 切換 UI 是後期功能）

---

## 五、AssetRegistry API

```csharp
public static class AssetRegistry
{
    // 啟動時呼叫；自動讀 res://assets/packs/default/pack.json
    public static void Initialize() { ... }

    // 手動載入額外素材包（外部 JSON 路徑）
    public static void LoadPack(string jsonPath) { ... }

    // 依 Key 解析出完整路徑；找不到回傳 null
    public static string? Resolve(string key) { ... }

    // 載入 Godot Resource（res:// 用 GD.Load，user:// 用 AssetLoader）
    public static T? Load<T>(string key) where T : GodotObject { ... }

    // 載入貼圖（統一介面，處理 res:// / user:// 差異）
    public static Texture2D? LoadTexture(string key) { ... }

    // 查詢 Key 是否有對應（供 Debug / Preflight 使用）
    public static bool Has(string key) { ... }
}
```

### Resolve 優先序
1. 已載入的 Pack 中，`priority` 最高的那個有對應此 Key → 使用它
2. 沒有任何 Pack 有此 Key → 回傳 `null`，由呼叫方決定用 fallback 素材或靜默忽略

---

## 六、AssetLoader — res:// vs user:// 差異處理

Godot 4 的限制：
- `res://` 資源必須透過 Godot importer 處理，用 `GD.Load<T>()` 讀取
- `user://` 外部檔案沒有 import 記錄，必須用 runtime 讀法：
  - 圖片 → `Image.LoadFromFile(absPath)` → `ImageTexture.CreateFromImage(img)`
  - GLB 模型 → `GltfDocument + GltfState`（Godot 4 runtime 支援）
  - `.tres` 文字資源 → `ResourceLoader.Load()`（`user://` 支援）

```csharp
// AssetLoader 內部邏輯（簡化）
public static Texture2D? LoadTextureFromPath(string fullPath)
{
    if (fullPath.StartsWith("res://"))
        return GD.Load<Texture2D>(fullPath);

    // user:// → 轉換為系統絕對路徑再用 Image 讀
    string absPath = ProjectSettings.GlobalizePath(fullPath);
    var img = new Image();
    if (img.Load(absPath) != Error.Ok) return null;
    return ImageTexture.CreateFromImage(img);
}
```

---

## 七、分階段實作

### AR-A — 純資料層（0 風險）

> 不動任何現有程式，只建骨架。

- 建立 `Scripts/Assets/` 資料夾
- `AssetPack.cs`：record，只存 id / name / priority / baseDir / mappings
- `AssetRegistry.cs`：靜態字典，`Initialize()` 讀 default pack.json，`Resolve()` 回傳路徑
- `AssetLoader.cs`：只實作 `LoadTextureFromPath()`（處理 res:// / user:// 分支）
- 建立 `res://assets/packs/default/pack.json`（先空 mappings，等後續填入）

Build 確認 0 錯誤後進入 AR-B。

---

### AR-B — 接入第一個實際使用點（Tile 外觀）

> 選 Tile 外觀作第一個接入點，因為它目前最可能先換素材。

- 在 `TileWorld3D` 或 `TileWorldRenderer3D` 中，找到目前 hardcode 材質的位置
- 改呼叫 `AssetRegistry.LoadTexture("tile.{materialKey}.albedo")`
- 在 default/pack.json 補入對應 mapping（指向現有路徑）
- 行為不應改變，只是把路徑移出程式碼

---

### AR-C — user:// 外部包支援

> 允許放在 user:// 的素材包覆蓋預設包。

- `Initialize()` 額外掃描 `user://packs/` 下所有資料夾，找到 `pack.json` 就載入
- 完成 `AssetLoader` 對 user:// 圖片的 runtime 讀取
- 完成 `AssetLoader` 對 user:// GLB 的 runtime 讀取（`GltfDocument`）
- 建立一份測試用外部包，手動驗證覆蓋行為

---

### AR-D — Pack 切換 UI（後期）

> 提供遊戲設定介面讓玩家選擇素材包。

- 設定頁面加「素材包」Tab，列出已偵測的 user:// packs
- 勾選/取消 → 更新 priority 排序 → 重新呼叫 `Initialize()`（或 `ReloadAll()`）
- 設定持久化至 `user://settings.json`

---

## 八、Preflight 整合（可選）

可在 `preflight-check-v2.ps1` 加一道掃描：

- 遍歷 default/pack.json 所有 mapping
- 檢查每個路徑對應的檔案是否存在
- 不存在 → WARN，以便早期發現路徑錯誤

---

## 九、本次暫緩

| 項目 | 說明 |
|------|------|
| CA 特效 / 技能特效接入 | 視覺系統尚未穩定，AR-A/B 完成後再規劃 |
| 生物模型接入 | 生物目前是 Mesh 程式生成，等外部模型路線確定後接入 |
| Pack 切換 UI | AR-D，優先度低，後期功能 |
| 動畫資源 | 等外部模型確定格式（glb 含骨骼動畫）後一起處理 |
| 素材包驗證器 / 模式 | 後期工具，不影響核心機制 |

---

## 十、實作順序

```
AR-A（純資料層，0 風險）→ Build 確認
  ↓
AR-B（接入 Tile 外觀，最小改動驗證機制有效）
  ↓
AR-C（user:// 外部包，runtime 讀法）
  ↓
AR-D（Pack 切換 UI，後期）
```

---

## 附錄：Key 命名速查

> 後續「預設路徑」只需說明 Key 名稱與對應的檔案即可，不需要在程式碼中改路徑。

```
mob.{id}.model          生物 3D 模型（.glb）
mob.{id}.texture        生物 albedo 貼圖
tile.{materialKey}.albedo   磁磚 albedo 貼圖
tile.{materialKey}.normal   磁磚法線貼圖
tile.{materialKey}.emissive 磁磚自發光貼圖
material.{id}.surface   Godot 材質球（.tres）
placement.{id}.model    放置物 3D 模型
effect.ca.{id}.scene    CA 特效場景
effect.skill.{id}.scene 技能特效場景
effect.particle.{id}    粒子資源（.tres）
ui.icon.{id}            UI 圖示
```
