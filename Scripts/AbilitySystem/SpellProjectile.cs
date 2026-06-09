namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.World;
using SkillCreator.World.Materials;

// 投射物執行容器：施放時生成，移動到命中點後才執行效果
public class SpellProjectile
{
    public GridPos Position { get; private set; }
    public bool    IsAlive  { get; private set; } = true;

    private readonly GridPos          _dir;
    private readonly SpellArray       _spell;
    private readonly PlayerController _caster;
    private readonly EnemyManager?    _enemies;  // 連段/命中傷害用
    private readonly SpellLoadout?    _loadout;  // 連段用

    private float _moveTimer      = 0f;
    private int   _remainingTiles;

    private const float MoveInterval = 0.06f;
    private const int   MaxRange     = 55;

    public SpellProjectile(GridPos start, GridPos dir, SpellArray spell, PlayerController caster,
        EnemyManager? enemies = null, SpellLoadout? loadout = null)
    {
        Position       = start;
        _dir           = new GridPos(dir.X == 0 ? 1 : Math.Sign(dir.X), 0);
        _spell         = spell;
        _caster        = caster;
        _enemies       = enemies;
        _loadout       = loadout;
        _remainingTiles= MaxRange;
    }

    public void Update(TileWorld world, EnemyManager enemies, float delta)
    {
        if (!IsAlive) return;

        _moveTimer -= delta;
        if (_moveTimer > 0f) return;
        _moveTimer = MoveInterval;

        var next = new GridPos(Position.X + _dir.X, Position.Y + _dir.Y);

        if (!world.InBoundsPublic(next.X, next.Y)) { IsAlive = false; return; }

        // 命中非空氣地塊
        if (world.TypeAt(next.X, next.Y) != MaterialType.Air)
        {
            HitAt(next, world, enemies);
            return;
        }

        // 命中敵人
        foreach (var e in enemies.Enemies)
        {
            if (e.IsAlive && (e.Position == next || e.Position == Position))
            {
                e.TakeDamage(25f);
                HitAt(next, world, enemies);
                return;
            }
        }

        Position = next;
        if (--_remainingTiles <= 0) IsAlive = false;
    }

    private void HitAt(GridPos pos, TileWorld world, EnemyManager enemies)
    {
        // 臨時把施法者位置移到命中點，效果在該點爆發
        var orig = _caster.Position;
        _caster.Position = pos;
        SpellCaster.ExecuteEffects(_spell, _caster, world, _enemies, _loadout, atHitPoint: true);
        _caster.Position = orig;
        IsAlive = false;
    }
}
