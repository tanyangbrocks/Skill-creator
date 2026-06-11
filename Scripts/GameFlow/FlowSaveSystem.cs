namespace SkillCreator.GameFlow;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

// 角色 + 世界清單存讀檔：user://flowsave.json
public static class FlowSaveSystem
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private static string SavePath => ProjectSettings.GlobalizePath("user://flowsave.json");

    private sealed class Dto
    {
        public List<CharacterSaveData> Characters { get; set; } = [];
        public List<WorldSaveData>     Worlds     { get; set; } = [];
    }

    public static (List<CharacterSaveData> Chars, List<WorldSaveData> Worlds) Load()
    {
        try
        {
            if (!System.IO.File.Exists(SavePath))
                return ([], []);
            var dto = JsonSerializer.Deserialize<Dto>(
                System.IO.File.ReadAllText(SavePath), Opts) ?? new Dto();
            return (dto.Characters, dto.Worlds);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FlowSave] Load failed: {e.Message}");
            return ([], []);
        }
    }

    public static void Save(List<CharacterSaveData> chars, List<WorldSaveData> worlds)
    {
        try
        {
            var dto = new Dto { Characters = chars, Worlds = worlds };
            System.IO.File.WriteAllText(SavePath, JsonSerializer.Serialize(dto, Opts));
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FlowSave] Save failed: {e.Message}");
        }
    }
}
