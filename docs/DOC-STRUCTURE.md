# 文件結構說明

## 根目錄索引檔（輕量，每次對話必讀）

| 檔案 | 用途 | 目標行數 |
|------|------|---------|
| `PLAN.md` | 設計哲學摘要 + 文件導覽索引 | < 80 行 |
| `實作進度.md` | 最新完成項目 + Phase 3 待辦 + 技術債 | < 100 行 |

**原則**：這兩個檔案要保持精簡。每次完成新功能，只更新「最新完成」表格和技術債，不貼技術細節。

---

## docs/ 子檔案（依需要才讀）

### 設計文件

| 檔案 | 內容 | 新增規則 |
|------|------|---------|
| `plan-ability-system.md` | §1-11：圖騰、容器、刻印、VM 積木、能力點、安全機制、衝突結算 | 定義能力系統「是什麼」時加這裡 |
| `plan-references.md` | §12, §17A-E：Noita/Psi/Scratch 整合分析、外部遊戲深度解析 | 靈感來源、參考遊戲研究加這裡 |
| `plan-roadmap.md` | §13-16, §18, 附錄：介面方向、技術選型、實作順序、未來方向、術語表 | 實作計畫、技術決策加這裡 |
| `plan-worldlore-integration.md` | W-1~W-21：蒼究世界觀導入計畫，各項依賴關係與實作時需讀的源文件 | 世界觀導入進度與設計決策 |
| `plan-3d-conversion.md` | Phase 0–4：3D 化實作計畫，五個關鍵決策、各元件修改評估、自動化腳本 | 3D 化技術決策與進度 |
| `plan-w6-mp-multitype.md` | W-6：MP 多類型框架，設計決策 A–E、分階段實作 W-6A~F | W-6 MP 系統設計決策與實作計畫 |
| `plan-tile-scale.md` | Tile 尺度縮放（TileSize 常數）：影響地圖、實作順序、尺度比率分析 | 縮放視覺細粒度時讀這裡 |
| `plan-scale-world.md` | 玩家碰撞體擴大（BodyH）、世界擴張、效能優化、3D 採掘 Raycast — P/E/W/R 四系列 | 玩家尺度、世界大小、Raycast 採掘計畫 |
| `plan-mining-placement.md` | 採掘建築系統深化、多形狀放置（PlacementShape）、R 系列實作計畫、未來方向 | R 系列新功能設計決策 |

### 歷史存檔（唯讀，已完成功能的技術細節）

| 檔案 | 內容 |
|------|------|
| `history/phase1-2.md` | Phase 1 ~ Phase 2 所有已完成功能的詳細技術說明 |
| `history/phase3.md` | Phase 3+ 最新完成紀錄（由 `docs/archive-done.ps1` 自動歸檔） |
| `history/採掘建築系統設計.md` | 2D 採掘建築早期設計（已被 plan-mining-placement.md 取代，留存備查） |
| `history/split-docs.ps1` | 一次性文件遷移腳本（已執行完畢，**勿重跑**） |

---

## 歷史歸檔規則

每當 Phase N 完成，將 `實作進度.md` 中對應的 Phase N 詳細段落：
1. 附加到 `docs/history/phase3.md`（或對應的 history 檔案）
2. 在 `實作進度.md` 刪除詳細段落，只在「最新完成」表格留一行摘要

**最新完成表格自動修剪**：見 `CLAUDE.md` 的「最新完成表格修剪」規則；腳本為 `docs/archive-done.ps1`。
