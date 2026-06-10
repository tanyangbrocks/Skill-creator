using Godot;

namespace SkillCreator.World;

public readonly record struct GridPos3D(int X, int Y, int Z)
{
    public static GridPos3D operator +(GridPos3D a, GridPos3D b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static GridPos3D operator -(GridPos3D a, GridPos3D b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public float DistanceTo(GridPos3D other)
    {
        int dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public GridPos3D MoveX(int dx) => new(X + dx, Y, Z);
    public GridPos3D MoveY(int dy) => new(X, Y + dy, Z);
    public GridPos3D MoveZ(int dz) => new(X, Y, Z + dz);

    public Vector3 ToWorldPos(float tileSize = 1f) =>
        new(X * tileSize, Y * tileSize, Z * tileSize);

    public override string ToString() => $"({X}, {Y}, {Z})";

    // Y+ 向下（重力方向），6-鄰接偏移（CA 用）
    public static readonly (int dx, int dy, int dz)[] Neighbors6 =
    {
        ( 0, +1,  0),  // 下（重力方向）
        ( 0, -1,  0),  // 上
        (-1,  0,  0),  // 左
        ( 1,  0,  0),  // 右
        ( 0,  0, -1),  // 前
        ( 0,  0, +1),  // 後
    };
}
