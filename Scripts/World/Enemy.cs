namespace SkillCreator.World;

using SkillCreator.World.Materials;

public enum EnemyState { Idle, Chase, Attack }

public enum EnemyType
{
    Melee,   // 標準近戰：追擊+攻擊
    Ranged,  // 遠程：維持距離、發射投射物
    Patrol,  // 巡邏：固定路線，玩家接近才追
    Heavy,   // 重裝：高 HP、緩慢、重擊
}

public class Enemy
{
    private static int _nextId = 0;

    public int        Id       { get; } = ++_nextId; // 場景內不重複的穩定 ID
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

    private float _gravityTimer;
    private float _moveTimer;
    private float _attackTimer;
    private float _respawnTimer;
    private int   _patrolDir = 1;   // Patrol 當前巡邏方向

    public const float RespawnTime = 8f;

    // ── 類型特化常數 ──────────────────────────────────────────────
    private const float GravityInterval = 0.25f;
    private const int   PatrolRange     = 12;   // Patrol 離出生點最遠距離

    private float MoveInterval => Type switch
    {
        EnemyType.Heavy  => 0.60f,
        EnemyType.Ranged => 0.45f,
        EnemyType.Patrol => 0.40f,
        _                => 0.35f,
    };

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
        _gravityTimer = 0f;
        _moveTimer    = 0f;
        _attackTimer  = 0f;
        _respawnTimer = 0f;
        _patrolDir    = 1;
    }

    // ── 每幀更新 ──────────────────────────────────────────────────

    public void Update(TileWorld world, PlayerController player, float delta)
    {
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

    private void UpdateMelee(TileWorld world, PlayerController player,
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
                TryMoveX(world, dx);
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

    private void UpdateRanged(TileWorld world, PlayerController player,
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
                TryMoveX(world, dx);
            }
        }
    }

    // ── Patrol ────────────────────────────────────────────────────

    private void UpdatePatrol(TileWorld world, PlayerController player,
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
                TryMoveX(world, dx);
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

    // ── Heavy ─────────────────────────────────────────────────────

    private void UpdateHeavy(TileWorld world, PlayerController player,
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
                TryMoveX(world, dx);
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

    private void TryMoveX(TileWorld world, int dx)
    {
        if (dx == 0) return;
        var next = new GridPos(Position.X + dx, Position.Y);
        if (world.TypeAt(next.X, next.Y) == MaterialType.Air)
            Position = next;
    }

    private void ApplyGravity(TileWorld world, float delta)
    {
        _gravityTimer -= delta;
        if (_gravityTimer > 0f) return;
        _gravityTimer = GravityInterval;
        var below = new GridPos(Position.X, Position.Y + 1);
        if (world.TypeAt(below.X, below.Y) == MaterialType.Air)
            Position = below;
    }

    public void TakeDamage(float amount) => Hp = Math.Max(0f, Hp - amount);
}
