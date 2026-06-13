namespace SkillCreator.World;

using SkillCreator.AbilitySystem;
using SkillCreator.AbilitySystem.Elemental;
using SkillCreator.Snapshot;
using SkillCreator.World.Materials;

public enum EnemyState { Idle, Chase, Attack }

public enum EnemyType
{
    Melee,   // 標準近戰：追擊+攻擊
    Ranged,  // 遠程：維持距離、發射投射物
    Patrol,  // 巡邏：固定路線，玩家接近才追
    Heavy,   // 重裝：高 HP、緩慢、重擊
}

public class Enemy : IElementalTarget, ISnapshottable, ICreature
{
    private static int _nextId = 0;

    public int        Id       { get; } = ++_nextId;
    int IElementalTarget.EntityId => Id;
    void IElementalTarget.TakeDirectDamage(float amount) => TakeDamage(amount);

    public ElementalAuraComponent Aura { get; } = new();
    public GridPos    Position { get; set; }
    public GridPos    SpawnPos { get; }
    public EnemyType  Type     { get; }
    public float      Hp       { get; set; }
    public float      MaxHp    { get; }
    public bool          IsAlive   => Hp > 0f;
    public SpawnCategory Category  { get; }
    public EnemyState    State     { get; private set; } = EnemyState.Idle;

    public bool WantsToFire { get; set; }
    public int FacingX { get; private set; } = 1;
    public int FacingZ { get; private set; } = 0;

    private float _vy     = 0f;
    private float _fractY = 0f;
    private float _moveTimer;
    private float _attackTimer;
    private float _respawnTimer;
    private int   _patrolDir = 1;

    public const float RespawnTime = 8f;

    private const float Gravity      = 30f;
    private const float MaxFallSpeed = 20f;
    private const int   PatrolRange  = 12;

    private float BaseMoveInterval => Type switch
    {
        EnemyType.Heavy  => 0.60f,
        EnemyType.Ranged => 0.45f,
        EnemyType.Patrol => 0.40f,
        _                => 0.35f,
    };

    private float MoveInterval => BaseMoveInterval * (1f + Aura.SpeedPenalty);

    private float AttackInterval => Type switch
    {
        EnemyType.Heavy  => 2.5f,
        EnemyType.Ranged => 2.2f,
        _                => 1.8f,
    };

    private float AttackDamage => Type switch
    {
        EnemyType.Heavy  => 25f,
        EnemyType.Ranged => 0f,
        _                => 8f,
    };

    private int AttackRange => Type switch
    {
        EnemyType.Heavy  => 3,
        EnemyType.Ranged => 12,
        _                => 2,
    };

    private int DetectRange => Type switch
    {
        EnemyType.Ranged => 30,
        EnemyType.Patrol => 15,
        _                => 25,
    };

    public Enemy(GridPos pos, EnemyType type = EnemyType.Melee, float maxHp = -1f,
                 SpawnCategory category = SpawnCategory.Common)
    {
        Position = pos;
        SpawnPos = pos;
        Type     = type;
        Category = category;
        MaxHp    = maxHp > 0f ? maxHp : type switch
        {
            EnemyType.Heavy  => 150f,
            EnemyType.Ranged => 35f,
            EnemyType.Patrol => 45f,
            _                => 50f,
        };
        Hp = MaxHp;
    }

    /// <summary>靜默清除（不給 XP、不進倒計時）。供 MobSpawnController 消除超範圍怪物使用。</summary>
    public void ForceDespawn() => Hp = 0f;

    public void StartRespawn() => _respawnTimer = RespawnTime;

    public bool TickRespawn(float delta)
    {
        _respawnTimer -= delta;
        return _respawnTimer <= 0f;
    }

    public void Respawn()
    {
        Position      = SpawnPos;
        Hp            = MaxHp;
        State         = EnemyState.Idle;
        WantsToFire   = false;
        _vy        = 0f;
        _fractY    = 0f;
        _moveTimer = 0f;
        _attackTimer  = 0f;
        _respawnTimer = 0f;
        _patrolDir    = 1;
        Aura.Reset();
    }

    public void ForceRevive(GridPos position, float hp)
    {
        Position      = position;
        Hp            = Math.Clamp(hp, 1f, MaxHp);
        State         = EnemyState.Idle;
        WantsToFire   = false;
        _respawnTimer = 0f;
        Aura.Reset();
    }

