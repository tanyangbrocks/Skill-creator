namespace SkillCreator.World;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkillCreator.World.Materials;

/// <summary>
/// 追蹤所有由玩家放置的物件（PlacedUnit）。
/// 每個世界對應一個 placed-registry.json，隨存檔讀寫。
/// </summary>
public class PlacedObjectRegistry
{
    private readonly Dictionary<GridPos, int>    _tileToUnit = new();
    private readonly Dictionary<int, PlacedUnit> _units      = new();
    private int _nextId = 1;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── 公開 API ─────────────────────────────────────────────────────

    /// <summary>玩家完成一次放置後呼叫，登記整批 tiles 為一個新 Unit。</summary>
    public PlacedUnit Register(IEnumerable<GridPos> tiles, MaterialType mat)
    {
        var unit = new PlacedUnit(_nextId++, mat, tiles);
        if (unit.Tiles.Count == 0) return unit;

        _units[unit.Id] = unit;
        foreach (var pos in unit.Tiles)
            _tileToUnit[pos] = unit.Id;
        return unit;
    }

    /// <summary>查詢某格是否屬於某個 PlacedUnit。</summary>
    public bool TryGetUnit(GridPos pos, out PlacedUnit unit)
    {
        if (_tileToUnit.TryGetValue(pos, out int id) && _units.TryGetValue(id, out unit!))
            return true;
        unit = null!;
        return false;
    }

    /// <summary>
    /// tile 被任何原因破壞時呼叫。
    /// Damage >= 0.5 時觸發解體：剩餘 tiles 全部移出 Registry，Unit 刪除。
    /// </summary>
    public void NotifyDestroyed(GridPos pos)
    {
        if (!_tileToUnit.TryGetValue(pos, out int id)) return;
        _tileToUnit.Remove(pos);

        if (!_units.TryGetValue(id, out var unit)) return;
        unit.Tiles.Remove(pos);

        if (unit.Tiles.Count == 0 || !unit.IsIntact)
            Disintegrate(unit);
    }

    /// <summary>強制移除整個 Unit 的所有剩餘 tiles（完美移除 / 解體共用）。</summary>
    public void RemoveUnit(PlacedUnit unit)
    {
        foreach (var pos in unit.Tiles)
            _tileToUnit.Remove(pos);
        _units.Remove(unit.Id);
    }

    // ── 持久化 ────────────────────────────────────────────────────────

    private const string FileName = "placed-registry.json";

    public void Save(string worldDir)
    {
        if (string.IsNullOrEmpty(worldDir)) return;
        try
        {
            var records = _units.Values.Select(u => new UnitRecord(u)).ToArray();
            var json = JsonSerializer.Serialize(records, _jsonOpts);
            File.WriteAllText(Path.Combine(worldDir, FileName), json);
        }
        catch { /* 存盤失敗靜默忽略 */ }
    }

    public void Load(string worldDir)
    {
        if (string.IsNullOrEmpty(worldDir)) return;
        var path = Path.Combine(worldDir, FileName);
        if (!File.Exists(path)) return;
        try
        {
            var records = JsonSerializer.Deserialize<UnitRecord[]>(
                File.ReadAllText(path), _jsonOpts);
            if (records == null) return;

            _tileToUnit.Clear();
            _units.Clear();
            _nextId = 1;

            foreach (var r in records)
            {
                if (!Enum.TryParse<MaterialType>(r.Mat, out var mat)) continue;
                var orig    = r.Original.Select(p => new GridPos(p.X, p.Y, p.Z)).ToHashSet();
                var current = r.Tiles.Select(p => new GridPos(p.X, p.Y, p.Z)).ToHashSet();
                if (current.Count == 0) continue;

                var unit = new PlacedUnit(r.Id, mat, orig, current);
                if (!unit.IsIntact) continue;   // 存檔後重開不應有損壞超半數的 unit

                _units[unit.Id] = unit;
                foreach (var pos in unit.Tiles)
                    _tileToUnit[pos] = unit.Id;
                if (r.Id >= _nextId) _nextId = r.Id + 1;
            }
        }
        catch { /* 格式損毀靜默忽略，清空 Registry */ }
    }

    // ── 內部 ─────────────────────────────────────────────────────────

    private void Disintegrate(PlacedUnit unit)
    {
        foreach (var pos in unit.Tiles)
            _tileToUnit.Remove(pos);
        unit.Tiles.Clear();
        _units.Remove(unit.Id);
    }

    // ── JSON DTO ──────────────────────────────────────────────────────

    private class UnitRecord
    {
        public int        Id       { get; set; }
        public string     Mat      { get; set; } = "";
        public XYZ[]      Original { get; set; } = [];
        public XYZ[]      Tiles    { get; set; } = [];

        public UnitRecord() { }
        public UnitRecord(PlacedUnit u)
        {
            Id       = u.Id;
            Mat      = u.Mat.ToString();
            Original = u.Original.Select(p => new XYZ(p.X, p.Y, p.Z)).ToArray();
            Tiles    = u.Tiles.Select(p => new XYZ(p.X, p.Y, p.Z)).ToArray();
        }
    }

    private record XYZ(int X, int Y, int Z);
}
