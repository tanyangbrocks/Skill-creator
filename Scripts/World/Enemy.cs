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

public class Enemy : IElementalTarget, ISnapshottable
{
    private static int _nextId = 0;

    public int        Id       { get; } = ++_nextId; // 場景內不重複的穩定 ID
    int IElementalTarget.EntityId => Id;             // IElementalTarget 實作
    void IElementalTarget.TakeDirectDamage(float amount) => TakeDamage(amount);

    // ── 元素系統（W-3）────────────────────────────────────────────────
    public ElementalAuraComponent Aura { get; } = new();
    public GridPos    Position { get; set; }
    public GridPos    SpawnPos { get; }
    public EnemyType  Type     { get; }
    public float      Hp       { get; set; }
    public float      MaxHp    { get; }
    public bool       IsAlive  => Hp > 0f;
    public EnemyState State    { get; private set; } = EnemyState.Idle;

    // 遠程敵人：本幀是否要發射投射物（由 EnemyManager 消費）
    public bool WantsToFire { get; set; }
    // 面向（用於發射方向）
    public int FacingX { get; private set; } = 1;

    private float _vy     = 0f;  // 縱向速度（tiles/s，正向下）
    private float _fractY = 0f; // 次格累積量
    private float _moveTimer;
    private float _attackTimer;
    private float _respawnTimer;
    private int   _patrolDir = 1;   // Patrol 當前巡邏方向

    public const float RespawnTime = 8f;

    // ── 類型特化常數 ──────────────────────────────────────────────
    private const float Gravity      = 30f;
    private const float MaxFallSpeed = 20f;
    private const int   PatrolRange     = 12;   // Patrol 離出生點最遠距離

    private float BaseMoveInterval => Type switch
    {
        EnemyType.Heavy  => 0.60f,
        EnemyType.Ranged => 0.45f,
        EnemyType.Patrol => 0.40f,
        _                => 0.35f,
    };

    // 套用元素移速懲罰後的實際移動間隔（值越大越慢）
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
        EnemyType.Ranged => 0f,   // 由投射物造成傷害
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

    // ── 建構 ──────────────────────────────────────────────────────

    public Enemy(GridPos pos, EnemyType type = EnemyType.Melee, float maxHp = -1f)
    {
        Position = pos;
        SpawnPos = pos;
        Type     = type;
        MaxHp    = maxHp > 0f ? maxHp : type switch
        {
            EnemyType.Heavy  => 150f,
            EnemyType.Ranged => 35f,
            EnemyType.Patrol => 45f,
            _                => 50f,
        };
        Hp = MaxHp;
    }

    // ── 重生 ──────────────────────────────────────────────────────

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

    /// <summary>S-14：繞過重生計時器，直接強制復活至指定狀態（Rollback 用）。</summary>
    public void ForceRevive(GridPos position, float hp)
    {
        Position      = position;
        Hp            = Math.Clamp(hp, 1f, MaxHp);
        State         = EnemyState.Idle;
        WantsToFire   = false;
        _respawnTimer = 0f;
        Aura.Reset();
    }

    // ── ISnapshottable（S-7）──────────────────────────────────────

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
        if (!snap.WasAlive) return; // 快照時已死亡，不做任何還原

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

    // ── 每幀更新 ──────────────────────────────────────────────────

    public void Update(TileWorld3D world, PlayerController player, float delta)
    {
        Aura.Process(delta, this);
        ApplyGravity(world, delta);
        WantsToFire = false;

        int distX = Math.Abs(Position.X - player.Position.X);
        int dist  = distX + Math.Abs(Position.Y - player.Position.Y);
        int dx    = Math.Sign(player.Position.X - Position.X);
        if (dx != 0) FacingX = dx;

        switch (Type)
        {
            case EnemyType.Melee:  UpdateMelee(world, player, delta, dist, dx);  break;
            case EnemyType.Ranged: UpdateRanged(world, player, delta, dist, dx); break;
            case EnemyType.Patrol: UpdatePatrol(world, player, delta, dist, dx); break;
            case EnemyType.Heavy:  UpdateHeavy(world, player, delta, dist, dx);  break;
        }
    }

