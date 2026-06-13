namespace SkillCreator.World;

using SkillCreator.World.Materials;

public class EnemyManager
{
    public List<Enemy> Enemies { get; } = new();

    private readonly List<EnemyProjectile> _bolts = new();
    public IReadOnlyList<EnemyProjectile> EnemyProjectiles => _bolts;

    private MobSpawnController? _spawner;

    private const float FireDps    = 25f;
    private const float LavaDps    = 40f;
    private const float BoltDamage = 12f;

    /// <summary>目前存活的 Common/Area 怪物數量（避免每幀 LINQ Count）。</summary>
    public int DynamicActiveCount { get; private set; }

    public void SetSpawner(MobSpawnController spawner) => _spawner = spawner;

    public void Spawn(GridPos pos, EnemyType type = EnemyType.Melee, float maxHp = -1f,
                      SpawnCategory category = SpawnCategory.Common)
    {
        Enemies.Add(new Enemy(pos, type, maxHp, category));
        if (category is SpawnCategory.Common or SpawnCategory.Area) DynamicActiveCount++;
    }

    public void Update(TileWorld3D world, PlayerController player, float delta)
    {
        // 生成器：ForceDespawn 超範圍怪物 + 嘗試新生成
        _spawner?.Update(world, player.Position, this, delta);

        // ── 敵人更新（反向迭代以便安全 RemoveAt）────────────────
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            var e = Enemies[i];

            if (!e.IsAlive)
            {
                // Common/Area 怪物死亡後直接移除，由生成器決定何時補新怪
                if (e.Category is SpawnCategory.Common or SpawnCategory.Area)
                {
                    DynamicActiveCount--;
                    Enemies.RemoveAt(i);
                }
                else if (e.TickRespawn(delta))
                    e.Respawn();
                continue;
            }

            e.Update(world, player, delta);

            var mat = world.GetTile(e.Position.X, e.Position.Y, e.Position.Z);
            if      (mat == MaterialType.Fire) e.TakeDamage(FireDps * delta);
            else if (mat == MaterialType.Lava) e.TakeDamage(LavaDps * delta);

            if (!e.IsAlive)
            {
                player.GainXp(e.XpReward);
                if (e.Category is SpawnCategory.Common or SpawnCategory.Area)
                {
                    DynamicActiveCount--;
                    Enemies.RemoveAt(i);
                }
                else
                    e.StartRespawn();
                continue;
            }

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
            int dz = e.Position.Z - center.Z;
            if (dx * dx + dy * dy + dz * dz <= r2)
            {
                e.TakeDamage(damage);
                CombatState.OnHit?.Invoke(e.Position, damage, false);
            }
        }
    }
}
