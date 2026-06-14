# Godot 4 → Unreal Engine 5 遷移實作計畫

> 撰寫日期：2026-06-14
> 依據：專案全掃描報告 + UE5 遷移技術研究
> 目標引擎：UE5.4+ / C++
> 遷移策略：**重新設計，不翻譯**（不使用自動 C# → C++ 工具）

---

## 一、遷移決策背景

| 問題 | 說明 |
|------|------|
| Godot 現在就已經卡 | 空世界就有效能壓力，根本問題在 Godot 的 chunk mesh rebuild 管線 |
| Grain 計畫調升至 64+ | 每玩家單位佔 64×128×64 tile，世界體積是現在的 64 倍，Godot 無法應對 |
| 視覺目標（AAA 規格） | Nanite 高面數模型、Lumen 動態光照、城堡破碎、捏臉系統，Godot 均缺乏對應 pipeline |
| CA + 每 tile 物理 | 需要 GPU Compute Shader 卸載模擬，Godot 沒有成熟方案 |
| **結論** | UE5 是唯一合理的長期目標平台 |

---

## 二、關鍵技術決策

### 2-1 C# → C++ 策略：重新設計，不翻譯

自動翻譯工具（CodePorting、AlterNative 等）**不適用**於遊戲邏輯：
- C# GC vs C++ RAII 記憶體模型差距太大
- `virtual generic method`、LINQ、async/await 無對應翻譯
- 翻出的 C++ 通常比原版 C# 更慢

**做法：設計移植，程式碼重寫**
- 所有系統的設計（資料模型、狀態機邏輯、VM opcode 語義）直接沿用
- 程式碼用 C++ 重新實現，以 UE5 慣用模式替代 Godot API

### 2-2 Tile 世界渲染：RealtimeMeshComponent

| 方案 | 評估 |
|------|------|
| ProceduralMeshComponent | ✗ 每幀重建 mesh 成本高，記憶體是 RMC 兩倍 |
| **RealtimeMeshComponent (RMC)** | ✓ 動態更新比 PMC 快 2 倍，記憶體少 50%+，UE5 專用 |
| Voxel Plugin 2 | 適合 Minecraft 地形，CA 物理需另外疊，過度封裝 |

**選擇：RealtimeMeshComponent**
- GitHub：https://github.com/TriAxis-Games/RealtimeMeshComponent
- Greedy Meshing 邏輯從 C# 移植，只換 mesh 提交 API

### 2-3 CA 世界模擬：自訂 Compute Shader（RDG）

| 方案 | Grain 64+ 可行性 |
|------|----------------|
| CPU Task Graph 多執行緒 | 中（Grain 32 以下夠用，64 壓力大） |
| Niagara Grid2D Simulation Stage | 中（視覺層有效，gameplay 反饋延遲） |
| **自訂 Compute Shader + RDG** | ✓ 最大彈性，Grain 64+ 唯一選項 |

