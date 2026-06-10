namespace SkillCreator.UI;

using SkillCreator.AbilitySystem.Data;

// 完整圖騰與刻印資料庫（全部設計，數值待調整 ⚠️）
// 搜尋標記說明：
//   TODO-STUB  = 尚未實作，SpellCaster 完全未讀取此 Id
//   TODO-IMPL  = 有佔位邏輯，但不是真實效果（Phase 1 假設 / 粒子代替等）
public static class TotemLibrary
{
    // ════════════════════════════════════════════════════════════
    //  圖騰（27 種）
    // ════════════════════════════════════════════════════════════

    public static readonly List<TotemData> AllTotems = new()
    {
        // ── 範圍圖騰（無門檻）────────────────────────────────────
        // 定義技能的施放形狀/作用範圍
        new TotemData { Id = "area_fan",     DisplayName = "扇形衝擊",     Type = TotemType.Area, BaseAbilityPointCost = 6  }, // TODO-STUB: 前方扇形範圍，未實作真實扇形碰撞
        new TotemData { Id = "area_around",  DisplayName = "周身衝擊",     Type = TotemType.Area, BaseAbilityPointCost = 8  }, // TODO-STUB: 以自身為圓心全向，未實作圓形 AoE
        new TotemData { Id = "area_distant", DisplayName = "遠距圓形衝擊", Type = TotemType.Area, BaseAbilityPointCost = 10 }, // TODO-STUB: 游標位置圓形 AoE，未實作遠距定點 AoE
        new TotemData { Id = "area_beam",    DisplayName = "射線衝擊",     Type = TotemType.Area, BaseAbilityPointCost = 8  }, // TODO-STUB: 直線穿透射線，未接真實射線碰撞

        // ── 武技圖騰（無門檻）────────────────────────────────────
        // 玩家手中的武器種類決定可用招式；招式效果與武器屬性掛鉤
        new TotemData { Id = "technique_sword",  DisplayName = "劍技",  Type = TotemType.Technique, BaseAbilityPointCost = 8  }, // TODO-STUB: 需裝備劍類武器，招式軌跡/傷害未接武器屬性
        new TotemData { Id = "technique_punch",  DisplayName = "拳擊",  Type = TotemType.Technique, BaseAbilityPointCost = 6  }, // TODO-STUB: 近距離拳打，未接格鬥招式系統
        new TotemData { Id = "technique_shield", DisplayName = "盾防",  Type = TotemType.Technique, BaseAbilityPointCost = 7  }, // TODO-STUB: 需裝備盾類，防禦/反擊機制未實作

        // ── 投射物圖騰（無門檻）──────────────────────────────────
        // 發射可飛行的投射物（與 ContainerType.Projectile 容器配合）
        new TotemData { Id = "projectile_energy",   DisplayName = "能量投射", Type = TotemType.Projectile, BaseAbilityPointCost = 10 }, // TODO-STUB: 無形能量彈，可附加元素/法則，未接完整能量彈系統
        new TotemData { Id = "projectile_physical",  DisplayName = "實物投射", Type = TotemType.Projectile, BaseAbilityPointCost = 12 }, // TODO-STUB: 具現物體投出（石塊/武器等），物理碰撞未實作

        // ── 被動圖騰（無門檻）────────────────────────────────────
        // 讓法陣以被動模式運行；兩種變體決定觸發機制
        new TotemData { Id = "passive_continuous", DisplayName = "持續偵測", Type = TotemType.Passive, BaseAbilityPointCost = 3 }, // TODO-STUB: 每幀/每 Tick 持續執行條件判斷，未接 SpellRunner 持續輪詢
        new TotemData { Id = "passive_switch",     DisplayName = "開關式",   Type = TotemType.Passive, BaseAbilityPointCost = 4 }, // TODO-STUB: 首次觸發啟動、再次觸發關閉，開關狀態機未實作

        // ── 變幻圖騰（LV 20+）────────────────────────────────────
        new TotemData { Id = "morph_speed",      DisplayName = "加速形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 10, RequiredPlayerLevel = 20 }, // TODO-STUB: 速度加成未接 PlayerController 移速屬性
        new TotemData { Id = "morph_flight",     DisplayName = "飛行形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 15, RequiredPlayerLevel = 20 }, // TODO-STUB: 飛行物理未實作，暫以爆炸佔位
        new TotemData { Id = "morph_invisible",  DisplayName = "隱身形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 15, RequiredPlayerLevel = 20 }, // TODO-STUB: 隱身遮罩系統未實作
        new TotemData { Id = "morph_strengthen", DisplayName = "強化形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 12, RequiredPlayerLevel = 20 }, // TODO-STUB: 強化數值未接屬性系統
        new TotemData { Id = "morph_possession", DisplayName = "附體形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 20, RequiredPlayerLevel = 20 }, // TODO-STUB: 靈體附體/操控目標實體，未實作

        // ── 位移圖騰（LV 20+）────────────────────────────────────
        new TotemData { Id = "displace_dash",     DisplayName = "衝刺",      Type = TotemType.Displacement, BaseAbilityPointCost = 8,  RequiredPlayerLevel = 20 },
        new TotemData { Id = "displace_teleport", DisplayName = "瞬移",      Type = TotemType.Displacement, BaseAbilityPointCost = 15, RequiredPlayerLevel = 20 },
        new TotemData { Id = "displace_dodge",    DisplayName = "閃避翻滾",  Type = TotemType.Displacement, BaseAbilityPointCost = 10, RequiredPlayerLevel = 20 },
        new TotemData { Id = "displace_portal",   DisplayName = "傳送門",    Type = TotemType.Displacement, BaseAbilityPointCost = 18, RequiredPlayerLevel = 20 }, // TODO-STUB: 雙向傳送入口，與瞬移不同（門持續存在），未實作

        // ── 召喚圖騰（LV 30+）────────────────────────────────────
        new TotemData { Id = "summon_minion",    DisplayName = "召喚精靈",  Type = TotemType.Summon, BaseAbilityPointCost = 15, RequiredPlayerLevel = 30 }, // TODO-STUB: 無 AI 實體，暫以粒子特效代替
        new TotemData { Id = "summon_turret",    DisplayName = "召喚砲台",  Type = TotemType.Summon, BaseAbilityPointCost = 18, RequiredPlayerLevel = 30 }, // TODO-STUB: 無 AI 砲台，暫以石塊佔位
        new TotemData { Id = "summon_guardian",  DisplayName = "召喚護衛",  Type = TotemType.Summon, BaseAbilityPointCost = 14, RequiredPlayerLevel = 30 }, // TODO-STUB: 無 AI 護衛，暫以粒子特效代替
        new TotemData { Id = "summon_weapon",    DisplayName = "召喚武器",  Type = TotemType.Summon, BaseAbilityPointCost = 16, RequiredPlayerLevel = 30 }, // TODO-STUB: 具現浮空武器實體，未實作
        new TotemData { Id = "summon_building",  DisplayName = "召喚建築",  Type = TotemType.Summon, BaseAbilityPointCost = 22, RequiredPlayerLevel = 30 }, // TODO-STUB: 生成建築物/堡壘實體，未實作
        new TotemData { Id = "summon_vehicle",   DisplayName = "召喚載具",  Type = TotemType.Summon, BaseAbilityPointCost = 20, RequiredPlayerLevel = 30 }, // TODO-STUB: 召喚可乘坐載具，未實作

        // ── 領域圖騰（LV 50+）────────────────────────────────────
        new TotemData { Id = "domain_barrier", DisplayName = "結界",      Type = TotemType.Domain, BaseAbilityPointCost = 20, RequiredPlayerLevel = 50 }, // TODO-IMPL: 石塊圓環佔位，無真實阻擋邏輯
        new TotemData { Id = "domain_terrain", DisplayName = "地形改造",  Type = TotemType.Domain, BaseAbilityPointCost = 25, RequiredPlayerLevel = 50 }, // TODO-IMPL: 暫以爆炸改地形，無細粒度控制
        new TotemData { Id = "domain_weather", DisplayName = "天候操控",  Type = TotemType.Domain, BaseAbilityPointCost = 30, RequiredPlayerLevel = 50 }, // TODO-IMPL: 暫以水格降雨，無天候系統
    };

    // ════════════════════════════════════════════════════════════
    //  刻印（90 種）
    // ════════════════════════════════════════════════════════════

    public static readonly List<EngraveData> AllEngravings = new()
    {
        // ── 白：傷害型 ────────────────────────────────────────────
        new EngraveData { Id = "white_dmg",        DisplayName = "傷害增幅",  Color = EngraveColor.White, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 2 },
        new EngraveData { Id = "white_pen",        DisplayName = "穿透",      Color = EngraveColor.White, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f,               BaseCost = 2 }, // TODO-STUB: 穿透計算未接傷害結算層
        new EngraveData { Id = "white_fixed",      DisplayName = "固定傷害",  Color = EngraveColor.White, ScalingType = ScalingType.Linear,     ScalingCoefficient = 5f,  BaseEffect = 0f, BaseCost = 2 }, // TODO-STUB: 未接 VM 層傷害結算
        new EngraveData { Id = "white_crit",       DisplayName = "暴擊機率",  Color = EngraveColor.White, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.5f,               BaseCost = 3 }, // TODO-STUB: 暴擊系統未實作
        new EngraveData { Id = "white_multi_hit",  DisplayName = "連續傷害",  Color = EngraveColor.White, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,  BaseEffect = 1f, BaseCost = 4 }, // TODO-STUB: 單次施放命中多次，傷害計算未實作
        new EngraveData { Id = "white_ignore_def", DisplayName = "無視防禦",  Color = EngraveColor.White, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f,               BaseCost = 5 }, // TODO-STUB: 防禦無視計算未實作
        new EngraveData { Id = "white_ignore_res", DisplayName = "無視抗性",  Color = EngraveColor.White, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f,               BaseCost = 5 }, // TODO-STUB: 元素/法則抗性無視未實作

        // ── 橙：控制型 ────────────────────────────────────────────
        new EngraveData { Id = "orange_push",       DisplayName = "擊退",      Color = EngraveColor.Orange, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 1f,  BaseCost = 3 }, // TODO-STUB: 擊退物理未實作
        new EngraveData { Id = "orange_slow",       DisplayName = "減速",      Color = EngraveColor.Orange, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 3 }, // TODO-STUB: 移速 debuff 未實作
        new EngraveData { Id = "orange_freeze",     DisplayName = "凍結機率",  Color = EngraveColor.Orange, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 2.0f,               BaseCost = 4 }, // TODO-STUB: 凍結狀態未實作
        new EngraveData { Id = "orange_pull",       DisplayName = "牽引",      Color = EngraveColor.Orange, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 3 }, // TODO-STUB: 牽引物理未實作
        new EngraveData { Id = "orange_levitate",   DisplayName = "浮力",      Color = EngraveColor.Orange, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 4 }, // TODO-STUB: 垂直推力（上升/下降），未接物理引擎
        new EngraveData { Id = "orange_pos_swap",   DisplayName = "位置交換",  Color = EngraveColor.Orange, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 6 }, // TODO-STUB: 施法者與目標位置互換，未實作
        new EngraveData { Id = "orange_rand_tp",    DisplayName = "隨機傳送",  Color = EngraveColor.Orange, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 5 }, // TODO-STUB: 目標隨機傳送至附近位置，未實作
        new EngraveData { Id = "orange_prop_change",DisplayName = "性質改造",  Color = EngraveColor.Orange, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 7 }, // TODO-STUB: 改變物體物理性質（硬/軟/彈/輕/重等），未實作
        new EngraveData { Id = "orange_reflect_atk",DisplayName = "反射攻擊",  Color = EngraveColor.Orange, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 6 }, // TODO-STUB: 吸收/轉化/反射/轉向來襲攻擊，未實作
        new EngraveData { Id = "orange_charm",      DisplayName = "操控",      Color = EngraveColor.Orange, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f,               BaseCost = 8 }, // TODO-STUB: 操控 NPC / 野怪行為，未接 AI 系統

        // ── 藍：圖騰改造 ─────────────────────────────────────────
        new EngraveData { Id = "blue_multi",            DisplayName = "多段發動",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 1f,  BaseCost = 5 },
        // [移至被動圖騰層] blue_passive → passive_continuous / passive_switch（見 TotemType.Passive）
        // [移至被動圖騰層] blue_switch_mode → passive_switch
        new EngraveData { Id = "blue_chargeable",       DisplayName = "可儲存式",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0.5f, BaseEffect = 1f,  BaseCost = 5 }, // TODO-STUB: 儲存集氣後一次爆發，集氣系統未實作
        new EngraveData { Id = "blue_condition_trigger",DisplayName = "條件觸發",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 4 }, // TODO-STUB: 條件觸發邏輯未實作
        new EngraveData { Id = "blue_no_interrupt",     DisplayName = "不可打斷",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 4 }, // TODO-STUB: 姿態系統未實作（Phase 2）
        new EngraveData { Id = "blue_quick_cancel",     DisplayName = "快速取消",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0.1f, BaseEffect = 0f,  BaseCost = 2 }, // TODO-STUB: RC 取消窗口未實作
        new EngraveData { Id = "blue_track_trail",      DisplayName = "軌跡記錄",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0.5f, BaseEffect = 1f,  BaseCost = 4 }, // TODO-STUB: 投射物軌跡歷史記錄未實作
        new EngraveData { Id = "blue_track_replay",     DisplayName = "軌跡回播",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0.5f, BaseEffect = 1f,  BaseCost = 6 }, // TODO-STUB: 軌跡反向回播未實作（需配對軌跡記錄）

        // ── 紅：侵略效果 ─────────────────────────────────────────
        new EngraveData { Id = "red_stun",          DisplayName = "暈眩機率",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 2.0f,               BaseCost = 4 }, // TODO-STUB: 暈眩狀態系統未實作
        new EngraveData { Id = "red_instakill",     DisplayName = "瞬殺機率",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.1f,               BaseCost = 8 }, // TODO-STUB: 瞬殺判定未接敵人 HP 系統
        new EngraveData { Id = "red_combo_break",   DisplayName = "斷招",           Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.5f,               BaseCost = 4 }, // TODO-STUB: 斷招系統未實作（Phase 2 細化）
        new EngraveData { Id = "red_sense_deprive", DisplayName = "感官剝奪",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 5 }, // TODO-STUB: 感官剝奪效果未實作
        new EngraveData { Id = "red_stack_amplify", DisplayName = "狀態疊加強化",   Color = EngraveColor.Red, ScalingType = ScalingType.Linear,     ScalingCoefficient = 5f,   BaseEffect = 0f,  BaseCost = 3 }, // TODO-STUB: 狀態層數系統未實作
        new EngraveData { Id = "red_effect_steal",  DisplayName = "效果劫奪",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 6 }, // TODO-STUB: 替代效果（負向）— 目標 Buff 轉移至施法者
        new EngraveData { Id = "red_target_replace",DisplayName = "目標替代",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 7 }, // TODO-STUB: 替代效果（負向）— 命中改為對目標盟友生效
        new EngraveData { Id = "red_dot",           DisplayName = "持續傷害",       Color = EngraveColor.Red, ScalingType = ScalingType.Linear,     ScalingCoefficient = 2f,   BaseEffect = 5f,  BaseCost = 3 }, // TODO-STUB: DoT 每秒傷害，持續時間系統未實作
        new EngraveData { Id = "red_ability_seal",  DisplayName = "能力封印",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 7 }, // TODO-STUB: 封印目標指定法陣/能力，未實作
        new EngraveData { Id = "red_ability_copy",  DisplayName = "能力複製",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f,               BaseCost = 8 }, // TODO-STUB: 臨時複製目標法陣，未接技能複製積木
        new EngraveData { Id = "red_ability_steal", DisplayName = "能力奪取",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f,               BaseCost = 10}, // TODO-STUB: 掠奪目標法陣使用權，未接技能掠奪積木
        new EngraveData { Id = "red_burn",          DisplayName = "燒傷",           Color = EngraveColor.Red, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 3 }, // TODO-STUB: 火焰 DoT 狀態，未接元素狀態系統
        new EngraveData { Id = "red_bleed",         DisplayName = "流血",           Color = EngraveColor.Red, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 3 }, // TODO-STUB: 物理 DoT 狀態，未實作
        new EngraveData { Id = "red_petrify",       DisplayName = "石化",           Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.5f,               BaseCost = 6 }, // TODO-STUB: 使目標完全靜止，未接移動控制
        new EngraveData { Id = "red_fear",          DisplayName = "恐懼",           Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.5f,               BaseCost = 5 }, // TODO-STUB: 使目標逃離/無法攻擊，未接 AI 情緒
        new EngraveData { Id = "red_paralyze",      DisplayName = "麻痺",           Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.5f,               BaseCost = 5 }, // TODO-STUB: 使目標完全無法行動，未實作

        // ── 綠：輔助效果 ─────────────────────────────────────────
        new EngraveData { Id = "green_shield",       DisplayName = "護盾值",       Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 10f,  BaseEffect = 50f, BaseCost = 3 }, // TODO-STUB: 護盾系統未實作
        new EngraveData { Id = "green_heal",         DisplayName = "回復效果",     Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 5f,   BaseEffect = 20f, BaseCost = 2 }, // TODO-STUB: 治療計算未接 HP 系統
        new EngraveData { Id = "green_death_replace",DisplayName = "死亡替代",     Color = EngraveColor.Green, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f,               BaseCost = 8 }, // TODO-STUB: 替代效果（正向）— 致命傷改為 HP 剩 1
        new EngraveData { Id = "green_dmg_to_heal",  DisplayName = "傷害轉治療",   Color = EngraveColor.Green, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 6 }, // TODO-STUB: 替代效果（正向）— 傷害事件替換為等量回復
        new EngraveData { Id = "green_observe",      DisplayName = "觀測強化",     Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 2 }, // TODO-STUB: 觀測/資訊系統未實作
        new EngraveData { Id = "green_evasion",      DisplayName = "閃避強化",     Color = EngraveColor.Green, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 4 }, // TODO-STUB: 閃避率加成未接 CharacterStats
        new EngraveData { Id = "green_lifesteal",    DisplayName = "吸血",         Color = EngraveColor.Green, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 5 }, // TODO-STUB: 傷害轉回血（按比例），未接傷害事件
        new EngraveData { Id = "green_reflect",      DisplayName = "反彈",         Color = EngraveColor.Green, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 6 }, // TODO-STUB: 將受到的攻擊反彈給攻擊者，未接 ActionBus
        new EngraveData { Id = "green_invincible",   DisplayName = "無敵",         Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 10}, // TODO-STUB: 短暫無敵幀，未接傷害攔截
        new EngraveData { Id = "green_super_armor",  DisplayName = "霸體",         Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 7 }, // TODO-STUB: 免疫打斷（不免傷），未接姿態系統
        new EngraveData { Id = "green_tracking",     DisplayName = "追蹤",         Color = EngraveColor.Green, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f,               BaseCost = 5 }, // TODO-STUB: 技能/投射物自動追蹤最近目標，未實作
        new EngraveData { Id = "green_mark",         DisplayName = "附加標記",     Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 3 }, // TODO-STUB: 對目標施加可被其他效果讀取的標記，未接標籤系統

        // ── 紫：額外操作 ─────────────────────────────────────────
        new EngraveData { Id = "purple_discover",    DisplayName = "選擇型效果",    Color = EngraveColor.Purple, ScalingType = ScalingType.Linear, ScalingCoefficient = 0f,  BaseEffect = 2f, BaseCost = 5  }, // TODO-STUB: Discover 選項 UI 未實作
        new EngraveData { Id = "purple_rhythm",      DisplayName = "節奏輸入加強",  Color = EngraveColor.Purple, ScalingType = ScalingType.Linear, ScalingCoefficient = 0.5f,BaseEffect = 1f, BaseCost = 4  }, // TODO-STUB: 音遊輸入系統未實作
        new EngraveData { Id = "purple_puzzle",      DisplayName = "謎題挑戰",      Color = EngraveColor.Purple, ScalingType = ScalingType.Linear, ScalingCoefficient = 0f,  BaseEffect = 1f, BaseCost = 6  }, // TODO-STUB: 要求目標（或施法者）完成小遊戲才生效，未實作
        new EngraveData { Id = "purple_bullet_ctrl", DisplayName = "子彈操控",      Color = EngraveColor.Purple, ScalingType = ScalingType.Linear, ScalingCoefficient = 0f,  BaseEffect = 1f, BaseCost = 7  }, // TODO-STUB: 玩家手動控制投射物軌跡，未實作
        new EngraveData { Id = "purple_fps",         DisplayName = "第一人稱射擊",  Color = EngraveColor.Purple, ScalingType = ScalingType.Linear, ScalingCoefficient = 0f,  BaseEffect = 1f, BaseCost = 8  }, // TODO-STUB: 切換 FPS 視角進行精準瞄準射擊，視角系統未實作

        // ── 黃：能力限制（IsRestriction = true → TotalCost 為負，加入後回收 AP）──
        new EngraveData { Id = "yellow_cd",            DisplayName = "冷卻限制",   Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 1f, BaseEffect = 0f, BaseCost = 5, IsRestriction = true }, // TODO-STUB: 冷卻計時器未實作
        new EngraveData { Id = "yellow_mp",            DisplayName = "MP消耗限制", Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 1f, BaseEffect = 0f, BaseCost = 5, IsRestriction = true }, // TODO-STUB: MP 消耗限制未接能量系統
        new EngraveData { Id = "yellow_range",         DisplayName = "射程限制",   Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 1f, BaseEffect = 0f, BaseCost = 4, IsRestriction = true }, // TODO-STUB: 射程限制未接投射物參數
        new EngraveData { Id = "yellow_hp_cost",       DisplayName = "HP代價",     Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 2f, BaseEffect = 5f, BaseCost = 8, IsRestriction = true }, // TODO-STUB: HP 代價扣血未接玩家 HP
        new EngraveData { Id = "yellow_use_condition", DisplayName = "使用條件",   Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 1f, BaseEffect = 0f, BaseCost = 6, IsRestriction = true }, // TODO-STUB: 需滿足狀態/地形/血量等條件才可施放，設計介面待補
        new EngraveData { Id = "yellow_negative_cost", DisplayName = "負面代價",   Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 1f, BaseEffect = 0f, BaseCost = 7, IsRestriction = true }, // TODO-STUB: 施放後對自身施加負面效果作為代價，未接 ActionBus

        // ── 屬性（元素）：11 種 ───────────────────────────────────
        new EngraveData { Id = "elem_metal",   DisplayName = "金屬性",  Color = EngraveColor.Elemental, Element = ElementType.Metal,   ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 }, // TODO-STUB: 金屬性反應未實作
        new EngraveData { Id = "elem_wood",    DisplayName = "木屬性",  Color = EngraveColor.Elemental, Element = ElementType.Wood,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 }, // TODO-STUB: 木屬性反應未實作
        new EngraveData { Id = "elem_water",   DisplayName = "水屬性",  Color = EngraveColor.Elemental, Element = ElementType.Water,   ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 },
        new EngraveData { Id = "elem_fire",    DisplayName = "火屬性",  Color = EngraveColor.Elemental, Element = ElementType.Fire,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 },
        new EngraveData { Id = "elem_earth",   DisplayName = "土屬性",  Color = EngraveColor.Elemental, Element = ElementType.Earth,   ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 }, // TODO-STUB: 土屬性反應未實作
        new EngraveData { Id = "elem_ice",     DisplayName = "冰屬性",  Color = EngraveColor.Elemental, Element = ElementType.Ice,     ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f, BaseCost = 5 },
        new EngraveData { Id = "elem_wind",    DisplayName = "風屬性",  Color = EngraveColor.Elemental, Element = ElementType.Wind,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f, BaseCost = 5 }, // TODO-STUB: 風屬性反應未實作
        new EngraveData { Id = "elem_light",   DisplayName = "光屬性",  Color = EngraveColor.Elemental, Element = ElementType.Light,   ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.2f, BaseCost = 6 }, // TODO-STUB: 光屬性反應未實作
        new EngraveData { Id = "elem_dark",    DisplayName = "暗屬性",  Color = EngraveColor.Elemental, Element = ElementType.Dark,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.2f, BaseCost = 6 }, // TODO-STUB: 暗屬性反應未實作
        new EngraveData { Id = "elem_thunder", DisplayName = "雷屬性",  Color = EngraveColor.Elemental, Element = ElementType.Thunder, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.2f, BaseCost = 6 },
        new EngraveData { Id = "elem_poison",  DisplayName = "毒屬性",  Color = EngraveColor.Elemental, Element = ElementType.Poison,  ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f, BaseCost = 5 }, // TODO-STUB: 毒屬性反應未實作

        // ── 法則：14 種（LV 50 解鎖）————————————————————————————
        // TODO-STUB: 所有法則刻印均為資料佔位，法則碰撞系統待高層實作（§9 第三～五層）
        new EngraveData { Id = "law_time",      DisplayName = "時間", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 15, RequiredPlayerLevel = 50 }, // TODO-STUB
        new EngraveData { Id = "law_space",     DisplayName = "空間", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 15, RequiredPlayerLevel = 50 }, // TODO-STUB
        new EngraveData { Id = "law_genesis",   DisplayName = "造化", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 15, RequiredPlayerLevel = 50 }, // TODO-STUB
        new EngraveData { Id = "law_causality", DisplayName = "因果", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 15, RequiredPlayerLevel = 50 }, // TODO-STUB
        new EngraveData { Id = "law_cycle",      DisplayName = "輪迴", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f, BaseCost = 20, RequiredPlayerLevel = 65 }, // TODO-STUB
        new EngraveData { Id = "law_life_death", DisplayName = "生死", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f, BaseCost = 20, RequiredPlayerLevel = 65 }, // TODO-STUB
        new EngraveData { Id = "law_soul",       DisplayName = "靈魂", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f, BaseCost = 20, RequiredPlayerLevel = 65 }, // TODO-STUB
        new EngraveData { Id = "law_yin_yang",  DisplayName = "陰陽", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f, BaseCost = 20, RequiredPlayerLevel = 65 }, // TODO-STUB
        new EngraveData { Id = "law_extreme",   DisplayName = "極點", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.2f, BaseCost = 25, RequiredPlayerLevel = 80 }, // TODO-STUB
        new EngraveData { Id = "law_infinite",  DisplayName = "無量", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.2f, BaseCost = 25, RequiredPlayerLevel = 80 }, // TODO-STUB
        new EngraveData { Id = "law_create",    DisplayName = "創造", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.2f, BaseCost = 25, RequiredPlayerLevel = 80 }, // TODO-STUB
        new EngraveData { Id = "law_dimension", DisplayName = "次元", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.2f, BaseCost = 25, RequiredPlayerLevel = 80 }, // TODO-STUB
        new EngraveData { Id = "law_world",     DisplayName = "世界", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.1f, BaseCost = 30, RequiredPlayerLevel = 100 }, // TODO-STUB
        new EngraveData { Id = "law_chaos",     DisplayName = "混沌", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.1f, BaseCost = 30, RequiredPlayerLevel = 100 }, // TODO-STUB
    };
}
