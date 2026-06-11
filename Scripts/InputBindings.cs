namespace SkillCreator;

using Godot;
using System.Linq;
using System.Text.Json;
using System.IO;

/// <summary>
/// 全域鍵位管理。所有可繫結的行為以 string 常數定義；
/// 預設值在 _defaults（Key[]，單鍵為單元素陣列，組合鍵為多元素陣列）；
/// 單鍵行為注入 Godot InputMap，組合鍵由 _Process 自訂邏輯偵測。
/// Main._Ready() 呼叫 RegisterAll() 完成初始化。
/// </summary>
public static class InputBindings
{
    // ── 行為名稱常數 ─────────────────────────────────────────────────
    // 移動
    public const string MoveLeft       = "move_left";
    public const string MoveRight      = "move_right";
    public const string MoveForward    = "move_forward";
    public const string MoveBackward   = "move_backward";
    public const string Jump           = "jump";

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

    // 偵錯（不顯示在設定 UI，仍可在開發時重新繫結）
    public const string DebugCoord    = "debug_coord";
    public const string DebugVmTrace  = "debug_vm_trace";
    public const string DebugSurvival = "debug_survival";
    public const string DebugSnapTake = "debug_snap_take";
    public const string DebugSnapRoll = "debug_snap_roll";

    // ── 預設鍵位（Key[]：單鍵 = 單元素陣列） ─────────────────────
    private static readonly Dictionary<string, Key[]> _defaults = new()
    {
        [MoveLeft]       = new[] { Key.A },
        [MoveRight]      = new[] { Key.D },
        [MoveForward]    = new[] { Key.W },
        [MoveBackward]   = new[] { Key.S },
        [Jump]           = new[] { Key.Space },
        [EquipItem]      = new[] { Key.Q },
        [OpenInventory]  = new[] { Key.Z },
        [OpenEquipment]  = new[] { Key.X },
        [OpenEditor]     = new[] { Key.E },
        [OpenStats]      = new[] { Key.C },
        [TogglePaint]    = new[] { Key.F1 },
        [Hotbar1]        = new[] { Key.Key1 },
        [Hotbar2]        = new[] { Key.Key2 },
        [Hotbar3]        = new[] { Key.Key3 },
        [Hotbar4]        = new[] { Key.Key4 },
        [Hotbar5]        = new[] { Key.Key5 },
        [DebugCoord]     = new[] { Key.F2 },
        [DebugVmTrace]   = new[] { Key.F3 },
        [DebugSurvival]  = new[] { Key.F4 },
        [DebugSnapTake]  = new[] { Key.F5 },
        [DebugSnapRoll]  = new[] { Key.F6 },
    };

    // 目前有效鍵位（預設值 + 玩家自訂覆蓋）
    private static readonly Dictionary<string, Key[]> _current = new();

    private static readonly string SavePath =
        Path.Combine(OS.GetUserDataDir(), "bindings.json");

    // ── 公開 API ─────────────────────────────────────────────────────

    /// <summary>Main._Ready() 呼叫：從磁碟讀取自訂鍵位，並注入 Godot InputMap（僅單鍵行為）。</summary>
    public static void RegisterAll()
    {
        foreach (var (action, keys) in _defaults)
            _current[action] = keys;

        LoadFromFile();

        foreach (var (action, keys) in _current)
            if (keys.Length == 1) ApplyToInputMap(action, keys[0]);
    }

    /// <summary>取得目前某行為繫結的鍵位陣列。</summary>
    public static Key[] GetKeys(string action) =>
        _current.TryGetValue(action, out var k) ? k : System.Array.Empty<Key>();

    /// <summary>重新繫結某行為到新鍵位組合，並立即寫盤。單鍵才注入 InputMap。</summary>
    public static void Rebind(string action, Key[] newKeys)
    {
        if (!_defaults.ContainsKey(action)) return;
        _current[action] = newKeys;
        if (newKeys.Length == 1) ApplyToInputMap(action, newKeys[0]);
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
                .Where(kv =>
                {
                    if (!_defaults.TryGetValue(kv.Key, out var def)) return true;
                    if (def.Length != kv.Value.Length) return true;
                    return !def.SequenceEqual(kv.Value);
                })
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(k => k.ToString()).ToArray());
            File.WriteAllText(SavePath, JsonSerializer.Serialize(overrides));
        }
        catch { /* 存盤失敗靜默忽略 */ }
    }

    private static void LoadFromFile()
    {
        if (!File.Exists(SavePath)) return;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                File.ReadAllText(SavePath));
            if (raw == null) return;
            foreach (var (action, keyStrs) in raw)
            {
                if (!_defaults.ContainsKey(action)) continue;
                var keys = keyStrs
                    .Select(s => Enum.TryParse<Key>(s, out var k) ? k : Key.Unknown)
                    .Where(k => k != Key.Unknown)
                    .ToArray();
                if (keys.Length > 0) _current[action] = keys;
            }
        }
        catch { /* 格式損毀靜默忽略，繼續使用預設值 */ }
    }
}
