# 技能編輯器 UI 重構計畫

> 建立日期：2026-06-10
> 狀態：規格確認完畢，可進入 Stage 1 實作
> 前置依賴：`實作進度.md` 的「全 Scratch 模式重構」待辦項目

---

## 一、設計概述

取代現有的「插槽面板 + 嵌入式 ScratchCanvas」混合架構，改為兩層全新介面：

```
技能創建空間（圓球列表）
    │
    ├── 點擊技能圓球
    │       └── 技能編輯頁面（Scratch Canvas 全版）
    │               │
    │               └── 雙擊容器圖騰（投射物 / 召喚物）
    │                       └── 容器效果編輯頁面（結構同技能編輯頁面）
    │                               └──（可繼續巢狀，深度待評估）
    │
    └── 點擊「+」
            └── 新增空技能
```

整體色系：高質感藍色 + 白色系。

---

## 二、畫面規格

### 2-A 技能創建空間

- **圓球列表**：橫向可滑動，每顆圓球代表一個技能
  - 主動技能排左，被動技能排右
  - 主動技能順序對應熱鍵：第 1 顆（最左）= U，第 2 顆 = I，第 3 顆 = O，第 4 顆 = P，第 5 顆 = U+I，以此類推（與 T-5 系統一致）
  - **視覺區分**：主動技能圓球邊框紅色；被動技能圓球邊框橘色（不加分隔線或標籤）
- **懸停 Tooltip**：MP 消耗、主被動 + 主動類型（即時/宣告/持續）、自動生成的效果文案
- **新增按鈕**：列表最右側「+」圓圈 ＋ 右下角浮動「+」按鈕，兩者行為相同；上限達到時「+」變灰並顯示已滿提示
- **超過上限處理**：
  - 主動上限 10，被動上限 20
  - 玩家可以先自由創建技能再決定刪哪個
  - 儲存技能後若目前數量超過上限，最右側多出來的圓球**變黑**並標注「超過上限」
  - 黑色圓球仍可點擊進入編輯、或刪除；玩家刪到上限以內後黑色消失

### 2-B 技能編輯頁面

```
┌─────────────────────────────────────────────────────────┐
│ [← 返回]  技能：[技能名稱輸入欄]              [警示跳出區] │
├──────┬──────────────────────────────────────────────────┤
│ 左側  │                                                  │
│ 面板  │             Scratch Canvas 工作區                │
│      │         （整個頁面可自由拖放積木/圖騰/刻印）         │
│      │                                                  │
├──────┤                                                  │
│      │                                                  │
│      │                                          [儲存技能]│
└──────┴──────────────────────────────────────────────────┘
```

**左側面板**：
- 母分類標籤（頂部橫排）：`圖騰` ｜ `積木` ｜ `刻印`
- 子分類標籤（母分類下方，縱向列表，可捲動）：
  - 圖騰：範圍 / 投射物 / 武技 / 變幻 / 位移 / 召喚 / 領域
  - 積木：控制流 / 邊緣觸發 / 變數 / 列表 / 向量 / 實體 / 廣播 / 統計 / 攔截 / 快照 / 其他
  - 刻印：白 / 橙 / 藍 / 紅 / 綠 / 紫 / 黃 / 元素 / 法則
- 子分類下方顯示屬於該分類的積木卡片，可拖入 Canvas

**Canvas 工作區**（Scratch 原則）：
- 積木可自由拖曳、組合、巢狀
- 容器型積木（If / RepeatN 等）自動展開包住子積木
- 積木上有輸入格可塞其他積木（參數型）
- 全部動作與 Scratch 視覺風格保持一致

**儲存驗證**（缺少時彈出警示）：
1. 技能名稱（必填）
2. 主被動定義（必選）
3. 若為主動：即時型 / 宣告型 / 持續生效型 三擇一（必選）

### 2-C 容器效果編輯頁面

- 觸發方式：在 Canvas 上**雙擊**容器型圖騰（投射物 / 召喚精靈 / 砲台 / 護衛）
- 跳出確認對話框「編輯此容器的效果？（是 / 否）」
- 確認後進入容器效果頁，結構與 2-B **完全相同**
- 左上角標題改為「容器效果編輯：[容器類型名稱]」，並顯示麵包屑路徑  
  例：`← 技能：天崩 > 投射物容器`
- 容器效果頁也可再放容器圖騰，再次雙擊可繼續深入（巢狀深度待評估，見 §四）

---

## 三、已知衝突與待解問題

### 🔴 架構衝突（實作前必須決策）

