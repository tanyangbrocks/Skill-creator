namespace SkillCreator.World;

using SkillCreator.World.Materials;

// 遠程敵人發射的弓箭型投射物
public sealed class EnemyProjectile
{
    public GridPos Position { get; private set; }
    public bool    IsAlive  { get; private set; } = true;

    private readonly int   _dir;
    private readonly float _damage;
    private float _moveTimer;
    private int   _remaining;

    private const float MoveInterval = 0.08f;
    private const int   MaxRange     = 22;

    public EnemyProjectile(GridPos start, int dir, float damage)
    {
        Position   = new GridPos(start.X + dir, start.Y);  // 從身前一格出發
        _dir       = dir;
        _damage    = damage;
        _remaining = MaxRange;
    }

    public void Update(TileWorld3D world, PlayerController player, float delta)
    {
        if (!IsAlive) return;

        _moveTimer -= delta;
        if (_moveTimer > 0f) return;
        _moveTimer = MoveInterval;

        var next = new GridPos(Position.X + _dir, Position.Y);

        if (!world.InBoundsPublic(next.X, next.Y)) { IsAlive = false; return; }
        if (world.TypeAt(next.X, next.Y) != MaterialType.Air) { IsAlive = false; return; }

        // 命中玩家
        if (next == player.Position || Position == player.Position)
        {
            player.TakeDamage(_damage);
            IsAlive = false;
            return;
        }

        Position = next;
        if (--_remaining <= 0) IsAlive = false;
    }
}
