# W-6 多 MP 框架：完整實作計畫

> 撰寫日期：2026-06-13
> 依賴：W-1（境界）已完成
> 狀態：設計已確認，等待開始實作
> 前置文件：`plan-w6-mp-multitype.md`（設計探討稿，本文件為正式實作版）

---

## 一、決策確認

> 原設計稿（`plan-w6-mp-multitype.md`）中列出的五項待確認決策，已全數根據玩家設計意圖確認如下。

| 決策 | 原選項 | 確認結果 |
|------|--------|---------|
| A. MP 綁定粒度 | A2（技能整構選） vs A3（圖騰自動對應） | **A 變體**：每個「技能因子（SpellSlot）」獨立選，非整個技能整構共用一種 |
| B. 走火入魔 | 施法時觸發 vs 獲得時觸發 | **本次暫緩**：觸發機制留待後期，預設玩家已擁有所有 MP 類型 |
| C. ManaSlot Max/Regen | 獨立 vs 共用 | **C1 獨立**：每種已解鎖的 MP 類型有獨立 Current/Max/RegenRate |
| D. 儲能瓶 | 方案 E1/E2 | **本次暫緩**：拆出獨立待辦，只確保 MP 可回復即可 |
| E. HUD 排版 | 橫列 vs 縱排 vs 分段 | **縱向排列**，1–3 條，切換技能組時立即更新 |

---

## 二、設計釐清

> 此節說明原始設計文字（`origin text setting/about w - 6 mp.txt`）中含糊或有邏輯需確認的部分。

### 2-1 「技能因子」的精確定義

「技能因子」= **SpellSlot**（技能整構中每個圖騰實例）。
一個 SpellArray 可有多個 SpellSlot，每個 SpellSlot 可獨立指定一種 ManaTypeKey（nullable — 未指定代表不消耗 MP 或免費執行）。

### 2-2 「容器效果頁面」的 MP 計算方式

ContainerEffect 在程式碼中已是 `SpellArray?`，天然擁有自己的 Slots，故：
- ContainerEffect 的每個 SpellSlot 也獨立設定 ManaTypeKey
- **雙層驗證**：
  1. **ContainerEffect 自身**：所有 Slot 的 ManaTypeKey 去重後 ≤ 3 種（存容器效果頁面時觸發）
  2. **父技能整體**：父技能的 Slot 類型 ∪ 容器效果的 Slot 類型 ≤ 3 種（存技能時觸發）

這使容器可以用與父技能不同的 MP 類型，但所有類型的總數仍受 3 種上限管制。

### 2-3 「不需要消耗 MP 的技能因子串」

SpellSlot.ManaTypeKey = null 是合法狀態，代表這串不消耗 MP。
**觸發紅光警告**的條件是：某 SpellSlot 底下存在「確定需要消耗 MP 的積木或刻印」但 ManaTypeKey 仍為 null。
> 判斷「需要消耗 MP」的積木：實作時以 SpellSlot.HasAnyMpBlocks（computed）決定，初期可先保守地假設「有任何非空 Slot 都可能要 MP」。

### 2-4 技能組 AP 上限

「每個技能組最多能使用的能力點數 = 該角色的能力點總數」這個規則是描述 AP（Ability Point），不是 MP。AP 已是 per-SpellLoadout 計算（現有邏輯不需改動）。

### 2-5 技能組超限的處理方式

當退出技能編輯器時，若發現某技能組的 MP 種類 > 3：
- 跳出**警告彈窗**，列出超限的技能組
- 問玩家：繼續調整？還是確認退出？（確認退出後，超限技能組被標記為「已停用」，在遊戲中無法切換至該組）

> **未決定項（後續設計）**：
> - 3 種 MP 上限日後是否會隨種族/特異體提升？→ `MaxManaTypes` 常數預留，不要 hardcode 3
> - 走火入魔的相容性公式、觸發時機
> - 儲能瓶的具體道具互動流程

---

## 三、系統架構

### 新增檔案

| 檔案 | 說明 |
|------|------|
| `Scripts/AbilitySystem/Data/ManaType.cs` | MP 類型資料記錄（不可變 record） |
| `Scripts/AbilitySystem/Data/ManaTypeRegistry.cs` | 18 種基礎 MP 靜態登記表，不摻業務邏輯 |
| `Scripts/AbilitySystem/Data/ManaSlot.cs` | 單一 MP 槽位（Current/Max/RegenRate） |
| `Scripts/AbilitySystem/Data/SpellGroup.cs` | 最多 5 個 SpellLoadout 的容器 + ActiveIndex |

