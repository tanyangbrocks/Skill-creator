namespace SkillCreator.World;

using SkillCreator.World.Materials;

public class EnemyManager
{
    public List<Enemy> Enemies { get; } = new();

    private readonly List<EnemyProjectile> _bolts = new();
    public IReadOnlyList<EnemyProjectile> EnemyProjectiles => _bolts;

    private const float FireDps = 25f;
    private const float LavaDps = 40f;
    private const float BoltDamage = 12f;

    public void Spawn(GridPos pos, EnemyType type = EnemyType.Melee, float maxHp = -1f)
        => Enemies.Add(new Enemy(pos, type, maxHp));

    public void Update(TileWorld world, PlayerController player, float delta)
    {
        // ── 敵人更新 ────────────────────────────────────────────
        foreach (var e in Enemies)
        {
            if (!e.IsAlive)
            {
                if (e.TickRespawn(delta)) e.Respawn();
                continue;
            }

            e.Update(world, player, delta);

            var mat = world.TypeAt(e.Position.X, e.Position.Y);
            if      (mat == MaterialType.Fire) e.TakeDamage(FireDps * delta);
            else if (mat == MaterialType.Lava) e.TakeDamage(LavaDps * delta);

            if (!e.IsAlive) { player.GainXp(e.XpReward); e.StartRespawn(); continue; }

            // 遠程敵人想要發射
            if (e.WantsToFire)
                _bolts.Add(new EnemyProjectile(e.Position, e.FacingX, BoltDamage));
        }

        // ── 敵方投射物更新 ─────────────────────────────────────
        _bolts.RemoveAll(b => !b.IsAlive);
        foreach (var b in _bolts)
            b.Update(world, player, delta);
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
            {
                e.TakeDamage(damage);
                CombatState.OnHit?.Invoke(e.Position, damage, false);
            }
        }
    }
}
