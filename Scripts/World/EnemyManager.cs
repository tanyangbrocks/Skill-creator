namespace SkillCreator.World;

using SkillCreator.World.Materials;

public class EnemyManager
{
    public List<Enemy> Enemies { get; } = new();

    private const float FireDps = 25f;
    private const float LavaDps = 40f;

    public void Spawn(GridPos pos, float maxHp = 50f)
        => Enemies.Add(new Enemy(pos, maxHp));

    public void Update(TileWorld world, PlayerController player, float delta)
    {
        foreach (var e in Enemies)
        {
            if (!e.IsAlive) continue;
            e.Update(world, player, delta);

            var mat = world.TypeAt(e.Position.X, e.Position.Y);
            if      (mat == MaterialType.Fire) e.TakeDamage(FireDps * delta);
            else if (mat == MaterialType.Lava) e.TakeDamage(LavaDps * delta);
        }

        Enemies.RemoveAll(e => !e.IsAlive);
    }

    public void ApplyExplosionDamage(GridPos center, int radius, float damage)
    {
        int r2 = radius * radius;
        foreach (var e in Enemies)
        {
            if (!e.IsAlive) continue;
            int dx = e.Position.X - center.X;
            int dy = e.Position.Y - center.Y;
            if (dx * dx + dy * dy <= r2)
                e.TakeDamage(damage);
        }
    }
}
