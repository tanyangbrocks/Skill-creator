namespace SkillCreator.AbilitySystem.Elemental;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.Snapshot;
using Godot;

/// <summary>
/// 管理生物身上的多重元素 Aura 與元素狀態效果（W-3 核心元件）。
/// Enemy 和 PlayerController 各持有一份。
///
/// 疊層策略：A 方案 — 各層獨立計時，受 MaxStacks 上限控制。
/// 應用冷卻：環境接觸（Apply）每種元素 1 現實秒只觸發一次；技能命中（ApplyImmediate）無冷卻。
/// </summary>
public sealed class ElementalAuraComponent
{
    // ── 內部資料 ──────────────────────────────────────────────────────────

    private record struct AuraEntry(ElementType Element, float Duration);

    private readonly List<AuraEntry>                _auras   = new();
    private readonly List<ElementalStatusEffect>    _effects = new();
    private readonly Dictionary<ElementType, float> _cdLeft  = new();  // 環境接觸冷卻剩餘秒數

    // ── 可調常數 ──────────────────────────────────────────────────────────

    /// <summary>環境接觸（踩材質格 / 停留區域）的 Aura 應用冷卻（現實秒）。</summary>
    public const float ApplicationCooldownSec = 1.0f;

    /// <summary>未觸發反應時，Aura 在清單中的預設存在時間（秒）。</summary>
    public const float DefaultAuraDuration = 5.0f;

    // ── 聚合屬性（每幀由 Process() 重算，供 Enemy / PlayerController 讀取）──

    /// <summary>移速懲罰加總；0 = 正常，0.30 = 移動計時器延長 30%。</summary>
    public float SpeedPenalty     { get; private set; }

    /// <summary>是否完全無法移動（結凍 / 感電麻痺）。</summary>
    public bool  IsImmobilized    { get; private set; }

    /// <summary>受傷倍率加成加總；0 = 正常，0.20 = 多受 20% 傷害。</summary>
    public float DamageTakenBonus { get; private set; }

    /// <summary>防禦力懲罰加總（0–1）；0 = 正常，0.10 = 防禦降 10%。</summary>
    public float DefensePenalty   { get; private set; }

    /// <summary>
    /// 元素 Aura 對環境溫度的總偏移（°C）；由 PlayerController.UpdateEnvironment 讀取後疊加至基礎環境溫度。
    /// Fire +15 / Ice -20 / Water -8 每層；上限 ±AuraTempShiftMax。
    /// </summary>
    public float AuraTemperatureShift { get; private set; }

    // ── 元素 Aura 溫度偏移常數（W-5b 體溫系統連接）─────────────────
    public const float FireAuraTempShift  = +15f;  // ⚠️ 待平衡
    public const float IceAuraTempShift   = -20f;  // ⚠️ 待平衡
    public const float WaterAuraTempShift =  -8f;  // ⚠️ 待平衡
    public const float AuraTempShiftMax   =  50f;  // 偏移上下限（°C）

    // ── 主要 API ──────────────────────────────────────────────────────────

    /// <summary>
    /// 帶冷卻的 Aura 應用（環境接觸：踩材質格、停留在反應區域等）。
    /// 同一元素在 ApplicationCooldownSec 內只觸發一次。
    /// 回傳 true = 成功觸發（套用效果或加入 Aura 清單）。
    /// </summary>
    public bool Apply(ElementType element, float duration, IElementalTarget target)
    {
        if (_cdLeft.TryGetValue(element, out float cd) && cd > 0f) return false;
        _cdLeft[element] = ApplicationCooldownSec;
        ApplyInternal(element, duration, target);
        return true;
    }

    /// <summary>
    /// 無冷卻的 Aura 應用（技能命中 / CA 碰撞觸發）。
    /// </summary>
    public void ApplyImmediate(ElementType element, float duration, IElementalTarget target)
        => ApplyInternal(element, duration, target);

    // ── 快照 API（S-5）────────────────────────────────────────────────────

    /// <summary>擷取當前 Aura 與效果的不可變快照。</summary>
    public AuraSnapshot TakeSnapshot()
    {
        var auras = _auras
            .Select(a => new AuraEntryData(a.Element, a.Duration))
            .ToList();
        var effects = _effects
            .Select(e => new AuraEffectData(e.GetType(), e.RemainingDuration, e.GetAccumulatedState()))
            .ToList();
        return new AuraSnapshot(auras, effects);
    }

