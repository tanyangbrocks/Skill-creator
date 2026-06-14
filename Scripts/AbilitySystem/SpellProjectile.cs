namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.Elemental;
using SkillCreator.AbilitySystem.VM;
using SkillCreator.World;
using SkillCreator.World.Materials;

// 投射物執行容器：施放時生成，移動到命中點後才執行效果
public class SpellProjectile
{
    public GridPos Position { get; private set; }
    public bool    IsAlive  { get; private set; } = true;

    // 浮點位置 + 正規化 3D 方向向量
    private float _posX;
    private float _posY;
    private float _posZ;
    private readonly float             _dirX;
    private readonly float             _dirY;
    private readonly float             _dirZ;
    private readonly SpellArray        _spell;
    private readonly PlayerController  _caster;
    private readonly EnemyManager?     _enemies;
    private readonly SpellLoadout?     _loadout;
    private readonly SpellRunner?      _runner;

    private float _moveTimer      = 0f;
    private int   _remainingTiles;

    private const float MoveInterval = 0.06f;
    private const int   MaxRange     = 55;

    public SpellProjectile(GridPos start, float dirX, float dirY, float dirZ,
        SpellArray spell, PlayerController caster,
        EnemyManager? enemies = null, SpellLoadout? loadout = null, SpellRunner? runner = null)
    {
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        if (len < 0.001f) { dirX = 1f; dirY = 0f; dirZ = 0f; }
        else { dirX /= len; dirY /= len; dirZ /= len; }
        _dirX = dirX;
        _dirY = dirY;
        _dirZ = dirZ;
        _posX = start.X + 0.5f;
        _posY = start.Y + 0.5f;
        _posZ = start.Z + 0.5f;
        Position       = start;
        _spell         = spell;
        _caster        = caster;
        _enemies       = enemies;
        _loadout       = loadout;
        _runner        = runner;
        _remainingTiles= MaxRange;
    }

    public void Update(TileWorld3D world, EnemyManager enemies, float delta)
    {
        if (!IsAlive) return;

        _moveTimer -= delta;
        if (_moveTimer > 0f) return;
        _moveTimer = MoveInterval;

        _posX += _dirX;
        _posY += _dirY;
        _posZ += _dirZ;
        var next = new GridPos((int)MathF.Floor(_posX), (int)MathF.Floor(_posY), (int)MathF.Floor(_posZ));

        if (!world.InBounds(next.X, next.Y, next.Z)) { IsAlive = false; return; }

        // 命中非空氣地塊
        if (world.GetTile(next.X, next.Y, next.Z) != MaterialType.Air)
        {
            // W-3c：技能元素作用於命中的材質格
            var tileElem = _spell.PrimaryElement;
            if (tileElem != ElementType.None)
                world.ApplyElementalImpact(next.X, next.Y, next.Z, tileElem);

            HitAt(next, world, enemies);
            return;
        }

        // 命中敵人（Heavy 是 2×2 大小，需檢查整個碰撞盒）
        foreach (var e in enemies.Enemies)
        {
            if (e.IsAlive && (HitsEnemy(e, next) || HitsEnemy(e, Position)))
            {
                // W-3c：技能元素作用於命中的敵人（Apply Aura）
                var hitElem = _spell.PrimaryElement;
                if (hitElem != ElementType.None)
                    e.Aura.ApplyImmediate(hitElem, ElementalAuraComponent.DefaultAuraDuration, e);

                HitAt(next, world, enemies, new EntityInfo(e.Id, e.Position, e.Hp, e.MaxHp));
                return;
            }
        }

        Position = next;
        if (--_remainingTiles <= 0) IsAlive = false;
    }

    // 投射物格子 p 是否落在敵人的碰撞盒內（Heavy 佔 2 寬 × 2 高）
    private static bool HitsEnemy(Enemy e, GridPos p)
    {
        int w = e.Type == EnemyType.Heavy ? 2 : 1;
        int h = e.Type == EnemyType.Heavy ? 2 : 1;
        return p.X >= e.Position.X && p.X < e.Position.X + w
            && p.Y >= e.Position.Y - (h - 1) && p.Y <= e.Position.Y
            && p.Z == e.Position.Z;
    }

    private void HitAt(GridPos pos, TileWorld3D world, EnemyManager enemies, EntityInfo? hitTarget = null)
    {
        if (_runner != null)
        {
            _runner.Submit(_spell, _caster, world, _enemies, _loadout, fixedOrigin: pos, hitTarget: hitTarget);
        }
        else
        {
            var orig = _caster.Position;
            _caster.Position = pos;
            SpellCaster.ExecuteEffects(_spell, _caster, world, _enemies, _loadout, atHitPoint: true, hitTarget: hitTarget);
            _caster.Position = orig;
        }
        IsAlive = false;
    }
}
