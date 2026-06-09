namespace SkillCreator.World;

using SkillCreator.World.Materials;

public enum EnemyState { Idle, Chase, Attack }

public class Enemy
{
    public GridPos    Position { get; set; }
    public GridPos    SpawnPos { get; }       // 原始生成位置，重生時回到這裡
    public float      Hp       { get; set; }
    public float      MaxHp    { get; }
    public bool       IsAlive  => Hp > 0f;
    public EnemyState State    { get; private set; } = EnemyState.Idle;

    private float _gravityTimer  = 0f;
    private float _moveTimer     = 0f;
    private float _attackTimer   = 0f;
    private float _respawnTimer  = 0f;

    public const float RespawnTime = 8f;   // 死亡後幾秒重生

    private const float GravityInterval = 0.25f;
    private const float MoveInterval    = 0.35f;
    private const float AttackInterval  = 1.8f;
    private const float AttackDamage    = 8f;
    private const int   DetectRange     = 25;
    private const int   AttackRange     = 2;

    public Enemy(GridPos pos, float maxHp = 50f)
    {
        Position = pos;
        SpawnPos = pos;
        MaxHp    = maxHp;
        Hp       = maxHp;
    }

    // 開始重生倒數（由 EnemyManager 在偵測到死亡後呼叫）
    public void StartRespawn() => _respawnTimer = RespawnTime;

    // 每幀倒數，倒數完回傳 true
    public bool TickRespawn(float delta)
    {
        _respawnTimer -= delta;
        return _respawnTimer <= 0f;
    }

    // 重置到出生點
    public void Respawn()
    {
        Position      = SpawnPos;
        Hp            = MaxHp;
        State         = EnemyState.Idle;
        _gravityTimer = 0f;
        _moveTimer    = 0f;
        _attackTimer  = 0f;
        _respawnTimer = 0f;
    }

    public void Update(TileWorld world, PlayerController player, float delta)
    {
        ApplyGravity(world, delta);
        UpdateAI(world, player, delta);
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

    private void UpdateAI(TileWorld world, PlayerController player, float delta)
    {
        int dist = Math.Abs(Position.X - player.Position.X)
                 + Math.Abs(Position.Y - player.Position.Y);

        State = dist <= AttackRange ? EnemyState.Attack
              : dist <= DetectRange ? EnemyState.Chase
              :                       EnemyState.Idle;

        switch (State)
        {
            case EnemyState.Chase:
                _moveTimer -= delta;
                if (_moveTimer <= 0f)
                {
                    _moveTimer = MoveInterval;
                    int dx   = Math.Sign(player.Position.X - Position.X);
                    var next = new GridPos(Position.X + dx, Position.Y);
                    if (dx != 0 && world.TypeAt(next.X, next.Y) == MaterialType.Air)
                        Position = next;
                }
                break;

            case EnemyState.Attack:
                _attackTimer -= delta;
                if (_attackTimer <= 0f)
                {
                    _attackTimer = AttackInterval;
                    player.TakeDamage(AttackDamage);
                }
                break;
        }
    }

    public void TakeDamage(float amount) => Hp = Math.Max(0f, Hp - amount);
}
