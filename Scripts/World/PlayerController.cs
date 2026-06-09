namespace SkillCreator.World;

using SkillCreator.World.Items;
using SkillCreator.World.Materials;

public class PlayerController
{
    public GridPos Position     { get; set; }
    // Facing 只追蹤水平方向，確保投射物永遠往左/右打
    public GridPos Facing       { get; private set; } = new GridPos(1, 0);
    // 滑鼠對應的世界格座標（由 Main._Process 每幀更新）
    public GridPos MouseGridPos { get; set; }

    public Inventory Inventory { get; } = new Inventory();
    public const float MiningRange = 5f; // 採掘/放置最大距離（格數）

    public float Hp { get; set; }
    public const float MaxHp = 100f;

    public float Mp { get; set; }
    public const float MaxMp    = 100f;
    private const float MpRegen = 8f;

    private float _moveCooldown = 0f;
    private float _castCooldown = 0f;
    private const float MoveInterval = 0.12f;

    // 重力與跳躍
    private float _gravityTimer  = 0f;
    private float _jumpTimer     = 0f;
    private int   _jumpRemaining = 0;
    private const float GravityInterval = 0.2f;
    private const float JumpInterval    = 0.09f;
    private const int   JumpTiles       = 7;

    public bool IsAlive  => Hp > 0f;
    public bool CanMove  => _moveCooldown <= 0f;
    public bool CanCast  => _castCooldown <= 0f;

    public void TakeDamage(float amount) => Hp = Math.Max(0f, Hp - amount);

    public PlayerController(GridPos startPos)
    {
        Position = startPos;
        Hp = MaxHp;
        Mp = MaxMp;
    }

    public void Tick(float delta)
    {
        if (_moveCooldown > 0f) _moveCooldown -= delta;
        if (_castCooldown > 0f) _castCooldown -= delta;
        Mp = MathF.Min(MaxMp, Mp + MpRegen * delta);
    }

    // 水平移動（A/D），同時更新朝向
    public bool TryMove(TileWorld world, int dx, int dy)
    {
        if (!CanMove) return false;
        var next = new GridPos(Position.X + dx, Position.Y + dy);
        if (world.TypeAt(next.X, next.Y) != MaterialType.Air) return false;

        Position = next;
        if (dx != 0) Facing = new GridPos(Math.Sign(dx), 0); // 只有水平移動才更新 Facing
        _moveCooldown = MoveInterval;
        return true;
    }

    // 重力 + 跳躍物理（每幀由 Main._Process 呼叫）
    public void ApplyPhysics(TileWorld world, float delta)
    {
        if (_jumpRemaining > 0)
        {
            _jumpTimer -= delta;
            if (_jumpTimer <= 0f)
            {
                _jumpTimer = JumpInterval;
                var above = new GridPos(Position.X, Position.Y - 1);
                if (world.TypeAt(above.X, above.Y) == MaterialType.Air)
                    Position = above;
                _jumpRemaining--;
            }
        }
        else
        {
            _gravityTimer -= delta;
            if (_gravityTimer <= 0f)
            {
                _gravityTimer = GravityInterval;
                var below = new GridPos(Position.X, Position.Y + 1);
                if (world.TypeAt(below.X, below.Y) == MaterialType.Air)
                    Position = below;
            }
        }
    }

    public bool IsOnGround(TileWorld world)
    {
        var below = new GridPos(Position.X, Position.Y + 1);
        return world.TypeAt(below.X, below.Y) != MaterialType.Air;
    }

    public void StartJump()
    {
        _jumpRemaining = JumpTiles;
        _jumpTimer     = 0f;
        _gravityTimer  = 0f;
    }

    public void SetCastCooldown(float seconds) => _castCooldown = seconds;

    // ── 採掘 ──────────────────────────────────────────────────────

    public GridPos? MiningTarget   { get; private set; }
    public float    MiningProgress { get; private set; }

    public void CancelMining()
    {
        MiningTarget   = null;
        MiningProgress = 0f;
    }

    // 回傳 true 代表本幀破壞了方塊
    public bool TickMining(TileWorld world, GridPos target, float delta)
    {
        var mat  = world.TypeAt(target.X, target.Y);
        var data = MaterialRegistry.Get(mat);

        if (!data.IsMineable || data.RequiredToolTier > Inventory.ActiveToolTier)
        {
            CancelMining();
            return false;
        }

        // 換了目標 → 重置進度
        if (MiningTarget != target)
        {
            MiningTarget   = target;
            MiningProgress = 0f;
        }

        // 累加採掘進度（以 60fps 為基準，乘上工具速度倍率）
        MiningProgress += Inventory.ActiveMiningSpeedMult * delta * 60f;

        if (MiningProgress >= data.Hardness)
        {
            world.DestroyTile(target);
            CancelMining();
            return true;
        }
        return false;
    }
}