    public EntitySnapshot TakeSnapshot() => new(
        EntityId: Id,
        Position: Position,
        Hp:       Hp,
        Mp:       0f,
        WasAlive: IsAlive,
        Aura:     Aura.TakeSnapshot(),
        CharState: null,
        CharStats: null
    );

    public void RestoreFromSnapshot(EntitySnapshot snap)
    {
        if (!snap.WasAlive) return;

        if (!IsAlive)
            ForceRevive(snap.Position, snap.Hp);
        else
        {
            Position = snap.Position;
            Hp       = snap.Hp;
        }
        _vy     = 0f;
        _fractY = 0f;
        Aura.RestoreFromSnapshot(snap.Aura);
    }

    public void Update(TileWorld3D world, PlayerController player, float delta)
    {
        Aura.Process(delta, this);
        ApplyGravity(world, delta);
        WantsToFire = false;

        int dx   = Math.Sign(player.Position.X - Position.X);
        int dz   = Math.Sign(player.Position.Z - Position.Z);
        int dist = Math.Abs(player.Position.X - Position.X)
                 + Math.Abs(player.Position.Y - Position.Y)
                 + Math.Abs(player.Position.Z - Position.Z);

        if (dx != 0) FacingX = dx;
        FacingZ = dz;

        switch (Type)
        {
            case EnemyType.Melee:  UpdateMelee(world, player, delta, dist, dx, dz);  break;
            case EnemyType.Ranged: UpdateRanged(world, player, delta, dist, dx, dz); break;
            case EnemyType.Patrol: UpdatePatrol(world, player, delta, dist, dx, dz); break;
            case EnemyType.Heavy:  UpdateHeavy(world, player, delta, dist, dx, dz);  break;
        }
    }

