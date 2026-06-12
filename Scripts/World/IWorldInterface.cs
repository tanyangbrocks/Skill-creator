namespace SkillCreator.World;

using SkillCreator.World.Materials;

// 能力系統與世界之間的介面合約（§16）
public interface IWorldInterface
{
    // 查詢
    WorldEntity? GetEntityAt(GridPos position);
    MaterialType GetMaterialAt(GridPos position);
    List<WorldEntity> GetEntitiesNear(GridPos position, float radius);
    object? GetEntityProperty(WorldEntity entity, string property);

    // 指令
    void DestroyTile(GridPos position, DestroyReason reason = DestroyReason.Mining);
    void ApplyForce(WorldEntity entity, float dx, float dy);
    void SpawnEffect(string type, GridPos position, Dictionary<string, object?> parameters);
    void SetEntityProperty(WorldEntity entity, string property, object? value);
    WorldEntity? CreateEntity(string type, GridPos position, Dictionary<string, object?> parameters);

    // 事件通知
    event Action<WorldEntity, WorldEntity, float>? OnEntityHit;
    event Action<GridPos, MaterialType, DestroyReason>? OnTileDestroyed;
    event Action<WorldEntity>? OnEntityDied;
    event Action<string, object?>? OnPlayerAction;
}
