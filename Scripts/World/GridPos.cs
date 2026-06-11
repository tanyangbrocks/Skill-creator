namespace SkillCreator.World;

// 引擎無關的格子座標型別（Phase 1：升級為 3D，Y+ 向下）
public readonly record struct GridPos(int X, int Y, int Z)
{
    // 向後相容：大量舊碼使用 new GridPos(x, y)，Z 預設 0
    public GridPos(int x, int y) : this(x, y, 0) { }

    public static GridPos operator +(GridPos a, GridPos b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static GridPos operator -(GridPos a, GridPos b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public float DistanceTo(GridPos other)
    {
        int dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
}
