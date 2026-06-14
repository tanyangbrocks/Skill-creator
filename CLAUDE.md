# SkillCreator — Claude Code 工作規則

---

> ⚠️ **此 Godot 專案已於 2026-06-14 封存（archived）。**
> 主線開發已移至 UE5：
> - 本地路徑：`C:\Users\譚揚勳\SkillCreatorUE5\`
> - GitHub：https://github.com/tanyangbrocks/Skill-creator-UE5.git
> - 遷移計畫：`docs/plan-ue5-migration.md`（兩個 repo 都有副本）
>
> 此 repo 僅供邏輯對照，不再做任何新功能。

---

## 專案概要（Godot 封存版）
Godot 4.6.3 mono（.NET 8、C#）技能設計系統。玩家用類 Scratch 積木設計能力，效果與 Noita 式細胞自動機世界物理互動。
世界觀以**蒼究（`Import word setting/`）**為主軸，導入計畫獨立於主線之外。

## 必讀文件
啟動時依需要讀取（不要一次全讀）：
- `實作進度.md` — 目前狀態、最新完成、Phase 3 待辦、技術債
- `PLAN.md` — 設計哲學索引，細節見 `docs/plan-*.md`
- `docs/DOC-STRUCTURE.md` — 文件組織規則

## 實作計畫分兩條線

| 計畫 | 文件 | 說明 |
|------|------|------|
| **主線開發** | `PLAN.md` + `實作進度.md` | 遊戲系統（VM / 世界 / UI），按 Phase 推進 |
| **世界觀導入** | `docs/plan-worldlore-integration.md` | 蒼究世界觀整合，執行順序獨立，可與主線並行 |

兩條線互不阻塞；世界觀計畫的 W-N 項目完成後**不需要**更新 `實作進度.md`（它有自己的狀態標記），但若改動了現有 C# 系統，仍需同步 `實作進度.md`。

---

## 強制規則

### 🔴 每次實作或修改檔案後，必須同步 `實作進度.md`

具體要做：
1. 在「最新完成」表格加入新一行（功能、關鍵檔案、一句摘要）
2. 如果有影響 Phase 3 待辦或技術債，同步更新對應區塊
3. 更新標頭的「最後更新」日期

**不接受「等稍後再補」。每個 commit 前都要確認 `實作進度.md` 已更新。**

---

## 其他工作習慣

- **Build 必須 0 錯誤 0 警告**：每次改完 C# 都跑 `dotnet build`，有錯立刻修
- **Commit 粒度**：功能完成 + 進度同步 = 一個 commit，不要把進度更新單獨拆出來
- **歷史歸檔**：Phase 完成後把 `實作進度.md` 的詳細段落移到 `docs/history/`，讓 md 保持精簡
- **待辦勾掉**：`實作進度.md` 的 `[ ]` 待辦完成後，立刻改為 `[x]`；不要讓未勾項目積累
- **PLAN.md 索引同步**：新增 `docs/plan-*.md` 計畫檔案後，必須同步更新 `PLAN.md` 的文件結構 code block 和「什麼內容放哪裡」表格
- **最新完成表格修剪**：「最新完成」超過 **8 筆**時，執行下方腳本（保留最新 5 筆，舊紀錄追加到 `docs/history/phase3.md`）：
  ```powershell
  powershell -ExecutionPolicy Bypass -File docs\archive-done.ps1
  ```
  流程：先加新紀錄到表格 → 判斷筆數 → 超過 8 筆才執行腳本。

## 技術注意事項

- VM 執行：`SpellCaster.ExecuteEffects`（同步）vs `SpellRunner`（跨幀，Wait 真實計時）
- PlayerBody 法陣走 SpellRunner；Projectile / Contact 命中走同步路徑
- `BlockNode.Params` 值型別：序列化用 `JsonElement`，執行時用 `object?`（`GetParam<T>` pattern-match）
- 敵人：Melee（紅）/ Ranged（橙）/ Patrol（藍紫）/ Heavy（暗紅 2×2）
