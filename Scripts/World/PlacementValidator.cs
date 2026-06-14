namespace SkillCreator.World;

using SkillCreator.World.Materials;

public static class PlacementValidator
{
    /// <summary>
    /// 判斷能否在指定格放置材質。
    /// 目前規則：必須在邊界內且目標格為 Air（水上不可放等未來在此擴充）。
    /// </summary>
    public static bool CanPlace(TileWorld3D world, GridPos pos, MaterialType mat)
    {
        if (!world.InBounds(pos.X, pos.Y, pos.Z)) return false;
        return world.GetTile(pos.X, pos.Y, pos.Z) == MaterialType.Air;
    }
}
