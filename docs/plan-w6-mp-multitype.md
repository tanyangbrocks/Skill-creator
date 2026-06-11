# W-6 MP 多類型框架 實作計畫

> 撰寫日期：2026-06-11
> 依賴：W-1（境界）已完成；W-CC3（種族 MP 親和）後期補入
> 狀態：設計階段，尚未開始實作

---

## 一、核心矛盾：蒼究 vs game-setting 的差異

W-6 採用**蒼究版本**（相容性判斷，非硬上限）：

| 面向 | game-setting（技能.md） | 蒼究世界觀（本計畫） |
|------|------------------------|----------------------|
| 硬上限 | 3 種（特殊 5 種） | 無固定數字，以**相容性判斷**取代 |
| 超過後果 | 直接自爆 | 走火入魔，嚴重度分級，不一定死 |
| MP 種類數 | 12 種符文 | 18 基礎 + 53 複合 = 71 種 |

遊戲初期：玩家預設 3 槽上限作為起點，走火入魔機率是真正的彈性限制。

---

## 二、系統邊界

### 新增檔案

| 檔案 | 內容 |
|------|------|
| `ManaType.cs` | 能量類型資料模型 |
| `ManaTypeRegistry.cs` | 18 基礎 + 53 複合的靜態登記表 |
| `ManaSlot.cs` | 玩家的單一能量槽位 |
| `ManaCompatibility.cs` | 相容性判斷 + 走火入魔邏輯 |

### 修改檔案

| 檔案 | 改動 |
|------|------|
| `PlayerController.cs` | 從單一 `CurrentMp` 改為多槽位 |
| `CharacterStats.cs` | `MaxMp` 改為 per-slot，加 `ManaAffinity` |
| `SpellArray.cs` | 加 `RequiredManaTypeId`（nullable，舊存檔相容） |
| `SpellCaster.cs` | 消耗指定槽位，fallback 到第 0 槽 |
| `SaveSystem.cs` | 存讀 ManaSlot 狀態 |
| HUD（ScriptCanvas / Main）| 顯示多條 MP 條 |

### 不需修改

- `TotemLibrary.cs` / `ScratchCanvas.cs` / `ExecutionLoop.cs`
  積木 VM 層不感知 ManaType，技能效果與能量類型解耦

---

## 三、設計決策

> ⚠️ 動程式碼前必須先確認。以下標注「待確認」的需要設計決策。

### 決策 A：法陣如何選擇 ManaType？【待確認】

| 選項 | 說明 | 代價 |
|------|------|------|
| A1. 自動用第 0 槽 | 最簡單，無選擇 | 多槽意義小 |
| **A2. 法陣設定中選 ManaType（下拉）** | 玩家可指定每個法陣用哪種 MP | 要改 AbilityEditorUI |
| A3. 圖騰決定 ManaType | 詭道圖騰 → 詭道 MP，天然對應 | TotemLibrary 加欄位 |

**推薦 A2**：法陣層選擇，詭道作預設值，玩家可覆蓋。

---

### 決策 B：走火入魔三個嚴重度？【待確認】

走火入魔在裝備跨根源第 N 個槽時，或施法時以機率觸發：

| 級別 | 觸發條件 | 效果（草案） |
|------|---------|------------|
| 輕微 | 衝突機率觸發 | 精力/體力大量消耗，MP regen ↓50%，持續 10s |
| 嚴重 | 連續 3 次輕微 | HP drain 5%/s，移速 ↓30%，持續 30s |
| 致命 | HP < 20% 且發生嚴重 | 進入「氣脈逆亂」，HP 快速流失，需道具解除 |

衝突機率公式（初始簡化版）：

```
衝突率 = 跨根源槽位對數 × 15% − 種族親和修正 − 特異體質修正
```

具體係數待平衡設計確認後填入。

---

### 決策 C：ManaSlot 的 Max/Regen 是否獨立？【待確認】

| 選項 | 說明 |
|------|------|
| **C1. 獨立**（每槽自己的 Max + Regen）| 允許「詭道 MP 多、仙道 MP 少」，世界觀感強 |
| C2. 共用 Max，只分槽追蹤 Current | 簡單，但失去差異化 |

**推薦 C1 獨立**。

---

