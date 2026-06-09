namespace SkillCreator.UI;

using SkillCreator.AbilitySystem.Data;

// 完整圖騰與刻印資料庫（全部設計，數值待調整 ⚠️）
public static class TotemLibrary
{
    // ════════════════════════════════════════════════════════════
    //  圖騰（22 種）
    // ════════════════════════════════════════════════════════════

    public static readonly List<TotemData> AllTotems = new()
    {
        // ── 觸發圖騰（無門檻）────────────────────────────────────
        new TotemData { Id = "trigger_on_cast",    DisplayName = "主動觸發",   Type = TotemType.Trigger,    BaseAbilityPointCost = 4  },
        new TotemData { Id = "trigger_on_hit",     DisplayName = "命中觸發",   Type = TotemType.Trigger,    BaseAbilityPointCost = 6  },
        new TotemData { Id = "trigger_on_hp_low",  DisplayName = "血量觸發",   Type = TotemType.Trigger,    BaseAbilityPointCost = 8  },
        new TotemData { Id = "trigger_on_kill",    DisplayName = "擊殺觸發",   Type = TotemType.Trigger,    BaseAbilityPointCost = 6  },
        new TotemData { Id = "trigger_periodic",   DisplayName = "定時觸發",   Type = TotemType.Trigger,    BaseAbilityPointCost = 5  },
        new TotemData { Id = "trigger_on_damaged", DisplayName = "受傷觸發",   Type = TotemType.Trigger,    BaseAbilityPointCost = 7  },

        // ── 武技圖騰（無門檻）────────────────────────────────────
        new TotemData { Id = "technique_slash",      DisplayName = "斬擊",      Type = TotemType.Technique,  BaseAbilityPointCost = 8  },
        new TotemData { Id = "technique_projectile", DisplayName = "投射物",    Type = TotemType.Technique,  BaseAbilityPointCost = 10 },
        new TotemData { Id = "technique_area",       DisplayName = "範圍效果",  Type = TotemType.Technique,  BaseAbilityPointCost = 12 },
        new TotemData { Id = "technique_beam",       DisplayName = "射線",      Type = TotemType.Technique,  BaseAbilityPointCost = 10 },
        new TotemData { Id = "technique_chain",      DisplayName = "連擊",      Type = TotemType.Technique,  BaseAbilityPointCost = 14 },

        // ── 變幻圖騰 ──────────────────────────────────────────────
        new TotemData { Id = "morph_speed",     DisplayName = "加速形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 10 },
        new TotemData { Id = "morph_flight",    DisplayName = "飛行形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 15 },
        new TotemData { Id = "morph_invisible", DisplayName = "隱身形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 15 },
        new TotemData { Id = "morph_strengthen",DisplayName = "強化形態",  Type = TotemType.Morph,  BaseAbilityPointCost = 12 },

        // ── 位移圖騰 ──────────────────────────────────────────────
        new TotemData { Id = "displace_dash",     DisplayName = "衝刺",      Type = TotemType.Displacement, BaseAbilityPointCost = 8  },
        new TotemData { Id = "displace_teleport", DisplayName = "瞬移",      Type = TotemType.Displacement, BaseAbilityPointCost = 15 },
        new TotemData { Id = "displace_dodge",    DisplayName = "閃避翻滾",  Type = TotemType.Displacement, BaseAbilityPointCost = 10 },

        // ── 召喚圖騰 ──────────────────────────────────────────────
        new TotemData { Id = "summon_minion",  DisplayName = "召喚精靈",  Type = TotemType.Summon, BaseAbilityPointCost = 15 },
        new TotemData { Id = "summon_turret",  DisplayName = "召喚砲台",  Type = TotemType.Summon, BaseAbilityPointCost = 18 },
        new TotemData { Id = "summon_guardian",DisplayName = "召喚護衛",  Type = TotemType.Summon, BaseAbilityPointCost = 14 },

        // ── 領域圖騰 ──────────────────────────────────────────────
        new TotemData { Id = "domain_barrier", DisplayName = "結界",      Type = TotemType.Domain, BaseAbilityPointCost = 20 },
        new TotemData { Id = "domain_terrain", DisplayName = "地形改造",  Type = TotemType.Domain, BaseAbilityPointCost = 25 },
        new TotemData { Id = "domain_weather", DisplayName = "天候操控",  Type = TotemType.Domain, BaseAbilityPointCost = 30 },
    };

    // ════════════════════════════════════════════════════════════
    //  刻印（54 種）
    // ════════════════════════════════════════════════════════════

    public static readonly List<EngraveData> AllEngravings = new()
    {
        // ── 白：傷害型 ────────────────────────────────────────────
        new EngraveData { Id = "white_dmg",   DisplayName = "傷害增幅",  Color = EngraveColor.White, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 2 },
        new EngraveData { Id = "white_pen",   DisplayName = "穿透",      Color = EngraveColor.White, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f,               BaseCost = 2 },
        new EngraveData { Id = "white_fixed", DisplayName = "固定傷害",  Color = EngraveColor.White, ScalingType = ScalingType.Linear,     ScalingCoefficient = 5f,  BaseEffect = 0f, BaseCost = 2 },
        new EngraveData { Id = "white_crit",  DisplayName = "暴擊機率",  Color = EngraveColor.White, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.5f,               BaseCost = 3 },

        // ── 橙：控制型 ────────────────────────────────────────────
        new EngraveData { Id = "orange_push",   DisplayName = "擊退",      Color = EngraveColor.Orange, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 1f,  BaseCost = 3 },
        new EngraveData { Id = "orange_slow",   DisplayName = "減速",      Color = EngraveColor.Orange, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 3 },
        new EngraveData { Id = "orange_freeze", DisplayName = "凍結機率",  Color = EngraveColor.Orange, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 2.0f,               BaseCost = 4 },
        new EngraveData { Id = "orange_pull",   DisplayName = "牽引",      Color = EngraveColor.Orange, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 3 },

        // ── 藍：圖騰改造 ─────────────────────────────────────────
        new EngraveData { Id = "blue_multi",       DisplayName = "多段發動",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 1f,  BaseCost = 5 },
        new EngraveData { Id = "blue_passive",     DisplayName = "被動模式",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 3 },
        new EngraveData { Id = "blue_no_interrupt",DisplayName = "不可打斷",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0f,   BaseEffect = 1f,  BaseCost = 4 },
        new EngraveData { Id = "blue_quick_cancel",DisplayName = "快速取消",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0.1f, BaseEffect = 0f,  BaseCost = 2 },
        new EngraveData { Id = "blue_track_trail", DisplayName = "軌跡記錄",   Color = EngraveColor.Blue, ScalingType = ScalingType.Linear,     ScalingCoefficient = 0.5f, BaseEffect = 1f,  BaseCost = 4 },

        // ── 紅：侵略效果 ─────────────────────────────────────────
        new EngraveData { Id = "red_stun",          DisplayName = "暈眩機率",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 2.0f,               BaseCost = 4 },
        new EngraveData { Id = "red_instakill",     DisplayName = "瞬殺機率",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.1f,               BaseCost = 8 },
        new EngraveData { Id = "red_combo_break",   DisplayName = "斷招",           Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.5f,               BaseCost = 4 },
        new EngraveData { Id = "red_sense_deprive", DisplayName = "感官剝奪",       Color = EngraveColor.Red, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 5 },
        new EngraveData { Id = "red_stack_amplify", DisplayName = "狀態疊加強化",   Color = EngraveColor.Red, ScalingType = ScalingType.Linear,     ScalingCoefficient = 5f,   BaseEffect = 0f,  BaseCost = 3 },

        // ── 綠：輔助效果 ─────────────────────────────────────────
        new EngraveData { Id = "green_shield",      DisplayName = "護盾值",       Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 10f,  BaseEffect = 50f, BaseCost = 3 },
        new EngraveData { Id = "green_heal",        DisplayName = "回復效果",     Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 5f,   BaseEffect = 20f, BaseCost = 2 },
        new EngraveData { Id = "green_death_replace",DisplayName="死亡替代",      Color = EngraveColor.Green, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f,               BaseCost = 8 },
        new EngraveData { Id = "green_dmg_to_heal", DisplayName = "傷害轉治療",   Color = EngraveColor.Green, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f,               BaseCost = 6 },
        new EngraveData { Id = "green_observe",     DisplayName = "觀測強化",     Color = EngraveColor.Green, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 2 },

        // ── 紫：額外操作 ─────────────────────────────────────────
        new EngraveData { Id = "purple_discover", DisplayName = "選擇型效果",  Color = EngraveColor.Purple, ScalingType = ScalingType.Linear, ScalingCoefficient = 0f, BaseEffect = 2f, BaseCost = 5 },
        new EngraveData { Id = "purple_rhythm",   DisplayName = "節奏輸入加強",Color = EngraveColor.Purple, ScalingType = ScalingType.Linear, ScalingCoefficient = 0.5f,BaseEffect = 1f,BaseCost = 4 },

        // ── 黃：能力限制（IsRestriction = true → TotalCost 為負，加入後回收 AP）──
        new EngraveData { Id = "yellow_cd",      DisplayName = "冷卻限制",   Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 1f,  BaseEffect = 0f, BaseCost = 5, IsRestriction = true },
        new EngraveData { Id = "yellow_mp",      DisplayName = "MP消耗限制", Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 1f,  BaseEffect = 0f, BaseCost = 5, IsRestriction = true },
        new EngraveData { Id = "yellow_range",   DisplayName = "射程限制",   Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 1f,  BaseEffect = 0f, BaseCost = 4, IsRestriction = true },
        new EngraveData { Id = "yellow_hp_cost", DisplayName = "HP代價",     Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear, ScalingCoefficient = 2f,  BaseEffect = 5f, BaseCost = 8, IsRestriction = true },

        // ── 屬性（元素）：11 種 ───────────────────────────────────
        new EngraveData { Id = "elem_metal",   DisplayName = "金屬性",  Color = EngraveColor.Elemental, Element = ElementType.Metal,   ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 },
        new EngraveData { Id = "elem_wood",    DisplayName = "木屬性",  Color = EngraveColor.Elemental, Element = ElementType.Wood,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 },
        new EngraveData { Id = "elem_water",   DisplayName = "水屬性",  Color = EngraveColor.Elemental, Element = ElementType.Water,   ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 },
        new EngraveData { Id = "elem_fire",    DisplayName = "火屬性",  Color = EngraveColor.Elemental, Element = ElementType.Fire,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 },
        new EngraveData { Id = "elem_earth",   DisplayName = "土屬性",  Color = EngraveColor.Elemental, Element = ElementType.Earth,   ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.8f, BaseCost = 4 },
        new EngraveData { Id = "elem_ice",     DisplayName = "冰屬性",  Color = EngraveColor.Elemental, Element = ElementType.Ice,     ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f, BaseCost = 5 },
        new EngraveData { Id = "elem_wind",    DisplayName = "風屬性",  Color = EngraveColor.Elemental, Element = ElementType.Wind,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f, BaseCost = 5 },
        new EngraveData { Id = "elem_light",   DisplayName = "光屬性",  Color = EngraveColor.Elemental, Element = ElementType.Light,   ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.2f, BaseCost = 6 },
        new EngraveData { Id = "elem_dark",    DisplayName = "暗屬性",  Color = EngraveColor.Elemental, Element = ElementType.Dark,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.2f, BaseCost = 6 },
        new EngraveData { Id = "elem_thunder", DisplayName = "雷屬性",  Color = EngraveColor.Elemental, Element = ElementType.Thunder, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.2f, BaseCost = 6 },
        new EngraveData { Id = "elem_poison",  DisplayName = "毒屬性",  Color = EngraveColor.Elemental, Element = ElementType.Poison,  ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f, BaseCost = 5 },

        // ── 法則：14 種（群星級 LV 50 解鎖）────────────────────────
        new EngraveData { Id = "law_time",      DisplayName = "時間", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 15, RequiredPlayerLevel = 50 },
        new EngraveData { Id = "law_space",     DisplayName = "空間", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 15, RequiredPlayerLevel = 50 },
        new EngraveData { Id = "law_genesis",   DisplayName = "造化", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 15, RequiredPlayerLevel = 50 },
        new EngraveData { Id = "law_causality", DisplayName = "因果", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 15, RequiredPlayerLevel = 50 },
        new EngraveData { Id = "law_cycle",      DisplayName = "輪迴", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f, BaseCost = 20, RequiredPlayerLevel = 65 },
        new EngraveData { Id = "law_life_death", DisplayName = "生死", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f, BaseCost = 20, RequiredPlayerLevel = 65 },
        new EngraveData { Id = "law_soul",       DisplayName = "靈魂", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f, BaseCost = 20, RequiredPlayerLevel = 65 },
        new EngraveData { Id = "law_yin_yang",  DisplayName = "陰陽", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.3f, BaseCost = 20, RequiredPlayerLevel = 65 },
        new EngraveData { Id = "law_extreme",   DisplayName = "極點", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.2f, BaseCost = 25, RequiredPlayerLevel = 80 },
        new EngraveData { Id = "law_infinite",  DisplayName = "無量", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.2f, BaseCost = 25, RequiredPlayerLevel = 80 },
        new EngraveData { Id = "law_create",    DisplayName = "創造", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.2f, BaseCost = 25, RequiredPlayerLevel = 80 },
        new EngraveData { Id = "law_dimension", DisplayName = "次元", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.2f, BaseCost = 25, RequiredPlayerLevel = 80 },
        new EngraveData { Id = "law_world",     DisplayName = "世界", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.1f, BaseCost = 30, RequiredPlayerLevel = 100 },
        new EngraveData { Id = "law_chaos",     DisplayName = "混沌", Color = EngraveColor.Law, ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.1f, BaseCost = 30, RequiredPlayerLevel = 100 },
    };
}