**選擇：Compute Shader + RDG（Render Dependency Graph）**
- Phase 1 先做 CPU 版本（與現有 C# 邏輯直接對應）
- Phase 3 做 GPU 版本（棋盤格 checkerboard 更新模式解決並行競爭）
- 參考：[GPU Falling Sand（meatbatgames）](https://meatbatgames.com/blog/falling-sand-gpu/)

### 2-4 UI：Slate（積木編輯器）+ UMG（遊戲內 HUD）

| 位置 | 框架 | 理由 |
|------|------|------|
| 積木技能編輯器 | **Slate SGraphEditor** | UE5 的 Blueprint 編輯器、Material 編輯器本身就是 Slate，拖曳節點原生支援 |
| 遊戲內 HUD / 選單 | **UMG** | 快速原型，BP 可視化佈局 |

積木 UI 路徑：
```
自訂 UEdGraph + UEdGraphNode（表示 BlockNode）
→ SGraphPanel / SGraphEditor（拖曳畫布）
→ 自訂 SGraphNode Widget（積木外觀）
```
參考：[UMG-Slate Compendium](https://github.com/YawLighthouse/UMG-Slate-Compendium)

### 2-5 模組結構：Lyra 式 GameFeature Plugin

```
YourGame/
├── Source/
│   ├── SkillCreatorCore/       ← 基礎型別、介面（無 gameplay 依賴）
│   ├── SkillCreatorRuntime/    ← 主 Gameplay Module（Player、World、AI）
│   └── SkillCreatorEditor/     ← 積木編輯器（僅 Editor 版本編譯）
└── Plugins/
    ├── AbilitySystem/          ← VM + 技能施放（可獨立測試）
    ├── VoxelWorld/             ← CA 世界 + Chunk + RMC 渲染
    └── SkillCreatorUI/         ← 積木編輯器 Slate UI
```

### 2-6 已驗證的 UE5 開發陷阱

#### 坑一：Live Coding + UHT 反射資料不同步 → 無預警 crash

**觸發條件**：開著 UE Editor（含 Live Coding）時，修改 `.h` 裡的 `UCLASS/UENUM/UPROPERTY/USTRUCT` 並熱編譯。

**原因**：Live Coding 把新 `.dll` patch 進正在執行的 process，但 UHT 的反射資料（`*.generated.h`、`TClassCompiledInDefer` 登記表）不會更新。新 `.h` 改變了 struct 的記憶體 layout，舊的 UObject 系統仍用舊 offset 讀寫 → crash 或靜默資料損壞。

**強制工作流**：
- **安全**（Live Coding 可用）：只改 `.cpp` 的純邏輯（switch/case 內容、算法本體）
- **危險，必須關閉 Editor + VS Rebuild**：改 `.h` 裡任何 `U*` 巨集修飾的成員或 enum

#### 坑二：FLatentActionManager 不適合 SpellRunner

`FLatentActionManager` 是 Blueprint Delay 節點的底層實現，設計為低頻使用。SpellRunner 需要每幀推進 VM 指針，應改用**手動 Tick 累加器**：

```cpp
struct FSpellRunner {
    TArray<FInstruction> Instructions;
    int32  ProgramCounter  = 0;
    float  TimeAccumulator = 0.f;

    // 在 AActor::Tick 或 GameMode Tick 裡每幀呼叫
    void Advance(float DeltaTime);
};
```

這樣效能可控、容易 debug。`FLatentActionInfo` 和 UE5.5+ 協程可在需要非常深的巢狀 async 語義時再考慮，但 SpellRunner 的跨幀 Wait 靠累加器足夠。

> 注意：CA 瓦片不跑 SpellRunner VM，兩個系統完全獨立。FLatentActionManager 的壓力來源是 SpellRunner 實例數，實際並發量是個位數到幾十個，不是問題根源；**可控性才是選手動 Tick 的真正理由。**

#### 坑三：TArray\<FBlockNode\> 遞迴樹的記憶體搬家

`TArray<T>` 是連續值記憶體。`FBlockNode` 含子分支陣列，擴容時會整棵子樹 deep copy，且舊指標全部懸空。

**錯誤做法**：
```cpp
TArray<FBlockNode> ThenBranch;  // ← 擴容時整棵子樹 deep copy
```

**正確做法**（單一擁有權樹，用 `TUniquePtr`）：
```cpp
USTRUCT()
struct FBlockNode {
    GENERATED_BODY()
    EBlockType Type;
    TMap<FString, FInstancedStruct> Params;
    // 子分支不加 UPROPERTY（UHT 無法追蹤 TUniquePtr，且 BlockNode 不需要 BP 序列化）
    TArray<TUniquePtr<FBlockNode>> ThenBranch;
    TArray<TUniquePtr<FBlockNode>> ElseBranch;
    TArray<TUniquePtr<FBlockNode>> LoopBody;
};
```

`TUniquePtr` vs `TSharedPtr`：BlockNode 的擁有權是嚴格父→子（樹），不需要引用計數和執行緒鎖，用 `TUniquePtr` 語義更精確、overhead 更低。

> BlockNode 是「設計完成後編譯一次、只讀執行」的 AST，這個問題只影響 spell 載入時期，不影響每幀執行。即便如此，在 M-1 階段就正確宣告，避免之後大量積木的 spell 載入期 stall。

---

## 三、現有專案系統遷移評估

> 掃描結果：106 個 C# 檔，17,529 行程式碼

| 系統 | 行數 | Godot 耦合 | 遷移難度 | UE5 對應 |
|------|------|-----------|---------|---------|
| VM 執行層（ExecutionLoop / SpellCompiler） | ~1,671 | 極低（只有 GD.Print） | **低** | 純 C++ struct/class，直接重寫 |
| 技能資料層（SpellArray / BlockNode / Data） | ~321 | 無 | **低** | UE5 USTRUCT |
| 元素/Aura 系統 | ~325 | 無 | **低** | 純 C++ |
| 快照/回滾系統 | ~342 | 無 | **低** | 純 C++ |
| 物品/裝備系統 | ~302 | 無 | **低** | 純 C++，可接 GAS ItemFragment |
| 技能施放（SpellCaster / SpellRunner） | ~1,650 | 極低 | **低** | 純 C++，SpellRunner 改用手動 Tick 累加器（見 §2-6 坑二） |
| 敵人 AI（Enemy / EnemyManager） | ~1,000 | 低（Vector3I → FIntVector） | **低-中** | C++ 狀態機，或升級為 Behavior Tree |
| 世界模擬（TileWorld3D / Chunk3D） | ~2,500 | 低（Vector 型別） | **中** | C++ + GPU Compute（Phase 3） |
| 存讀檔（FlowSaveSystem） | ~518 | 低（user:// 路徑） | **中** | UE FArchive + 自訂 JSON |
| Main 遊戲迴圈 | ~2,969 | 高（_Process / _Input） | **中** | AGameMode + APlayerController |
| 渲染（TileWorldRenderer3D） | ~800 | 高（ArrayMesh） | **中-高** | RealtimeMeshComponent |
| UI（AbilityEditorUI / ScriptCanvas） | ~3,818 | **極高**（全是 Control 節點） | **高** | Slate SGraphEditor + UMG（完全重寫） |

**遷移比例：**
- ~60% 純邏輯 → 幾乎直接重寫（語法換，設計不換）
- ~24% 混合 → 邏輯保留，API 替換
- ~16% UI/渲染 → 完全重新設計

---

## 四、架構對照表

| Godot | UE5 C++ |
|-------|---------|
| `Node3D` | `AActor` + `USceneComponent` |
| `Control` | `UUserWidget` (UMG) / `SWidget` (Slate) |
| `_Ready()` | `BeginPlay()` |
| `_Process(delta)` | `Tick(float DeltaTime)` |
| `Signal` / C# event | `DECLARE_DYNAMIC_MULTICAST_DELEGATE` |
| `Vector3I` | `FIntVector` |
| `Vector3` | `FVector` |
| `Dictionary<K,V>` | `TMap<K,V>` |
| `List<T>` | `TArray<T>` |
| `GD.Load<T>()` | `LoadObject<T>()` / `ConstructorHelpers::FObjectFinder` |
| `ArrayMesh` | `URuntimeMeshComponent` / `UProceduralMeshComponent` |
| `StandardMaterial3D` | `UMaterialInstanceDynamic` |
| `async/await` | `FLatentActionInfo` / UE Coroutine（5.5+） |
| `interface` | UE5 `IInterface` 抽象類 |

---

## 五、分階段實作計畫

### M-0 — 準備期（開始前）

- [ ] 在 GitHub 開 `feature/ue5-migration` branch
- [ ] 安裝 UE5.4+、Visual Studio 2022
- [ ] 建立空白 C++ UE5 專案，確認 build 環境
- [ ] 建立 Module 資料夾結構（見第二節 2-5）
- [ ] Godot `feature/3d-cutover` branch 封存，保留作邏輯對照

---

### M-1 — 核心資料層（低風險，最先做）

> 目標：建立 C++ 資料結構，讓後續系統有型別基礎

- [ ] `BlockNode.h/cpp`：對應現有 BlockNode（ThenBranch/ElseBranch/LoopBody 用 `TArray<FBlockNode>`）
- [ ] `BlockType.h`：OpCode enum → UE5 UENUM
- [ ] `SpellArray.h/cpp`：SpellSlot、SpellArray、EngraveData
- [ ] `ManaType.h/cpp`：ManaTypeRegistry（18 種基礎 MP）
- [ ] `WorldScale.h`：常數（Grain、PlayerW、PlayerH 等）
- [ ] `GridPos.h`：FIntVector wrapper（加 Manhattan distance 方法）
- [ ] `MaterialType.h`：Tile 材質 enum

Build 確認 0 錯誤，進入 M-2。

---

### M-2 — VM 執行層

> 目標：在 UE5 C++ 裡跑起技能 VM，不依賴任何渲染

- [ ] `Instruction.h/cpp`：OpCode + Params（`TMap<FString, FInstancedStruct>`）
- [ ] `ExecutionContext.h/cpp`：狀態、delegate 注入（EntityQuery、RaycastQuery）
- [ ] `ExecutionLoop.h/cpp`：OpCode dispatch table（switch/case，對應現有 ~50 個 opcode）
- [ ] `SpellCompiler.h/cpp`：BlockNode AST → Instruction list
- [ ] `SpellRunner.h/cpp`：跨幀執行容器（用 UE5 `FLatentActionManager` 或協程）
- [ ] 單元測試：用 UE5 Automation Test Framework 跑幾個技能腳本

---

### M-3 — 世界模擬（CPU 版）

> 目標：TileWorld3D + CA 在 C++ 裡跑，暫不做 GPU

- [ ] `TileCell.h`：tile 狀態資料
- [ ] `Chunk3D.h/cpp`：16³ tile 陣列 + dirty tracking
- [ ] `TileWorld3D.h/cpp`：Chunk 字典、GetTile/SetTile、CA 更新迴圈
- [ ] `WorldSaveData.h/cpp`：Chunk 二進位序列化
- [ ] `MapGenerator3D.h/cpp`：程序地形生成（Perlin noise → tile）
- [ ] 接入 UE5 Task Graph 做多執行緒 Chunk 更新

---

### M-4 — 角色與敵人（Gameplay 層）

> 目標：玩家和敵人在世界裡能移動、互動

- [ ] `PlayerController` → `ASkillCreatorCharacter`（AActor + movement）
- [ ] `CharacterStats.h/cpp`：HP/MP/屬性，對應 CharacterStats.cs
- [ ] `CharacterState.h/cpp`：生存數值（氧氣、體溫、飢餓等）
- [ ] `ElementalAuraComponent.h/cpp`：元素 Aura + 反應表
- [ ] `Enemy.h/cpp` + `EnemyManager.h/cpp`：四種 AI 狀態機（可升級為 Behavior Tree）
- [ ] `CombatState.h/cpp`：戰鬥/非戰鬥狀態切換

---

### M-5 — 技能施放整合

> 目標：玩家能施放技能，VM 執行結果影響世界

- [ ] `SpellCaster.h/cpp`：TryCast、消耗 MP、施放冷卻
- [ ] `SpellProjectile.h/cpp`：投射物移動、碰撞、命中回調
- [ ] 接通 SpellRunner ↔ TileWorld3D（技能效果修改 tile、爆炸等）
- [ ] 接通 SpellRunner ↔ EnemyManager（傷害、控制效果）

---

### M-6 — 渲染（RMC + 基礎視覺）

> 目標：世界可以正確顯示，瓦片有顏色/材質

- [ ] 建立 `VoxelWorldRenderer` Actor（持有 RealtimeMeshComponent）
- [ ] 移植 Greedy Meshing 邏輯（C# → C++，演算法不變）
- [ ] 每個 Chunk 對應一個 RMC Section，dirty 時重建該 Section
- [ ] 基礎材質：先用 vertex color 區分 tile 類型
- [ ] 相機控制（3D 視角，滑鼠旋轉，捲輪縮放）

---

### M-7 — 存讀檔

> 目標：角色資料 + 世界狀態可以持久化

- [ ] `FlowSaveSystem.h/cpp`：FArchive 二進位 + JSON 混合方案
- [ ] `CharacterSaveData.h/cpp`：SpellGroup、ManaSlots、裝備、物品
- [ ] Chunk 二進位存檔（與現有格式相容或重定義）
- [ ] 遊戲啟動時讀存檔，對照 Godot 版本驗證資料正確性

---

### M-8 — 基礎 HUD（UMG）

> 目標：有 HUD 可以遊玩，技能編輯器先用最簡版

- [ ] HP / MP 條（1–3 條依技能組動態產生）
- [ ] 技能熱鍵欄（10 格）
- [ ] 技能列表（可點擊選技能）
- [ ] 簡易積木編輯器（暫用 ListView 呈現，拖曳功能後續補）

---

### M-9 — 積木編輯器 UI（Slate）

> 目標：完整 ScriptCanvas 功能，對標現有 Godot 版本

- [ ] 自訂 `UEdGraphNode`（對應 BlockNode）
- [ ] 自訂 `SGraphPanel`（積木畫布，拖曳、磁吸、連結）
- [ ] 調色盤（積木庫側欄，搜尋、分類）
- [ ] 積木卡片（顯示類型色塊、參數 SpinBox / OptionButton）
- [ ] 儲存驗證邏輯（從 M-1 資料層沿用）
- [ ] 技能組切換（V 鍵，5 組）

---

### M-10 — GPU CA（Compute Shader，長期目標）

> 目標：Grain 64+ 世界 CA 模擬達到可接受幀率

- [ ] 設計 GPU Tile Buffer 格式（每 tile 2-4 byte 狀態）
- [ ] 實作棋盤格（checkerboard）Compute Shader（解決並行寫入競爭）
- [ ] RDG FRDGBuilder 整合，每幀 dispatch + readback
- [ ] Chunk 邊界交換邏輯（GPU ↔ CPU 同步）
- [ ] 效能測試：Grain 64 × 8 chunk radius 維持 60fps

---

## 六、Godot 版本與 UE5 版本並行策略

```
GitHub
├── main                    ← 穩定版（目前 Godot）
├── feature/3d-cutover      ← Godot 最新開發版（封存，作邏輯對照）
└── feature/ue5-migration   ← UE5 遷移（本計畫）
```

- Godot 版本保留，任何時候都能回滾
- 遷移期間如有新的邏輯設計，先在 Godot 版確認，再移植到 UE5
- M-1 到 M-5 完成後，進行首次功能對照測試（技能施放結果一致）

---

## 七、本次暫緩

| 項目 | 說明 |
|------|------|
| 捏臉系統 | 依賴 runtime skeletal mesh 變形，M-6 之後評估 |
| 城堡破碎 | Chaos Physics 整合，M-5 之後設計 |
| 角色動畫 | 需外部骨骼模型，AR 計畫（素材包）完成後接入 |
| 聯機/Replication | 目前無此計畫，UE5 Replication Graph 架構已預留 |
| GPU CA | M-10，需要 M-1~M-6 全部穩定後才做 |
| 複合 MP 體系（W-13） | 資料層設計可直接移植，實作時機與 W-6 相同 |

---

## 八、關鍵參考資源

| 資源 | 用途 |
|------|------|
| [RealtimeMeshComponent](https://github.com/TriAxis-Games/RealtimeMeshComponent) | Tile 世界動態 Mesh |
| [GPU Falling Sand（meatbatgames）](https://meatbatgames.com/blog/falling-sand-gpu/) | CA GPU 化原理 |
| [UE5NiagaraComputeShaderIntegration](https://github.com/Shadertech/UE5NiagaraComputeShaderIntegration) | Compute Shader 架構參考 |
| [UMG-Slate Compendium](https://github.com/YawLighthouse/UMG-Slate-Compendium) | Slate UI 完整指南 |
| [Lyra Starter Game](https://dev.epicgames.com/documentation/en-us/unreal-engine/lyra-sample-game-in-unreal-engine) | C++ Module 結構最佳範本 |
| [Tom Looman UE5 C++ Guide](https://tomlooman.com/unreal-engine-cpp-guide/) | C++ 入門到進階 |
| [Epic C++ Coding Standard](https://dev.epicgames.com/documentation/unreal-engine/epic-cplusplus-coding-standard-for-unreal-engine) | 命名規範 |
| [UE5 Procedural Voxel Mesh 教學](https://dev.epicgames.com/community/learning/tutorials/k8am/unreal-engine-procedural-voxel-mesh-generation) | Voxel Mesh 起點 |

---

## 九、實作順序總覽

```
M-0（環境準備）
  ↓
M-1（資料層）→ M-2（VM）→ M-3（世界模擬 CPU）
                              ↓
                          M-4（角色/敵人）→ M-5（技能施放）
                                               ↓
                                           M-6（渲染）→ M-7（存讀檔）→ M-8（基礎 HUD）
                                                                            ↓
                                                                        M-9（積木編輯器 Slate）
                                                                            ↓
                                                                        M-10（GPU CA，長期）
```

M-1 到 M-5 可以在沒有 UE5 Editor 顯示的情況下跑單元測試，
M-6 之後才需要啟動完整 UE5 Editor 做整合測試。