### 決策 D：儲能瓶的操作流程？【待確認】

| 方案 | 說明 |
|------|------|
| E1. 簡單 | 使用瓶 → 選槽位 → 把該槽 MP 類型裝入瓶 |
| **E2. 完整** | 瓶有固定 MP 類型，使用後切換槽位並保留兩者 Current 值 |

**推薦 E2**：符合世界觀「隨時切換使用的能量儲存道具」。

---

### 決策 E：HUD 多條 MP 條的排版？【待確認】

選項：橫向疊放（疊在現有 MP 條下方） / 側邊欄獨立區塊 / 簡化為單條帶分段標記。

---

## 四、分階段實作

### W-6A — 純資料層（零風險）

> **不動任何現有檔案**，只新增兩個資料定義檔。

```csharp
// ManaType.cs
public sealed record ManaType(
    int    Id,
    string Key,         // 識別用，如 "gui_dao"
    string DisplayName, // 顯示名，如 "詭道"
    string RootGroup,   // "修煉" / "支配" / "世界"
    bool   IsComposite  // true = 複合體系（W-13 才開放）
);

// ManaTypeRegistry.cs — 靜態登記
public static class ManaTypeRegistry
{
    public static readonly IReadOnlyDictionary<string, ManaType> All;
    public static ManaType? Get(string key);
    public static bool AreSameRoot(string keyA, string keyB);
}
```

18 種基礎 MP（三大根源各 6）：

| Id | Key | 名稱 | RootGroup |
|----|-----|------|-----------|
| 1 | `wu_dao`    | 武道 | 修煉 |
| 2 | `xian_dao`  | 仙道 | 修煉 |
| 3 | `fa_dao`    | 法道 | 修煉 |
| 4 | `yi_dao`    | 意道 | 修煉 |
| 5 | `hun_dao`   | 魂道 | 修煉 |
| 6 | `gui_dao`   | 詭道 | 修煉 |
| 7 | `mo_fa`     | 魔法 | 支配 |
| 8 | `yao_li`    | 妖力 | 支配 |
| 9 | `ao_shu`    | 奧術 | 支配 |
| 10 | `shen_sheng` | 神聖力 | 支配 |
| 11 | `yuan_neng` | 源能  | 支配 |
| 12 | `xing_neng` | 星能  | 支配 |
| 13 | `ji_neng`   | 技能  | 世界 |
| 14 | `zhi_ye`    | 職業  | 世界 |
| 15 | `chao_neng` | 超能  | 世界 |
| 16 | `shen_li`   | 神力  | 世界 |
| 17 | `gai_nian`  | 概念  | 世界 |
| 18 | `xun_neng`  | 尋能  | 世界 |

53 種複合體系（W-13 再登記，W-6A 先留 placeholder）。

---

### W-6B — 玩家能量槽

修改：`PlayerController.cs`、`CharacterStats.cs`

```csharp
// ManaSlot.cs
public sealed class ManaSlot
{
    public string    ManaTypeKey { get; }
    public float     Current     { get; set; }
    public float     Max         { get; set; }
    public float     RegenRate   { get; set; }
    public ManaSlot(string key, float max, float regenRate);
}

// PlayerController 新增：
public List<ManaSlot> ActiveManaSlots { get; }   // 最多 3 個
public const int MaxManaSlots = 3;
public bool  EquipManaType(string key);          // 失敗 = 不相容且走火
public void  UnequipManaType(string key);
```

**向後相容**：現有 `CurrentMp` / `MaxMp` 屬性保留，轉發到 `ActiveManaSlots[0]`。存檔格式中原本的 `"mp"` 欄位在讀檔時自動對應到 slot[0]（詭道）。

---

### W-6C — SpellArray 綁定 + 消耗邏輯

修改：`SpellArray.cs`、`SpellCaster.cs`

```csharp
// SpellArray.cs 新增：
public string? RequiredManaTypeKey { get; set; }  // null = 舊存檔，fallback slot[0]

// SpellCaster 新的消耗邏輯：
private static bool TryConsumeMp(PlayerController player, SpellArray spell, float cost)
{
    var slot = spell.RequiredManaTypeKey != null
        ? player.ActiveManaSlots.FirstOrDefault(s => s.ManaTypeKey == spell.RequiredManaTypeKey)
        : player.ActiveManaSlots.FirstOrDefault();     // fallback 第 0 槽
    if (slot == null || slot.Current < cost) return false;
    slot.Current -= cost;
    return true;
}
```