### 修改檔案

| 檔案 | 改動摘要 |
|------|---------|
| `SpellSlot.cs` | 加 `ManaTypeKey: string?`（nullable，預設 null） |
| `SpellArray.cs` | 加 `GetUsedManaTypes()` 驗證輔助方法，MP 消耗改為 per-slot 計算 |
| `SpellCaster.cs` | 消耗邏輯依 SlotManaTypeKey 從對應 ManaSlot 扣除；null key → 不扣 MP |
| `PlayerController.cs` | SpellGroup 取代單一 SpellLoadout；加 `ActiveManaSlots: List<ManaSlot>` |
| `CharacterSaveData.cs` | 存 SpellGroup（含 5 個 SpellLoadout）、ManaSlots、ActiveGroupIndex |
| `FlowSaveSystem.cs` | 序列化/反序列化 SpellGroup + ManaSlots；舊存檔向後相容 |
| HUD（待確認節點名） | 動態 1–3 條 MP 條，縱向排列，技能組切換時立即更新 |

### 不需修改

- `TotemLibrary.cs` / `ExecutionLoop.cs` / `OpCode.cs`：積木 VM 層與 ManaType 解耦
- `AbilityPointCalculator.cs`：AP 系統不受 MP 改動影響

---

## 四、分階段實作

> 「不要寫太死」原則：各階段的程式碼結構是**設計方向**，實作時可因技術細節調整；但以下定義的**資料模型欄位與驗證邏輯**需完整落地。

---

### W-6A — 純資料層（零風險）

> 不動任何現有檔案，只新增 4 個新檔案的骨架。

**ManaType.cs**
- `record ManaType(int Id, string Key, string DisplayName, string RootGroup, bool IsComposite, int SortOrder)`
- RootGroup 可選值：`"修煉"` / `"支配"` / `"世界"`

**ManaTypeRegistry.cs**
- 靜態 `IReadOnlyDictionary<string, ManaType> All`
- 靜態查詢方法：`Get(string key)`, `GetSortedForHud()`
- 18 種基礎 MP（見附錄一），複合體系（W-13）直接追加，不改結構

**ManaSlot.cs**
- 欄位：`ManaTypeKey`, `Current`, `Max`, `RegenRate`
- `Max` / `RegenRate` 預留 setter，初期用固定預設值（Max = 100f, Regen = 1f/s）

**SpellGroup.cs**
- 欄位：`SpellLoadout?[5] Groups`（允許部分空），`int ActiveGroupIndex`
- 屬性：`SpellLoadout? ActiveLoadout => Groups[ActiveGroupIndex]`
- 常數：`const int MaxGroups = 5`（日後可擴充）

---

### W-6B — SpellSlot 加欄位 + 驗證邏輯

> 只改 SpellSlot.cs 和 SpellArray.cs，不動執行路徑。

**SpellSlot.cs**
- 新增欄位：`public string? ManaTypeKey { get; set; }`（null = 未指定）
- 新增屬性：`public bool HasAnyMpBlocks`（初期可返回保守 true，後期依積木型別細化）

**SpellArray.cs — 新增輔助方法**
```
GetUsedManaTypes(bool recursive = true)
  → 收集所有 Slot 的非 null ManaTypeKey
  → 若 recursive = true，同時收集 ContainerEffect?.GetUsedManaTypes()
  → 回傳 HashSet<string>（去重）

IsValidManaTypeCount(int limit = MaxManaTypes)
  → GetUsedManaTypes().Count <= limit

HasUnboundMpBlocks()
  → 是否存在 Slot.HasAnyMpBlocks = true && ManaTypeKey = null
  → 容器效果遞迴檢查
```

- 加常數：`public const int MaxManaTypes = 3`（日後改這裡，不要 hardcode）

---

### W-6C — SpellCaster 消耗邏輯 + PlayerController 多槽管理

> 最核心的改動，改完後需完整測試施法流程。

