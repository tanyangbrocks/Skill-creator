namespace SkillCreator.Snapshot;

using SkillCreator.World;

/// <summary>
/// S-2：單一實體的不可變狀態快照。
/// EntityId = PlayerId（-1）代表玩家；正整數對應 Enemy.Id。
/// CharState / CharStats 僅玩家填值；敵人傳 null。
/// WasAlive：快照當下是否存活（D2 Rollback 用於判斷是否需要強制復活死亡敵人）。
/// </summary>
public sealed record EntitySnapshot(
    int                EntityId,
    GridPos            Position,
    float              Hp,
    float              Mp,
    bool               WasAlive,
    AuraSnapshot       Aura,
    CharStateSnapshot? CharState,
    CharStatsSnapshot? CharStats
)
{
    /// <summary>玩家在快照系統中的固定識別碼。</summary>
    public const int PlayerId = -1;
}