#### C-1｜SpellLoadout 無被動技能結構
- 現況：`SpellLoadout` = `SpellArray?[10]`，只有主動槽，無被動概念
- 影響：被動上限 20 個需要新增獨立容器；`SpellRunner` 需要知道哪些是被動並持續執行
- **所需修改**：
  ```csharp
  // SpellLoadout.cs 新增
  public const int MaxPassiveSlots = 20;
  private readonly SpellArray?[] _passiveSlots = new SpellArray?[MaxPassiveSlots];
  // 或改為 List<SpellArray> 動態管理
  ```
  `SpellArray` 需加 `bool IsPassive` 欄位（或改以獨立陣列區分）

#### C-2｜熱鍵系統 ✅ 已確認，無衝突
- 「第一個技能對應 U，第二個對應 I」→ 與 T-5 實作完全一致，無需修改
- 圓球列表順序直接對應 U / I / O / P / U+I / I+O / O+P / U+I+O / I+O+P / U+I+O+P

#### C-3｜容器巢狀無資料結構
- 現況：`SpellArray.Container` 只是 `ContainerType` enum，沒有容器本身的積木邏輯欄位
- 影響：容器效果頁的積木無處儲存
- **所需修改**：
  ```csharp
  // SpellArray.cs 新增
  public SpellArray? ContainerEffect { get; set; }
  ```
  `SaveSystem.cs` 需支援遞迴序列化（加深度保護防無限迴圈）；`SpellCompiler` 需支援遞迴編譯容器

---

### 🟡 設計模糊處（全部已確認）

#### C-4｜容器圖騰觸發方式 ✅ 已確認
- **雙擊**容器型圖騰觸發「編輯容器效果？」對話框，避免與 Scratch 拖曳操作衝突

#### C-5｜超過上限圓球行為 ✅ 已確認
- 玩家可以先自由創建技能，儲存後若超過上限，最右側多出的圓球**變黑**並標注「超過上限」
- 黑色圓球仍可點入編輯或刪除；玩家刪減到上限以內後黑色消失
- 發動時黑色圓球的技能**不觸發**（超過上限的熱鍵位置失效）

#### C-6｜空容器的有效性 ✅ 已確認
- 空容器效果頁（無任何積木）屬於合法技能
- 只要通過基本驗證（名稱 + 主被動 + 主動類型）即可儲存，空容器不額外報錯

#### C-7｜主被動視覺區分 ✅ 已確認
- **主動技能圓球邊框：紅色；被動技能圓球邊框：橘色**，不加分隔線或文字標籤
- 技能圓球排列順序由建立先後決定；不支援拖曳重排（熱鍵順序固定）

---

### 🟢 新子系統需求（已確認納入，Stage 5 實作）

#### C-8｜效果文案自動生成 ✅ 已確認
- 新建 `SpellDescriptionGenerator.cs`，讀取 `SpellArray.Slots`、`GlobalEngravings`、`ActivationType`、`Container` 組合成可讀文案
- 模板範例：「[容器] 發射，命中後造成 [傷害刻印]，附加 [控制刻印]」
- Stage 5 最後實作，Tooltip 先顯示靜態佔位文字

#### C-9｜積木子分類捲動 ✅ 已確認
- 子分類標籤列表加垂直捲動，不使用折疊分組（保持一目了然）

---

## 四、容器巢狀深度評估

### 技術可行性
理論上可行（`SpellArray.ContainerEffect` 遞迴），但需要：
- 編譯期深度計數（`SpellCompiler` 加 `containerDepth` 參數）
- 執行期深度上限（類似 `SafetyGuard.MaxComboDepth`）
- 儲存期循環防護（序列化時偵測 A → B → A 的引用迴圈）

### 建議深度上限
| 深度 | 描述 | 建議 |
|------|------|------|
| 0    | 技能主體 | 永遠允許 |
| 1    | 投射物 / 召喚物的效果 | 允許 |
| 2    | 容器內的容器 | 允許，但 UI 加提示「二層容器」 |
| 3+   | 深度三層以上 | 建議禁止；設計複雜度急遽上升，效能風險高 |

**建議先以深度上限 2 實作**，之後視設計需求再解鎖。

---

## 五、需修改的主要檔案

| 檔案 | 修改幅度 | 說明 |
|------|---------|------|
| `Scripts/UI/AbilityEditorUI.cs` | 完全重寫 | 換掉現有混合架構，實作新三頁導覽 |
| `Scripts/UI/ScratchCanvas.cs` | 中度調整 | 保留核心積木邏輯，整合進新版面 |
| `Scripts/AbilitySystem/Data/SpellLoadout.cs` | 中度修改 | 加入被動技能槽 |
| `Scripts/AbilitySystem/Data/SpellArray.cs` | 輕度修改 | 加入 `ContainerEffect`、`IsPassive` 欄位 |
| `Scripts/AbilitySystem/SaveSystem.cs` | 中度修改 | 支援遞迴容器序列化 |
| `Scripts/AbilitySystem/VM/SpellCompiler.cs` | 中度修改 | 支援容器遞迴編譯 |
| `Scripts/AbilitySystem/SpellRunner.cs` | 輕度修改 | 支援被動技能持續執行 |
| `Scripts/Main.cs` | 中度修改 | 技能選擇 UI 換成圓球列表 |
| `Scripts/AbilitySystem/SpellDescriptionGenerator.cs` | 全新建立 | 效果文案自動生成 |

