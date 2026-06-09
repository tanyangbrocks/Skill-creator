namespace SkillCreator.World;

using SkillCreator.World.Materials;

public class PlayerController
{
    public GridPos Position { get; set; }
    public GridPos Facing   { get; private set; } = new GridPos(1, 0);

    public float Mp { get; set; }
    public const float MaxMp    = 100f;
    private const float MpRegen = 8f;   // MP/秒

    private float _moveCooldown = 0f;
    private float _castCooldown = 0f;
    private const float MoveInterval = 0.12f;

    public bool CanMove => _moveCooldown <= 0f;
    public bool CanCast => _castCooldown <= 0f;

    public PlayerController(GridPos startPos)
    {
        Position = startPos;
        Mp = MaxMp;
    }

    public void Tick(float delta)
    {
        if (_moveCooldown > 0f) _moveCooldown -= delta;
        if (_castCooldown > 0f) _castCooldown -= delta;
        Mp = MathF.Min(MaxMp, Mp + MpRegen * delta);
    }

    // 嘗試向 (dx, dy) 移動一格；目標非 Air 則失敗
    public bool TryMove(TileWorld world, int dx, int dy)
    {
        if (!CanMove) return false;
        var next = new GridPos(Position.X + dx, Position.Y + dy);
        if (world.TypeAt(next.X, next.Y) != MaterialType.Air) return false;

        Position = next;
        Facing   = new GridPos(Math.Sign(dx), Math.Sign(dy));
        _moveCooldown = MoveInterval;
        return true;
    }

    public void SetCastCooldown(float seconds) => _castCooldown = seconds;
}