---

### W-6D — 相容性判斷 + 走火入魔

> 需要決策 B 確認後才能實作數值部分。

新增：`ManaCompatibility.cs`

```csharp
public enum ConflictSeverity { None, Mild, Severe, Fatal }

public static class ManaCompatibility
{
    // 計算裝備新 ManaType 時的衝突機率（0–1）
    public static float CalcConflictChance(
        IEnumerable<ManaSlot> existing, string newKey,
        float raceBonus = 0f, float constitutionBonus = 0f);

    // 施法時觸發走火入魔判定
    public static ConflictSeverity RollConflict(float conflictChance, int consecMildCount);

    // 走火入魔效果套用（接入 CharacterState）
    public static void ApplyDeviation(PlayerController player, ConflictSeverity severity);
}
```

走火入魔接入 `CharacterState.cs`（類似已有的體溫/體力系統），新增狀態欄位：

```csharp
// CharacterState.cs 新增：
public ConflictSeverity ManaDeviationLevel { get; set; }
public float            ManaDeviationTimer  { get; set; }  // 倒數秒數
public int              ConsecMildCount     { get; set; }  // 連續輕微次數
```

---

### W-6E — HUD 多條 MP 條

修改：HUD 相關（`ScriptCanvas.cs` 或 Main 的 HUD 節點）

- 根據 `ActiveManaSlots.Count` 動態顯示 1–3 條 MP 條
- 各條顏色對應 `RootGroup`（修煉=藍系 / 支配=紫系 / 世界=金系）
- 走火入魔時對應槽位的 MP 條閃紅色

---

### W-6F — 儲能瓶道具

修改：`ItemId.cs`、`ItemRegistry.cs`、`PlayerController.cs`

```csharp
// ItemId.cs 新增（示意，實際依 E2 方案）：
EnergyBottle_GuiDao,
EnergyBottle_XianDao,
// ... 其餘 16 種基礎體系 ...

// PlayerController.cs 新增：
public bool UseEnergyBottle(ItemId bottleId, int slotIndex);
// 效果：把 slotIndex 的 Current 存入瓶，把瓶的 ManaType 裝回該槽位
```

---

## 五、真正困難的部分

1. **相容性公式的數值平衡** — 衝突率係數、走火入魔嚴重度進階條件、種族/體質修正係數，都需要在決策 B 後設計。

2. **HUD 多條 MP 排版** — 3 條並排還是堆疊，需要 UI 決策（決策 E）。

3. **SpellArray + AbilityEditorUI 的 ManaType 選擇器** — 在法陣設定中加選擇 MP 類型的下拉，需改 UI（取決於決策 A）。

4. **18 種基礎 MP 的逐一定義** — 純內容工作，W-6A 只需要名稱 + 根源分組，詳細 gameplay 效果（仙道 MP 讓飛行更快等）留到各自的修煉系統實作時補。

5. **向後相容存檔** — 舊存檔沒有 `manaSlots` 欄位，讀檔時需要正確 fallback 到詭道 slot[0]。

---

## 六、建議實作順序

```
W-6A（純資料，0 風險）
  ↓ 確認決策 A、C 後
W-6B（多槽位，向後相容）
  ↓
W-6C（消耗邏輯接入）
  ↓ 測試：施法、存讀檔
  ↓ 確認決策 B 後
W-6D（走火入魔）
  ↓ 確認決策 E 後
W-6E（HUD 多條 MP 條）
  ↓
W-6F（儲能瓶）
```

---

## 七、W-6 與其他 W-N 的依賴關係

```
W-6A/B/C（框架）
  → 需要 W-1（境界，已完成）
  → W-13（複合體系解鎖）依賴 W-6 第二階段
  → W-10（角色創建）實作後才有種族 MP 親和係數，可更新 W-6D 公式
  → W-20（靈職/體質）完整版依賴 W-6
```

---

*此計畫為設計草稿。決策 A–E 確認後方可開始 W-6B 之後的實作。*
