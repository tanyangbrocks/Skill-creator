namespace SkillCreator.World.Items;

using SkillCreator.World.Materials;

public class DroppedItem
{
    public GridPos   Position { get; set; }
    public ItemStack Stack    { get; set; }
    public float     LifeTime { get; set; } = 60f;
    public bool      IsAlive  => LifeTime > 0f && !Stack.IsEmpty;

    private float _fallTimer = 0f;
    private const float FallInterval = 0.18f;

    public DroppedItem(GridPos pos, ItemStack stack)
    {
        Position = pos;
        Stack    = stack;
    }

    public void Update(TileWorld3D world, float delta)
    {
        LifeTime -= delta;
        if (!IsAlive) return;

        // 重力：每 FallInterval 秒嘗試往下落一格
        _fallTimer -= delta;
        if (_fallTimer <= 0f)
        {
            _fallTimer = FallInterval;
            var below = new GridPos(Position.X, Position.Y + 1, Position.Z);
            if (world.GetTile(below.X, below.Y, below.Z) == MaterialType.Air)
                Position = below;
        }
    }
}
