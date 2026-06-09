namespace SkillCreator.AbilitySystem;

using System.Text.Json;
using Godot;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.VM;

// 法陣存讀檔：user://loadout.json（System.Text.Json，不依賴 Newtonsoft）
public static class SaveSystem
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    private static string SavePath =>
        ProjectSettings.GlobalizePath("user://loadout.json");

    // ─────────────────────────────────────────────────────────────
    //  DTO（只用於序列化，不帶遊戲邏輯）
    // ─────────────────────────────────────────────────────────────

    private sealed class DtoLoadout
    {
        public int        ActiveIndex { get; set; }
        public DtoSpell?[] Slots      { get; set; } = [];
    }

    private sealed class DtoSpell
    {
        public string   Name           { get; set; } = "";
        public int      ActivationType { get; set; }
        public int      Container      { get; set; }
        public float    CastDelay      { get; set; }
        public float    BaseMpCost     { get; set; }
        public int      SceneUseLimit  { get; set; }
        public string?  NextInCombo    { get; set; }
        public List<DtoSlot>      SpellSlots       { get; set; } = [];
        public List<DtoEngraving> GlobalEngravings { get; set; } = [];
        public List<DtoBlock>     Blocks           { get; set; } = [];
    }

    private sealed class DtoSlot
    {
        public string              Name       { get; set; } = "";
        public string?             TotemId    { get; set; }
        public List<DtoEngraving>  Engravings { get; set; } = [];
    }

    private sealed class DtoEngraving
    {
        public string Id  { get; set; } = "";
        public int    Pts { get; set; }
    }

    // Params 以 JsonElement 儲存，可精確還原 string / float / bool
    private sealed class DtoBlock
    {
        public string                          Type   { get; set; } = "";
        public Dictionary<string, JsonElement> Params { get; set; } = new();
        public List<DtoBlock>                  Then   { get; set; } = [];
        public List<DtoBlock>                  Else   { get; set; } = [];
        public List<DtoBlock>                  Loop   { get; set; } = [];
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    public static void Save(SpellArray?[] spells, int activeIndex)
    {
        try
        {
            var dto = new DtoLoadout
            {
                ActiveIndex = activeIndex,
                Slots       = spells.Select(ToDto).ToArray(),
            };
            System.IO.File.WriteAllText(SavePath, JsonSerializer.Serialize(dto, Opts));
            GD.Print($"[SaveSystem] 存檔成功：{SavePath}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveSystem] 存檔失敗：{ex.Message}");
        }
    }

    // totemMap / engraveMap 由呼叫方（UI 層）傳入，避免循環依賴
    public static (SpellArray?[] spells, int activeIndex) Load(
        Dictionary<string, TotemData>   totemMap,
        Dictionary<string, EngraveData> engraveMap)
    {
        var empty = (new SpellArray?[SpellLoadout.MaxSlots], 0);
        if (!System.IO.File.Exists(SavePath)) return empty;

        try
        {
            string json = System.IO.File.ReadAllText(SavePath);
            var dto = JsonSerializer.Deserialize<DtoLoadout>(json, Opts);
            if (dto is null) return empty;

            SpellArray?[] spells = new SpellArray?[SpellLoadout.MaxSlots];
            for (int i = 0; i < Math.Min(dto.Slots.Length, SpellLoadout.MaxSlots); i++)
                spells[i] = dto.Slots[i] is { } s ? FromDto(s, totemMap, engraveMap) : null;

            GD.Print($"[SaveSystem] 讀檔成功");
            return (spells, Math.Clamp(dto.ActiveIndex, 0, SpellLoadout.MaxSlots - 1));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveSystem] 讀檔失敗：{ex.Message}");
            return empty;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Serialize（SpellArray → DTO）
    // ─────────────────────────────────────────────────────────────

    private static DtoSpell? ToDto(SpellArray? s) => s is null ? null : new DtoSpell
    {
        Name             = s.Name,
        ActivationType   = (int)s.ActivationType,
        Container        = (int)s.Container,
        CastDelay        = s.CastDelay,
        BaseMpCost       = s.BaseMpCost,
        SceneUseLimit    = s.SceneUseLimit,
        NextInCombo      = s.NextInCombo,
        SpellSlots       = s.Slots.Select(SlotToDto).ToList(),
        GlobalEngravings = s.GlobalEngravings.Select(EngrToDto).ToList(),
        Blocks           = s.Blocks.Select(BlockToDto).ToList(),
    };

    private static DtoSlot SlotToDto(SpellSlot s) => new()
    {
        Name       = s.Name,
        TotemId    = s.Totem?.Id,
        Engravings = s.LocalEngravings.Select(EngrToDto).ToList(),
    };

    private static DtoEngraving EngrToDto(EngraveData e) =>
        new() { Id = e.Id, Pts = e.PointsInvested };

    private static DtoBlock BlockToDto(BlockNode b)
    {
        var p = new Dictionary<string, JsonElement>();
        foreach (var (k, v) in b.Params)
            p[k] = v switch
            {
                string s => JsonSerializer.SerializeToElement(s),
                float  f => JsonSerializer.SerializeToElement(f),
                int    i => JsonSerializer.SerializeToElement((float)i),  // 統一存為 float
                bool  bo => JsonSerializer.SerializeToElement(bo),
                _        => JsonSerializer.SerializeToElement<object?>(null),
            };
        return new DtoBlock
        {
            Type   = b.Type.ToString(),
            Params = p,
            Then   = b.ThenBranch.Select(BlockToDto).ToList(),
            Else   = b.ElseBranch.Select(BlockToDto).ToList(),
            Loop   = b.LoopBody.Select(BlockToDto).ToList(),
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Deserialize（DTO → SpellArray）
    // ─────────────────────────────────────────────────────────────

    private static SpellArray FromDto(DtoSpell dto,
        Dictionary<string, TotemData>   totemMap,
        Dictionary<string, EngraveData> engraveMap)
    {
        var spell = new SpellArray
        {
            Name           = dto.Name,
            ActivationType = (AbilityActivationType)dto.ActivationType,
            Container      = (ContainerType)dto.Container,
            CastDelay      = dto.CastDelay,
            BaseMpCost     = dto.BaseMpCost,
            SceneUseLimit  = dto.SceneUseLimit,
            NextInCombo    = dto.NextInCombo,
        };

        foreach (var sd in dto.SpellSlots)
        {
            var slot = new SpellSlot { Name = sd.Name };
            if (sd.TotemId is not null && totemMap.TryGetValue(sd.TotemId, out var t))
                slot.Totem = t;
            foreach (var ed in sd.Engravings)
                if (engraveMap.TryGetValue(ed.Id, out var eng))
                    slot.LocalEngravings.Add(CloneEngrave(eng, ed.Pts));
            spell.Slots.Add(slot);
        }

        foreach (var ed in dto.GlobalEngravings)
            if (engraveMap.TryGetValue(ed.Id, out var eng))
                spell.GlobalEngravings.Add(CloneEngrave(eng, ed.Pts));

        foreach (var bd in dto.Blocks)
            spell.Blocks.Add(BlockFromDto(bd));

        return spell;
    }

    // 刻印需要 clone，避免不同法陣共享同一個 PointsInvested 可寫欄位
    private static EngraveData CloneEngrave(EngraveData src, int pts) => new()
    {
        Id                  = src.Id,
        DisplayName         = src.DisplayName,
        Color               = src.Color,
        ScalingType         = src.ScalingType,
        ScalingCoefficient  = src.ScalingCoefficient,
        BaseEffect          = src.BaseEffect,
        BaseCost            = src.BaseCost,
        IsGlobal            = src.IsGlobal,
        IsRestriction       = src.IsRestriction,
        RequiredPlayerLevel = src.RequiredPlayerLevel,
        PointsInvested      = pts,
    };

    private static BlockNode BlockFromDto(DtoBlock dto)
    {
        Enum.TryParse<BlockType>(dto.Type, out var bt);
        var p = new Dictionary<string, object?>();
        foreach (var (k, v) in dto.Params)
            p[k] = JeToObject(v);
        return new BlockNode
        {
            Type       = bt,
            Params     = p,
            ThenBranch = dto.Then.Select(BlockFromDto).ToList(),
            ElseBranch = dto.Else.Select(BlockFromDto).ToList(),
            LoopBody   = dto.Loop.Select(BlockFromDto).ToList(),
        };
    }

    // JsonElement → object? (string / float / bool / null)
    // VM 的 GetParam<T> 依賴 val is T typed，所以型別必須準確
    private static object? JeToObject(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True   => (object?)true,
        JsonValueKind.False  => (object?)false,
        JsonValueKind.Number => (object?)e.GetSingle(),  // 全部還原為 float
        _                    => null,
    };
}
