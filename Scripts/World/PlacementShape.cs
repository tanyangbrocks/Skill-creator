namespace SkillCreator.World;

using System;
using System.Collections.Generic;

public enum PlacementShape
{
    Single,
    Cube,
    Sphere,
    Cylinder,
    Cone,
    TriPyramid,
}

public static class ShapeVoxels
{
    // TODO: V-4 FillJunction — 靜態方法 FillJunction(TileWorld3D, GridPos from, GridPos to, MaterialType)
    //   偵測兩形狀接觸面的法線方向，自動補上接合 tile，消除球-錐等半徑不一致的幾何空隙。
    //   待 V 系列視覺需求明確後實作。

    /// <summary>
    /// 回傳形狀內所有相對偏移量。radius=-1 時自動取 WorldScale.PlayerH/6。
    /// </summary>
    public static IEnumerable<(int dx, int dy, int dz)> GetOffsets(PlacementShape shape, int radius = -1)
    {
        if (radius < 0) radius = Math.Max(1, WorldScale.PlayerH / 6);

        switch (shape)
        {
            case PlacementShape.Single:
                yield return (0, 0, 0);
                break;

            case PlacementShape.Cube:
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                    yield return (dx, dy, dz);
                break;

            case PlacementShape.Sphere:
            {
                int r2 = radius * radius;
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                    if (dx * dx + dy * dy + dz * dz <= r2)
                        yield return (dx, dy, dz);
                break;
            }

            case PlacementShape.Cylinder:
            {
                int r2 = radius * radius;
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                    if (dx * dx + dz * dz <= r2)
                        yield return (dx, dy, dz);
                break;
            }

            case PlacementShape.Cone:
                // 錐形：apex 在放置點，向 +Y 方向展開（底面圓半徑 = radius）
                for (int dy = 0; dy <= radius; dy++)
                {
                    int cr = radius - dy;
                    int cr2 = cr * cr;
                    for (int dx = -cr; dx <= cr; dx++)
                    for (int dz = -cr; dz <= cr; dz++)
                        if (dx * dx + dz * dz <= cr2)
                            yield return (dx, dy, dz);
                }
                break;

            case PlacementShape.TriPyramid:
                // 三角錐：三角形底面（XZ），向 +Y 方向收縮
                for (int dy = 0; dy <= radius; dy++)
                {
                    int cr = radius - dy;
                    for (int dx = -cr; dx <= cr; dx++)
                    for (int dz = 0; dz <= cr - Math.Abs(dx); dz++)
                        yield return (dx, dy, dz);
                }
                break;
        }
    }
}