    private void UpdateMelee(TileWorld3D world, PlayerController player,
        float delta, int dist, int dx, int dz)
    {
        State = dist <= AttackRange ? EnemyState.Attack
              : dist <= DetectRange ? EnemyState.Chase
              :                       EnemyState.Idle;

        if (State == EnemyState.Chase)
        {
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                if (!Aura.IsImmobilized) TryMoveXZ(world, dx, dz);
            }
        }
        else if (State == EnemyState.Attack)
        {
            _attackTimer -= delta;
            if (_attackTimer <= 0f)
            {
                _attackTimer = AttackInterval;
                player.TakeDamage(AttackDamage);
            }
        }
    }

    private const int RangedPreferredDist = 8;

    private void UpdateRanged(TileWorld3D world, PlayerController player,
        float delta, int dist, int dx, int dz)
    {
        if (dist > DetectRange) { State = EnemyState.Idle; return; }

        if (dist < RangedPreferredDist)
        {
            State = EnemyState.Chase;
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                TryMoveXZ(world, -dx, -dz);
            }
        }
        else if (dist <= AttackRange)
        {
            State = EnemyState.Attack;
            _attackTimer -= delta;
            if (_attackTimer <= 0f)
            {
                _attackTimer = AttackInterval;
                WantsToFire = true;
            }
        }
        else
        {
            State = EnemyState.Chase;
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                if (!Aura.IsImmobilized) TryMoveXZ(world, dx, dz);
            }
        }
    }

    private void UpdatePatrol(TileWorld3D world, PlayerController player,
        float delta, int dist, int dx, int dz)
    {
        if (dist <= AttackRange)
        {
            State = EnemyState.Attack;
            _attackTimer -= delta;
            if (_attackTimer <= 0f)
            {
                _attackTimer = AttackInterval;
                player.TakeDamage(AttackDamage);
            }
        }
        else if (dist <= DetectRange)
        {
            State = EnemyState.Chase;
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                if (!Aura.IsImmobilized) TryMoveXZ(world, dx, dz);
            }
        }
        else
        {
            // 巡邏：沿 X 軸在出生點 ±PatrolRange 間來回
            State = EnemyState.Idle;
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                if (!Aura.IsImmobilized)
                {
                    var next = new GridPos(Position.X + _patrolDir, Position.Y, Position.Z);
                    int bodyH = Type == EnemyType.Heavy ? 2 : 1;
                    bool pathClear = true;
                    for (int dy = 0; dy < bodyH && pathClear; dy++)
                        pathClear = world.GetTile(next.X, next.Y - dy, next.Z) == MaterialType.Air;
                    if (pathClear)
                    {
                        Position = next;
                        if (Math.Abs(Position.X - SpawnPos.X) >= PatrolRange)
                            _patrolDir = -_patrolDir;
                    }
                    else
                    {
                        _patrolDir = -_patrolDir;
                    }
                }
            }
        }
    }

    private void UpdateHeavy(TileWorld3D world, PlayerController player,
        float delta, int dist, int dx, int dz)
    {
        State = dist <= AttackRange ? EnemyState.Attack
              : dist <= DetectRange ? EnemyState.Chase
              :                       EnemyState.Idle;

        if (State == EnemyState.Chase)
        {
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                if (!Aura.IsImmobilized) TryMoveXZ(world, dx, dz);
            }
        }
        else if (State == EnemyState.Attack)
        {
            _attackTimer -= delta;
            if (_attackTimer <= 0f)
            {
                _attackTimer = AttackInterval;
                player.TakeDamage(AttackDamage);
            }
        }
    }

    // 嘗試向 (dx, dz) 移動：優先對角，次 X，次 Z；對 1-tile 台階補爬台階邏輯
    private void TryMoveXZ(TileWorld3D world, int dx, int dz)
    {
        if (dx == 0 && dz == 0) return;

        if (dx != 0 && dz != 0)
        {
            var diag = new GridPos(Position.X + dx, Position.Y, Position.Z + dz);
            if (world.GetTile(diag.X, diag.Y, diag.Z) == MaterialType.Air)
            { Position = diag; return; }
        }
        if (dx != 0)
        {
            var nx = new GridPos(Position.X + dx, Position.Y, Position.Z);
            if (world.GetTile(nx.X, nx.Y, nx.Z) == MaterialType.Air)
            { Position = nx; return; }
            // 嘗試爬上 1-tile 台階（Y-1 = 往上一格）
            if (Position.Y > 0)
            {
                var stepUp = new GridPos(Position.X + dx, Position.Y - 1, Position.Z);
                if (world.GetTile(stepUp.X, stepUp.Y, stepUp.Z) == MaterialType.Air
                 && world.GetTile(Position.X, Position.Y - 1, Position.Z) == MaterialType.Air)
                { Position = stepUp; return; }
            }
        }
        if (dz != 0)
        {
            var nz = new GridPos(Position.X, Position.Y, Position.Z + dz);
            if (world.GetTile(nz.X, nz.Y, nz.Z) == MaterialType.Air)
            { Position = nz; return; }
            if (Position.Y > 0)
            {
                var stepUp = new GridPos(Position.X, Position.Y - 1, Position.Z + dz);
                if (world.GetTile(stepUp.X, stepUp.Y, stepUp.Z) == MaterialType.Air
                 && world.GetTile(Position.X, Position.Y - 1, Position.Z) == MaterialType.Air)
                { Position = stepUp; }
            }
        }
    }

    private void ApplyGravity(TileWorld3D world, float delta)
    {
        _vy     = Math.Min(_vy + Gravity * delta, MaxFallSpeed);
        _fractY += _vy * delta;
        int w = Type == EnemyType.Heavy ? 2 : 1;
        while (_fractY >= 1f)
        {
            // 墜落至世界邊界 → 強制消除（防止在未生成 chunk 中無限墜落）
            if (Position.Y + 1 >= world.Height) { ForceDespawn(); return; }
            bool blocked = false;
            for (int bx = 0; bx < w && !blocked; bx++)
                blocked = world.GetTile(Position.X + bx, Position.Y + 1, Position.Z) != MaterialType.Air;
            if (blocked)
            { _vy = 0f; _fractY = 0f; return; }
            Position = new GridPos(Position.X, Position.Y + 1, Position.Z);
            _fractY -= 1f;
        }
        if (!IsAlive || Position.Y + 1 >= world.Height) return;
        if (_vy > 0f)
        {
            bool grounded = false;
            for (int bx = 0; bx < w && !grounded; bx++)
                grounded = world.GetTile(Position.X + bx, Position.Y + 1, Position.Z) != MaterialType.Air;
            if (grounded) { _vy = 0f; _fractY = 0f; }
        }
    }

    public float XpReward => Type switch
    {
        EnemyType.Ranged => 20f,
        EnemyType.Patrol => 15f,
        EnemyType.Heavy  => 40f,
        _                => 10f,
    };

    public void TakeDamage(float amount)
    {
        float modified = amount * (1f + Aura.DefensePenalty) * (1f + Aura.DamageTakenBonus);
        var result = ActionBus.Dispatch(new EntityDamageAction(Id, modified));
        if (result == null) return;
        float finalDmg = result is EntityDamageAction eda ? eda.Amount : modified;
        Hp = Math.Max(0f, Hp - finalDmg);
    }
}