**PlayerController.cs**
- 移除單一 `CurrentMp` / `MaxMp`（或暫時保留為向後相容的 pass-through property）
- 新增 `public List<ManaSlot> ActiveManaSlots { get; private set; }`（最多 MaxManaSlots = 3 筆）
- 新增方法：`ManaSlot? GetSlot(string key)`, `void TickManaRegen(float delta)`
- `SpellLoadout` 欄位改為 `SpellGroup`（或加 `SpellGroup SpellGroup`）
- `ActiveLoadout` 屬性：`SpellGroup.ActiveLoadout`

**SpellCaster.cs — 消耗邏輯**
```
替代現有 player.Mp -= cost 的地方：

float totalCost = AbilityPointCalculator.CalculateMpCost(spell);
// 若 slot 有 ManaTypeKey，從對應 ManaSlot 扣；否則不扣
foreach (var slot in spell.Slots)
{
    if (slot.ManaTypeKey == null) continue;
    float slotCost = CalculateSlotMpCost(slot, spell);  // 按比例或固定？初期用均分
    var manaSlot = player.GetSlot(slot.ManaTypeKey);
    if (manaSlot == null || manaSlot.Current < slotCost) return SpellCastResult.Failed;
}
// 全部通過後才真正扣除
```

> **設計注意**：「每個 SpellSlot 分攤多少 MP cost」需決定——初期最簡：**所有有 ManaTypeKey 的 Slot 均分 BaseMpCost**；後期可改為每個 Slot 自訂 cost。

**AbilityPointCalculator.cs**
- 加方法 `CalculateSlotCostByType(SpellArray spell)` → `Dictionary<string, float>`（每種 MP 各要消耗多少）
- 供編輯器 MP 消耗列表顯示使用

---

### W-6D — SpellGroup + V 鍵切換

> UI / 輸入層改動，不影響後端計算。

**技能編輯器（AbilityEditorUI 或 ScratchCanvas）**
- 右上角加 5 個技能組點選圓點（數字 1–5 或圓點UI），點擊切換
- 切換時自動更新編輯器顯示的 SpellLoadout 內容
- 每個技能組的 SpellLoadout 獨立存在 SpellGroup 中，互不干擾
- 退出編輯器時觸發「技能組 MP 超限」檢查（見 2-5 節）

**遊戲內 V 鍵（PlayerController / InputBindings）**
- `InputBindings.cs` 加 `SpellGroupSwitch` Action（預設 V）
- 按 V → 顯示技能組切換選單（啟用滑鼠游標）
- 滾輪滑動 or 點擊選擇技能組
- 確認切換：PlayerController.ActiveGroupIndex 更新
- 切換後：HUD 的 MP 條立即切換到新技能組使用的 MP 類型

---

### W-6E — HUD MP 條

> UI 改動，在 W-6C 完成後接入。

**多條 MP 條顯示規則**
- 根據 `PlayerController.ActiveManaSlots` 動態產生 1–3 條
- 顯示順序：依 `ManaTypeRegistry.GetSortedForHud()` 返回的 SortOrder
  - 18 種基礎類型（SortOrder 1–18）→ 同源複合（SortOrder 19–...）→ 異源複合（SortOrder ...–71）
  - 同 SortOrder 段內按文件原始列舉順序（即 Id 順序）
- 每條顏色對應 RootGroup：修煉 = 藍系 / 支配 = 紫系 / 世界 = 金系
- 技能組切換後，MP 條組合立即更新（若類型改變則 bar 即時替換）

**技能編輯器 MP 消耗列表**
- 右側「MP消耗」區塊改為 ScrollContainer + 逐行顯示 `類型名稱: xx 點`
- 由 `AbilityPointCalculator.CalculateSlotCostByType()` 提供資料
- 紅光警告：SpellSlot 有 MP 積木但無 ManaTypeKey 時，該整串 Slot 在 Canvas 上閃淡紅

---

### W-6F — 存檔更新

> 最後接入存檔，確保所有新資料持久化。

**CharacterSaveData.cs**
- 新增序列化欄位：`SpellGroupDto SpellGroup`（含 5 個 SpellLoadoutDto + ActiveGroupIndex）
- 新增序列化欄位：`List<ManaSlotDto> ManaSlots`（key + current + max + regenRate）

**SpellSlot 序列化**
- `SpellSlotDto` 加 `string? ManaTypeKey`
- 舊存檔讀取時：如無 ManaTypeKey 欄位 → 反序列化為 null（不影響現有技能運作）

