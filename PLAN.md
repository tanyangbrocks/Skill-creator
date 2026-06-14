# 能力設計系統 — 實作計畫索引

> 設計核心：讓玩家用類似寫程式的方式，創建屬於自己的能力。
> 能力施放後的效果，真實地與世界物理互動。
> 能力點越多，可設計的能力越接近「無敵」。

---

## 文件結構

```
docs/
├── plan-ability-system.md         §1-11   能力系統設計（是什麼）
├── plan-references.md             §12,17  靈感來源與參考遊戲解析
├── plan-roadmap.md                §13-16,18,附錄  實作規劃與技術決策
├── plan-worldlore-integration.md   蒼究世界觀導入計畫 W-1~W-21
├── plan-3d-conversion.md           3D 化實作計畫 Phase 0–4
├── plan-w6-mp-multitype.md         MP 多類型框架 W-6 設計探討稿（待確認決策）
├── plan-w6-implementation.md      MP 多類型框架 W-6 正式實作計畫（決策已確認）
├── plan-tile-scale.md              Tile 尺度縮放系統（TileSize 常數）
├── plan-scale-world.md             玩家尺度・世界擴張・Raycast 採掘
├── plan-mining-placement.md        採掘建築・多形狀放置 R 系列
├── plan-placed-objects.md          放置物件系統・殘餘格・拉伸（R-6a~e）
├── plan-player-actions.md          玩家動作全清單・鍵位對照・系統依賴分析・實作順序
├── plan-asset-registry.md          素材包系統（Asset Pack Registry）AR-A~D
└── history/
    ├── phase1-2.md         Phase 1~2 完成內容存檔
    └── phase3.md           Phase 3+ 實作歷史（archive-done.ps1 歸檔）
```

詳細每份文件的內容範疇見 [docs/DOC-STRUCTURE.md](docs/DOC-STRUCTURE.md)。

### 什麼內容放哪裡

| 要加入的內容 | 目標檔案 |
|---|---|
| 能力系統的運作規則（圖騰、刻印、VM、安全機制等） | `docs/plan-ability-system.md` |
| 靈感來源、參考遊戲深度解析 | `docs/plan-references.md` |
| 介面設計、技術選型、實作時程、未來方向 | `docs/plan-roadmap.md` |
| 蒼究世界觀導入（W 系列）的設計決策與進度 | `docs/plan-worldlore-integration.md` |
| 3D 化技術決策與各元件改動評估 | `docs/plan-3d-conversion.md` |
| MP 多類型框架（W-6）設計探討稿（含待確認決策） | `docs/plan-w6-mp-multitype.md` |
| MP 多類型框架（W-6）正式實作計畫（決策已確認） | `docs/plan-w6-implementation.md` |
| Tile 尺度縮放（TileSize 常數系統）相關技術決策 | `docs/plan-tile-scale.md` |
| 玩家尺度・世界擴張・效能優化・採掘 Raycast | `docs/plan-scale-world.md` |
| 採掘建築系統・多形狀放置（R 系列）計畫 | `docs/plan-mining-placement.md` |
| 放置物件系統・殘餘格・拉伸系統（R-6 系列）計畫 | `docs/plan-placed-objects.md` |
| 玩家動作完整清單・鍵位衝突・系統依賴・實作排序 | `docs/plan-player-actions.md` |
| 素材包系統（Key 命名・Pack 格式・AR 分階段）  | `docs/plan-asset-registry.md` |

---

## 設計哲學摘要

三個靈感來源：

| 來源 | 貢獻 |
|------|------|
| **Scratch** | 視覺化積木介面、函式容器、拖拉組裝 |
| **Noita** | 執行流語義、物理互動、資源作為熔斷器 |
| **Psi（Minecraft Mod）** | 節點邏輯、變數運算、實體查詢 |
| **獵人（HxH）念能力系統** | 制約與誓言→限制換點哲學、念系六分類→圖騰類型靈感、能力個人化、能力隱密度 |
| **泰拉瑞亞** | Tile 採掘建築世界、Boss 進程門檻、裝備與職業多樣化 |
| **原神** | 元素反應系統（直接對應屬性碰撞設計）、聖遺物詞條疊加 |
| **逆水寒** | 系統模組化玩法設計→讓不同類型玩家只玩自己喜歡的系統，不強制涉及其他玩法 |

核心原則：**限制即資源**、**設計與執行分層**、**代價對應強度**。

---

## 系統架構速覽

```
能力設計系統
├── 圖騰系統（施放形式）
├── 能量符文系統（MP 種類）
├── 刻印系統（效果修飾器）
│   ├── 屬性刻印 / 法則刻印 / 邏輯刻印（程式積木）
│   └── 傷害 / 控制 / 輔助 / 限制型刻印
├── 能力點系統（資源預算）
└── 標籤系統（GameplayTags）
```

詳細規格見 `docs/plan-ability-system.md`。

---

*原始 PLAN.md 完整內容保留在 git 歷史中。*