---

## 六、實作分段建議

> 各段之間互不阻塞，可視優先順序調整順序。

### Stage 1：資料層 ✅ 完成（2026-06-10）
- [x] `SpellArray` 加 `IsPassive` + `ContainerEffect`
- [x] `SpellLoadout` 加被動槽（`_passiveSpells: List<SpellArray>`，MaxPassiveSlots=20）
- [x] `SaveSystem` 遞迴序列化支援（`DtoSpell.ContainerEffect`，深度保護 `SafetyGuard.MaxContainerDepth=2`）
- [x] C-2 熱鍵確認無衝突，無需修改

### Stage 2：技能創建空間（圓球列表）✅ 完成（2026-06-10）
- [x] 圓球列表 UI（主被動分隔、橫向滑動）— `SpellListUI.cs`（新建）
- [x] 「+」按鈕邏輯（上限檢查、新增空技能）— 列表尾 + 浮動右下角雙按鈕
- [x] 懸停 Tooltip（靜態文字）— 顯示名稱、主被動、MP、Stage 5 佔位
- [x] `AbilityEditorUI` 加 `BackPressed` 信號 + `← 返回` 按鈕 + `OpenSlot(i)` + `_headerSlotBtns[]`
- [x] `Main.cs` 導覽邏輯：E → SpellListUI → 點圓球 → AbilityEditorUI → 返回 → SpellListUI

### Stage 3：技能編輯頁面（主要 Canvas 頁）

#### Stage 3-A：自由畫布架構骨架 ✅（2026-06-10）
- [x] `BlockScript.cs`（新）：一條可拖曳積木鏈，Header 整條移動、色條斷開、C-shape 分支容器、`SplitAt`/`AppendBlocks` API
- [x] `ScriptCanvas.cs`（新）：自由畫布，多腳本絕對定位，`_Input` 拖曳，40px 磁吸自動連結，`SyncFrom`/`Changed` 介面與舊版相容
- [x] `ScratchCanvas.cs` 重構：`BuildUI` delegate 改傳 `Func<opts>?`；`SlotPicker` 與全輔助方法改為 `internal static`；加 `TryGetDescriptor`
- [x] `AbilityEditorUI.cs`：`_canvas` 型別由 `ScratchCanvas` 改為 `ScriptCanvas`

#### Stage 3-B：完整 Scratch 互動 ✅（2026-06-10）
- [x] 調色盤拖入畫布（AbilityEditorUI._Input 4px 門檻啟動 BlockDrag.BeginNew，ScriptCanvas drop 生成新腳本）
- [x] C-shape 容器視覺（body Panel 左邊框 + closeRow armEnd+shelf，不用 _Draw()）
- [x] Reporter 積木槽（RepeatN/Wait/Sleep/SetEntityProp 改 SmallEdit，ExecutionLoop 改呼叫 ResolveNum）
- [x] SnapPoint 視覺高亮（_snapHL 綠色橫條，60px 範圍內顯示在目標底端；_palPreview 游標預覽 Label）

#### Stage 3-C：其他 ✅（2026-06-10）
- [x] 新版左側面板（圖騰/積木/刻印 三 Tab，切換重建可捲動子內容；積木按鈕保留拖放 + GuiInput）
- [x] Header 主被動 toggle（`[主動][被動]`，RefreshAll 同步）
- [x] 儲存驗證 AcceptDialog（收集名稱空白 / AP 超限錯誤後一次彈出警示）

### Stage 4：容器效果編輯頁面
- [ ] 雙擊容器圖騰觸發確認對話框
- [ ] 容器效果頁（與技能編輯頁共用同一組件，但換標題 + 麵包屑）
- [ ] 深度限制（建議上限 2，`SafetyGuard` 加新常數）
- [ ] `SpellCompiler` 遞迴容器編譯

### Stage 5：效果文案生成（可最後實作）
- [ ] `SpellDescriptionGenerator.cs` 模板引擎
- [ ] 接入圓球列表 Tooltip

---

*此文件為計畫草案，C-1 / C-2 / C-6 / C-7 的決策確認後，Stage 1 才可開始實作。*