    /// <summary>從快照還原 Aura 與效果狀態（清除冷卻並重算聚合屬性）。</summary>
    public void RestoreFromSnapshot(AuraSnapshot snap)
    {
        _auras.Clear();
        _effects.Clear();
        _cdLeft.Clear();

        foreach (var a in snap.Auras)
            _auras.Add(new AuraEntry(a.Element, a.Duration));

        foreach (var e in snap.Effects)
        {
            var fx = (ElementalStatusEffect)Activator.CreateInstance(e.EffectType)!;
            fx.RemainingDuration = e.RemainingDuration;
            fx.RestoreAccumulatedState(e.AccumulatedState);
            _effects.Add(fx);
        }

        Recompute();
    }

    /// <summary>重置所有 Aura 與效果（敵人重生等場景切換時使用）。</summary>
    public void Reset()
    {
        _auras.Clear();
        _effects.Clear();
        _cdLeft.Clear();
        Recompute();
    }

    // ── 每幀更新 ──────────────────────────────────────────────────────────

    /// <summary>每幀呼叫：更新冷卻、Aura 存在時間、效果計時，並重算聚合屬性。</summary>
    public void Process(float delta, IElementalTarget target)
    {
        // 1. 應用冷卻計時
        var cdKeys = new List<ElementType>(_cdLeft.Keys);
        foreach (var k in cdKeys)
        {
            _cdLeft[k] -= delta;
            if (_cdLeft[k] <= 0f) _cdLeft.Remove(k);
        }

        // 2. Aura 存在計時（倒計時到 0 自動移除）
        for (int i = _auras.Count - 1; i >= 0; i--)
        {
            var a      = _auras[i];
            float newDur = a.Duration - delta;
            if (newDur <= 0f) _auras.RemoveAt(i);
            else              _auras[i] = a with { Duration = newDur };
        }

        // 3. 效果計時 + 每幀 tick
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var e = _effects[i];
            e.OnProcess(delta);
            e.RemainingDuration -= delta;
            if (e.RemainingDuration <= 0f)
                _effects.RemoveAt(i);
        }

        // 4. 重算聚合屬性
        Recompute();
    }

    // ── 內部輔助 ──────────────────────────────────────────────────────────

    private void ApplyInternal(ElementType element, float duration, IElementalTarget target)
    {
        // 掃描現有 Aura，找第一個可配對的反應
        for (int i = 0; i < _auras.Count; i++)
        {
            var existing = _auras[i].Element;
            var def = ElementalReactionTable.Lookup(element, existing);
            if (def == null) continue;

            // 找到反應：消耗現有 Aura
            _auras.RemoveAt(i);
            GD.Print($"[Elemental] {existing} + {element} → {def.Name}");

            // 套用元素狀態效果（若有）
            if (def.MakeStatusEffect != null)
                AddEffect(def.MakeStatusEffect(), target);

            // incoming element 已用於反應，不加入 Aura 清單
            return;
        }

        // 無反應：加入 Aura 清單，等待未來配對
        _auras.Add(new AuraEntry(element, duration));
    }

    private void AddEffect(ElementalStatusEffect fx, IElementalTarget target)
    {
        // 計算同類型現有層數，超過 MaxStacks 則放棄
        int count = 0;
        var fxType = fx.GetType();
        foreach (var e in _effects)
            if (e.GetType() == fxType) count++;

        if (count >= fx.MaxStacks) return;

        fx.OnApply(target);
        _effects.Add(fx);
        Recompute();
    }

    private void Recompute()
    {
        float spd = 0f, dmgBonus = 0f, defPen = 0f, tempShift = 0f;
        bool  immob = false;
        foreach (var e in _effects)
        {
            spd      += e.SpeedPenalty;
            dmgBonus += e.DamageTakenBonus;
            defPen   += e.DefensePenalty;
            immob    |= e.Immobilizes;
        }
        foreach (var a in _auras)
        {
            tempShift += a.Element switch
            {
                ElementType.Fire  => FireAuraTempShift,
                ElementType.Ice   => IceAuraTempShift,
                ElementType.Water => WaterAuraTempShift,
                _                 => 0f,
            };
        }
        SpeedPenalty         = spd;
        DamageTakenBonus     = dmgBonus;
        DefensePenalty       = MathF.Min(1f, defPen);  // 最多降到 0 防禦
        IsImmobilized        = immob;
        AuraTemperatureShift = Math.Clamp(tempShift, -AuraTempShiftMax, AuraTempShiftMax);
    }
}
