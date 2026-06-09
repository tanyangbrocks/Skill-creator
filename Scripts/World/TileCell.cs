namespace SkillCreator.World;

using SkillCreator.World.Materials;

public struct TileCell
{
    public MaterialType Type;
    public byte Variant;   // 0–255，微小色差用
    public short Timer;    // 火燃燒 / 蒸汽消散倒計時
}
