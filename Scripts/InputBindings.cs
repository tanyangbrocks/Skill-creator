namespace SkillCreator;

using Godot;
using System.Text.Json;
using System.IO;

/// <summary>
/// 全域鍵位管理。所有可繫結的行為以 string 常數定義；
/// 預設值在 _defaults 字典；執行期可動態修改並存盤。
/// Main._Ready() 呼叫 RegisterAll() 完成初始化。
/// </summary>
public static class InputBindings
{
    // ── 行為名稱常數 ─────────────────────────────────────────────────
    // 移動
    public const string MoveLeft       = "move_left";
    public const string MoveRight      = "move_right";
    public const string Jump           = "jump";

    // 戰鬥
    public const string CastSpell      = "cast_spell";

    // 物品 / 裝備
    public const string EquipItem      = "equip_item";
    public const string OpenInventory  = "open_inventory";
    public const string OpenEquipment  = "open_equipment";

    // 介面
    public const string OpenEditor     = "open_editor";
    public const string OpenStats      = "open_stats";
    public const string TogglePaint    = "toggle_paint";

    // 熱鍵欄 1–5
    public const string Hotbar1 = "hotbar_1";
    public const string Hotbar2 = "hotbar_2";
    public const string Hotbar3 = "hotbar_3";
    public const string Hotbar4 = "hotbar_4";
    public const string Hotbar5 = "hotbar_5";

    // 技能槽 1–5（對應 Loadout）
    public const string SpellSlot1 = "spell_slot_1";
    public const string SpellSlot2 = "spell_slot_2";
    public const string SpellSlot3 = "spell_slot_3";
    public const string SpellSlot4 = "spell_slot_4";
    public const string SpellSlot5 = "spell_slot_5";

    // 偵錯（不顯示在設定 UI，仍可在開發時重新繫結）
    public const string DebugCoord    = "debug_coord";
    public const string DebugVmTrace  = "debug_vm_trace";
    public const string DebugSurvival = "debug_survival";
    public const string DebugSnapTake = "debug_snap_take";
    public const string DebugSnapRoll = "debug_snap_roll";

    // ── 預設鍵位 ─────────────────────────────────────────────────────
    private static readonly Dictionary<string, Key> _defaults = new()
    {
        [MoveLeft]       = Key.A,
        [MoveRight]      = Key.D,
        [Jump]           = Key.W,
        [CastSpell]      = Key.Space,
        [EquipItem]      = Key.Q,
        [OpenInventory]  = Key.I,
        [OpenEquipment]  = Key.P,
        [OpenEditor]     = Key.E,
        [OpenStats]      = Key.C,
        [TogglePaint]    = Key.F1,
        [Hotbar1]        = Key.Key1,
        [Hotbar2]        = Key.Key2,
        [Hotbar3]        = Key.Key3,
        [Hotbar4]        = Key.Key4,
        [Hotbar5]        = Key.Key5,
        [SpellSlot1]     = Key.Key1,
        [SpellSlot2]     = Key.Key2,
        [SpellSlot3]     = Key.Key3,
        [SpellSlot4]     = Key.Key4,
        [SpellSlot5]     = Key.Key5,
        [DebugCoord]     = Key.F2,
        [DebugVmTrace]   = Key.F3,
        [DebugSurvival]  = Key.F4,
        [DebugSnapTake]  = Key.F5,
        [DebugSnapRoll]  = Key.F6,
    };

    // 目前有效鍵位（預設值 + 玩家自訂覆蓋）
    private static readonly Dictionary<string, Key> _current = new();

    private static readonly string SavePath =
        Path.Combine(OS.GetUserDataDir(), "bindings.json");

    // ── 公開 API ─────────────────────────────────────────────────────

    /// <summary>Main._Ready() 呼叫：從磁碟讀取自訂鍵位，並注入 Godot InputMap。</summary>
    public static void RegisterAll()
    {
        // 先用預設值填充
        foreach (var (action, key) in _defaults)
            _current[action] = key;

        // 從存檔覆蓋
        LoadFromFile();

        // 注入 Godot InputMap
        foreach (var (action, key) in _current)
            ApplyToInputMap(action, key);
    }

    /// <summary>取得目前某行為繫結的鍵位。</summary>
    public static Key GetKey(string action) =>
        _current.TryGetValue(action, out var k) ? k : Key.Unknown;

    /// <summary>重新繫結某行為到新鍵位，並立即寫盤。</summary>
    public static void Rebind(string action, Key newKey)
    {
        if (!_defaults.ContainsKey(action)) return;
        _current[action] = newKey;
        ApplyToInputMap(action, newKey);
        SaveToFile();
    }

    /// <summary>將某行為重置為預設值。</summary>
    public static void ResetToDefault(string action)
    {
        if (_defaults.TryGetValue(action, out var def))
            Rebind(action, def);
    }

    /// <summary>所有可顯示於設定 UI 的行為（排除 debug 開頭）。</summary>
    public static IEnumerable<string> DisplayableActions =>
        _defaults.Keys.Where(a => !a.StartsWith("debug_"));

    // ── 內部 ─────────────────────────────────────────────────────────

    private static void ApplyToInputMap(string action, Key key)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);
        else
            InputMap.ActionEraseEvents(action);

        var ev = new InputEventKey { Keycode = key };
        InputMap.ActionAddEvent(action, ev);
    }

    private static void SaveToFile()
    {
        try
        {
            var overrides = _current
                .Where(kv => !_defaults.TryGetValue(kv.Key, out var def) || def != kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
            File.WriteAllText(SavePath, JsonSerializer.Serialize(overrides));
        }
        catch { /* 存盤失敗靜默忽略（路徑不存在等邊緣情況） */ }
    }

    private static void LoadFromFile()
    {
        if (!File.Exists(SavePath)) return;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(SavePath));
            if (raw == null) return;
            foreach (var (action, keyStr) in raw)
            {
                if (_defaults.ContainsKey(action) && Enum.TryParse<Key>(keyStr, out var key))
                    _current[action] = key;
            }
        }
        catch { /* 格式損毀靜默忽略，繼續使用預設值 */ }
    }
}
