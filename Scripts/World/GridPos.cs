namespace SkillCreator.World;

// 引擎無關的格子座標型別
public readonly record struct GridPos(int X, int Y)
{
    public static GridPos operator +(GridPos a, GridPos b) => new(a.X + b.X, a.Y + b.Y);
    public static GridPos operator -(GridPos a, GridPos b) => new(a.X - b.X, a.Y - b.Y);

    public float DistanceTo(GridPos other)
    {
        int dx = X - other.X;
        int dy = Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => $"({X}, {Y})";
}
