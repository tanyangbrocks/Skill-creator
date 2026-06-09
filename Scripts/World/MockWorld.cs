namespace SkillCreator.World;

using SkillCreator.World.Materials;

// 20×20 格子場景 Mock World（Phase 1 測試用，保留）
public class MockWorld : IWorldInterface
{
    public const int Width  = 20;
    public const int Height = 20;

    // 內部仍用簡化三態
    private enum TileState { Empty, Solid, Destructible }

    private readonly TileState[,] _grid = new TileState[Width, Height];
    private readonly List<WorldEntity> _entities = new();
    private int _nextId = 1;

    public event Action<WorldEntity, WorldEntity, float>? OnEntityHit;
    public event Action<GridPos, MaterialType>? OnTileDestroyed;
    public event Action<WorldEntity>? OnEntityDied;
    public event Action<string, object?>? OnPlayerAction;

    public MockWorld()
    {
        InitGrid();
        SpawnInitialEntities();
    }

    private void InitGrid()
    {
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            if (x == 0 || x == Width-1 || y == 0 || y == Height-1)
                _grid[x, y] = TileState.Solid;
            else if (x % 5 == 2 && y % 5 == 2)
                _grid[x, y] = TileState.Destructible;
            else
                _grid[x, y] = TileState.Empty;
        }
    }

    private void SpawnInitialEntities()
    {
        Spawn(new GridPos(10,10), 100f, "player");
        Spawn(new GridPos(5,  5),  50f, "enemy");
        Spawn(new GridPos(15, 5),  50f, "enemy");
        Spawn(new GridPos(5, 15),  50f, "enemy");
        Spawn(new GridPos(15,15),  50f, "enemy");
    }

    private WorldEntity Spawn(GridPos pos, float maxHp, string faction)
    {
        var e = new WorldEntity(_nextId++, pos, maxHp, faction);
        _entities.Add(e);
        return e;
    }

    // ── IWorldInterface ──────────────────────────────────────────

    public WorldEntity? GetEntityAt(GridPos pos) =>
        _entities.FirstOrDefault(e => e.IsAlive && e.Position == pos);

    public MaterialType GetMaterialAt(GridPos pos)
    {
        if (pos.X < 0 || pos.X >= Width || pos.Y < 0 || pos.Y >= Height)
            return MaterialType.Stone;
        return _grid[pos.X, pos.Y] switch
        {
            TileState.Solid        => MaterialType.Stone,
            TileState.Destructible => MaterialType.Wood,
            _                      => MaterialType.Air,
        };
    }

    public List<WorldEntity> GetEntitiesNear(GridPos pos, float radius) =>
        _entities.Where(e => e.IsAlive && e.Position.DistanceTo(pos) <= radius).ToList();

    public object? GetEntityProperty(WorldEntity entity, string property) => property switch
    {
        "hp"       => entity.Hp,
        "maxHp"    => entity.MaxHp,
        "faction"  => entity.Faction,
        "position" => entity.Position,
        _          => null,
    };

    public void DestroyTile(GridPos pos)
    {
        if (pos.X < 0 || pos.X >= Width || pos.Y < 0 || pos.Y >= Height) return;
        if (_grid[pos.X, pos.Y] != TileState.Destructible) return;
        _grid[pos.X, pos.Y] = TileState.Empty;
        OnTileDestroyed?.Invoke(pos, MaterialType.Wood);
    }

    public void ApplyForce(WorldEntity entity, float dx, float dy)
    {
        var np = entity.Position + new GridPos(Math.Sign((int)dx), Math.Sign((int)dy));
        if (GetMaterialAt(np) == MaterialType.Air) entity.Position = np;
    }

    public void SpawnEffect(string type, GridPos position, Dictionary<string, object?> parameters)
        => Console.WriteLine($"[MockWorld] SpawnEffect: {type} at {position}");

    public void SetEntityProperty(WorldEntity entity, string property, object? value)
    {
        if (property != "hp") return;
        float old = entity.Hp;
        entity.Hp = Math.Max(0f, Convert.ToSingle(value));
        if (entity.Hp < old) OnEntityHit?.Invoke(entity, entity, old - entity.Hp);
        if (entity.Hp <= 0f && old > 0f) OnEntityDied?.Invoke(entity);
    }

    public WorldEntity? CreateEntity(string type, GridPos pos, Dictionary<string, object?> parameters)
    {
        float maxHp = parameters.TryGetValue("maxHp", out var hp) ? Convert.ToSingle(hp) : 50f;
        string faction = parameters.TryGetValue("faction", out var f) ? f?.ToString() ?? "enemy" : "enemy";
        return Spawn(pos, maxHp, faction);
    }

    public void FirePlayerAction(string type, object? param = null) =>
        OnPlayerAction?.Invoke(type, param);
}