    // ── Melee ─────────────────────────────────────────────────────

    private void UpdateMelee(TileWorld3D world, PlayerController player,
        float delta, int dist, int dx)
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
                if (!Aura.IsImmobilized) TryMoveX(world, dx);
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

    // ── Ranged ────────────────────────────────────────────────────

    private const int RangedPreferredDist = 8;

    private void UpdateRanged(TileWorld3D world, PlayerController player,
        float delta, int dist, int dx)
    {
        if (dist > DetectRange) { State = EnemyState.Idle; return; }

        if (dist < RangedPreferredDist)
        {
            // 太近：後退
            State = EnemyState.Chase;
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                TryMoveX(world, -dx);   // 往反方向走
            }
        }
        else if (dist <= AttackRange)
        {
            // 在射程內：發射
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
            // 靠近到射程
            State = EnemyState.Chase;
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                if (!Aura.IsImmobilized) TryMoveX(world, dx);
            }
        }
    }

    // ── Patrol ────────────────────────────────────────────────────

    private void UpdatePatrol(TileWorld3D world, PlayerController player,
        float delta, int dist, int dx)
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
                if (!Aura.IsImmobilized) TryMoveX(world, dx);
            }
        }
        else
        {
            // 巡邏模式：在出生點 ±PatrolRange 間來回
            State = EnemyState.Idle;
            _moveTimer -= delta;
            if (_moveTimer <= 0f)
            {
                _moveTimer = MoveInterval;
                if (!Aura.IsImmobilized)
                {
                    var next = new GridPos(Position.X + _patrolDir, Position.Y);
                    if (world.TypeAt(next.X, next.Y) == MaterialType.Air)
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

    // ── Heavy ─────────────────────────────────────────────────────

    private void UpdateHeavy(TileWorld3D world, PlayerController player,
        float delta, int dist, int dx)
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
                if (!Aura.IsImmobilized) TryMoveX(world, dx);
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

    // ── 共用工具 ──────────────────────────────────────────────────

    private void TryMoveX(TileWorld3D world, int dx)
    {
        if (dx == 0) return;
        var next = new GridPos(Position.X + dx, Position.Y);
        if (world.TypeAt(next.X, next.Y) == MaterialType.Air)
            Position = next;
    }

    private void ApplyGravity(TileWorld3D world, float delta)
    {
        _vy     = Math.Min(_vy + Gravity * delta, MaxFallSpeed);
        _fractY += _vy * delta;
        while (_fractY >= 1f)
        {
            var below = new GridPos(Position.X, Position.Y + 1);
            if (world.TypeAt(below.X, below.Y) != MaterialType.Air)
            { _vy = 0f; _fractY = 0f; return; }
            Position = below;
            _fractY -= 1f;
        }
        if (_vy > 0f && world.TypeAt(Position.X, Position.Y + 1) != MaterialType.Air)
        { _vy = 0f; _fractY = 0f; }
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
        // ── 元素狀態效果修改（W-3：鏽化防禦懲罰 + 結凍受傷加成）────
        float modified = amount * (1f + Aura.DefensePenalty) * (1f + Aura.DamageTakenBonus);
        // ──────────────────────────────────────────────────────────────

        // ── 行動攔截鉤子（Phase 4 第三層）────────────────────────────
        var result = ActionBus.Dispatch(new EntityDamageAction(Id, modified));
        if (result == null) return; // 攔截：傷害取消
        float finalDmg = result is EntityDamageAction eda ? eda.Amount : modified;
        // ──────────────────────────────────────────────────────────────
        Hp = Math.Max(0f, Hp - finalDmg);
    }
}