**向後相容邏輯（FlowSaveSystem.cs）**
- 舊存檔有 `"spellLoadout"` 而無 `"spellGroup"` → 讀入 SpellGroup.Groups[0]
- 舊存檔有 `"mp"` 而無 `"manaSlots"` → 建立單筆 ManaSlot（Key = "gui_dao", Current = saved mp value）

---

## 五、本次暫緩事項（獨立待辦）

> 以下項目不在 W-6 實作範圍內，後期再啟動。

| 項目 | 說明 |
|------|------|
| 走火入魔 / 相容性判斷 | 觸發時機（獲得 MP 時）、嚴重度分級、公式數值設計 |
| `ManaCompatibility.cs` | 相容性計算模組，依賴走火入魔設計與 W-10 種族親和 |
| 儲能瓶 | 物品系統範疇，W-6 只確保 ManaSlot.Current 可以回復（RegenRate 機制） |
| 複合體系（53 種） | W-13；W-6A 的 ManaTypeRegistry 預留接口，勿硬限 18 種 |
| MP 上限 3 種的彈性調整 | 種族/特異體質提升至 5 種；使用 MaxManaTypes 常數，不 hardcode |
| 技能因子「確定消耗 MP」的精確判斷 | `HasAnyMpBlocks` 初期保守處理，後期依 BlockType 細化 |

---

## 六、建議實作順序

```
W-6A（純資料，0 風險）→ Build 確認 0 錯誤
  ↓
W-6B（SpellSlot 加欄位 + 驗證方法）→ 編輯器側可先接 GetUsedManaTypes
  ↓
W-6C（SpellCaster + PlayerController 多槽）→ 施法 + 存讀檔測試
  ↓
W-6D（SpellGroup + V 鍵切換）→ 編輯器 + 遊戲內切換測試
  ↓
W-6E（HUD MP 條）→ 視覺確認
  ↓
W-6F（存檔更新）→ 舊存檔向後相容測試
```

---

## 七、依賴關係

```
W-6（本計畫）
  → 依賴 W-1（境界，✅ 已完成）
  → W-13（複合體系 53 種）依賴 W-6 框架完成
  → W-10（角色創建）完成後可補入種族 MP 親和係數，更新走火入魔公式
  → W-20（靈職/體質完整版）依賴 W-6
```

---

## 附錄一：18 種基礎 MP 登記表（供 ManaTypeRegistry 填入）

> SortOrder 決定 HUD 縱向排列順序，按文件原始列舉順序編排。

| Id | Key | 顯示名 | RootGroup | SortOrder |
|----|-----|--------|-----------|-----------|
| 1  | `wu_dao`     | 武道  | 修煉 | 1  |
| 2  | `xian_dao`   | 仙道  | 修煉 | 2  |
| 3  | `fa_dao`     | 法道  | 修煉 | 3  |
| 4  | `yi_dao`     | 意道  | 修煉 | 4  |
| 5  | `hun_dao`    | 魂道  | 修煉 | 5  |
| 6  | `gui_dao`    | 詭道  | 修煉 | 6  |
| 7  | `mo_fa`      | 魔法  | 支配 | 7  |
| 8  | `yao_li`     | 妖力  | 支配 | 8  |
| 9  | `ao_shu`     | 奧術  | 支配 | 9  |
| 10 | `shen_sheng` | 神聖力 | 支配 | 10 |
| 11 | `yuan_neng`  | 源能  | 支配 | 11 |
| 12 | `xing_neng`  | 星能  | 支配 | 12 |
| 13 | `ji_neng`    | 技能  | 世界 | 13 |
| 14 | `zhi_ye`     | 職業  | 世界 | 14 |
| 15 | `chao_neng`  | 超能  | 世界 | 15 |
| 16 | `shen_li`    | 神力  | 世界 | 16 |
| 17 | `gai_nian`   | 概念  | 世界 | 17 |
| 18 | `xun_neng`   | 尋能  | 世界 | 18 |

複合體系（W-13）SortOrder 從 19 起依序追加，IsComposite = true。

---

*本計畫為正式實作依據。架構方向已定，細節可在實作中調整，但不得在未更新計畫的情況下改變核心資料模型（SpellSlot.ManaTypeKey、SpellGroup 結構、驗證邏輯）。*
