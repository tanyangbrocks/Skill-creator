# SkillCreator — Claude Code 工作規則

## 專案概要
Godot 4.6.3 mono（.NET 8、C#）技能設計系統。玩家用類 Scratch 積木設計能力，效果與 Noita 式細胞自動機世界物理互動。

## 必讀文件
啟動時依需要讀取（不要一次全讀）：
- `實作進度.md` — 目前狀態、最新完成、Phase 3 待辦、技術債
- `PLAN.md` — 設計哲學索引，細節見 `docs/plan-*.md`
- `docs/DOC-STRUCTURE.md` — 文件組織規則

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

## 技術注意事項

- VM 執行：`SpellCaster.ExecuteEffects`（同步）vs `SpellRunner`（跨幀，Wait 真實計時）
- PlayerBody 法陣走 SpellRunner；Projectile / Contact 命中走同步路徑
- `BlockNode.Params` 值型別：序列化用 `JsonElement`，執行時用 `object?`（`GetParam<T>` pattern-match）
- 敵人：Melee（紅）/ Ranged（橙）/ Patrol（藍紫）/ Heavy（暗紅 2×2）
