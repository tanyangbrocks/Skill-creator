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

    // G-5: 更新單一角色資料（load-update-save）
    public static void SaveCharacter(CharacterSaveData character)
    {
        var (chars, worlds) = Load();
        int idx = chars.FindIndex(c => c.Id == character.Id);
        if (idx >= 0) chars[idx] = character;
        else chars.Add(character);
        Save(chars, worlds);
    }

    // G-5: 更新單一世界資料（load-update-save）
    public static void SaveWorld(WorldSaveData world)
    {
        var (chars, worlds) = Load();
        int idx = worlds.FindIndex(w => w.Id == world.Id);
        if (idx >= 0) worlds[idx] = world;
        else worlds.Add(world);
        Save(chars, worlds);
    }

    // G-5: 計算世界存檔目錄（OS 絕對路徑）
    public static string MakeWorldDir(WorldSaveData world)
    {
        var safeName = System.Text.RegularExpressions.Regex.Replace(world.Name, @"[^\w\-]", "_");
        return ProjectSettings.GlobalizePath($"user://worlds/{safeName}_{world.Id}/");
    }

    // G-6: 刪除世界（chunks/ 目錄 + 清 IsFirstEnter + 從清單移除後由呼叫端存檔）
    public static void DeleteWorld(WorldSaveData world, List<WorldSaveData> worlds)
    {
        if (world.WorldDir.Length > 0 && System.IO.Directory.Exists(world.WorldDir))
            System.IO.Directory.Delete(world.WorldDir, recursive: true);
        worlds.Remove(world);
    }

    // G-6: 刪除角色（從清單移除後由呼叫端存檔）
    public static void DeleteCharacter(CharacterSaveData character, List<CharacterSaveData> chars)
        => chars.Remove(character);
}
