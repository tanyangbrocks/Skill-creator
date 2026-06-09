namespace SkillCreator.UI;

using SkillCreator.AbilitySystem.Data;

// Phase 1 可用的圖騰與刻印定義（範本，不可修改）
public static class TotemLibrary
{
    public static readonly List<TotemData> AllTotems = new()
    {
        new TotemData { Id = "trigger_on_cast",      DisplayName = "主動觸發",  Type = TotemType.Trigger,    BaseAbilityPointCost = 4  },
        new TotemData { Id = "trigger_on_hit",       DisplayName = "命中觸發",  Type = TotemType.Trigger,    BaseAbilityPointCost = 6  },
        new TotemData { Id = "trigger_on_hp_low",    DisplayName = "血量觸發",  Type = TotemType.Trigger,    BaseAbilityPointCost = 8  },
        new TotemData { Id = "technique_slash",      DisplayName = "斬擊",      Type = TotemType.Technique,  BaseAbilityPointCost = 8  },
        new TotemData { Id = "technique_projectile", DisplayName = "投射物",    Type = TotemType.Technique,  BaseAbilityPointCost = 10 },
        new TotemData { Id = "technique_area",       DisplayName = "範圍效果",  Type = TotemType.Technique,  BaseAbilityPointCost = 12 },
    };

    public static readonly List<EngraveData> AllEngravings = new()
    {
        // 白：傷害
        new EngraveData { Id = "white_dmg",    DisplayName = "傷害增幅",    Color = EngraveColor.White,  ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 1.0f, BaseCost = 2 },
        new EngraveData { Id = "white_pen",    DisplayName = "穿透",        Color = EngraveColor.White,  ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 0.5f, BaseCost = 2 },
        // 綠：輔助
        new EngraveData { Id = "green_shield", DisplayName = "護盾值",      Color = EngraveColor.Green,  ScalingType = ScalingType.Linear,     ScalingCoefficient = 10f,  BaseEffect = 50f, BaseCost = 3 },
        new EngraveData { Id = "green_heal",   DisplayName = "回復效果",    Color = EngraveColor.Green,  ScalingType = ScalingType.Linear,     ScalingCoefficient = 5f,   BaseEffect = 20f, BaseCost = 2 },
        // 紅：侵略
        new EngraveData { Id = "red_stun",     DisplayName = "暈眩機率",    Color = EngraveColor.Red,    ScalingType = ScalingType.Hyperbolic, ScalingCoefficient = 2.0f, BaseCost = 4 },
        // 藍：圖騰改造
        new EngraveData { Id = "blue_multi",   DisplayName = "多段發動",    Color = EngraveColor.Blue,   ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 1f,  BaseCost = 5 },
        // 黃：限制（換點）
        new EngraveData { Id = "yellow_cd",    DisplayName = "冷卻限制",    Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 0 },
        new EngraveData { Id = "yellow_mp",    DisplayName = "MP消耗限制",  Color = EngraveColor.Yellow, ScalingType = ScalingType.Linear,     ScalingCoefficient = 1f,   BaseEffect = 0f,  BaseCost = 0 },
    };
}
