namespace SkillCreator.Snapshot;

using SkillCreator.AbilitySystem;
using SkillCreator.World;

/// <summary>
/// S-8：靜態快照管理器。維護 Anchor 棧，支援多層 Rollback。
/// Main._Ready() 呼叫 Clear() 重置跨局狀態。
/// </summary>
public static class SnapshotManager
{
    // ── 內部快照容器 ──────────────────────────────────────────────────────

    private sealed record WorldSnapshot(
        float                AnchorTimestamp,   // GameClock.TotalTicks at anchor time
        List<EntitySnapshot> Entities,
        TileWorldSnapshot    Tiles
    );

    private static readonly Stack<WorldSnapshot> _stack = new();

    /// <summary>目前棧深度（供積木 UI debug 顯示）。</summary>
    public static int StackDepth => _stack.Count;

    // ── 主要 API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Anchor：擷取目前所有實體狀態 + 圓形 Tile 區域，壓入 Anchor 棧。
    /// center 為施法者當前格，radius 來自積木填入值。
    /// </summary>
    public static void TakeSnapshot(
        GridPos center, int radius,
        PlayerController player, EnemyManager? enemies, TileWorld3D world)
    {
        var entities = new List<EntitySnapshot> { player.TakeSnapshot() };
        if (enemies != null)
            foreach (var e in enemies.Enemies)
                entities.Add(e.TakeSnapshot());

        _stack.Push(new WorldSnapshot(
            GameClock.TotalTicks,
            entities,
            world.SnapshotRegion(center, radius)
        ));
    }

    /// <summary>
    /// Rollback：彈出棧頂快照，還原實體 + Tile 狀態，並清除錨點後提交的技能整構（退還 MP）。
    /// 棧為空時靜默返回。
    /// </summary>
    public static void ApplyLatest(
        PlayerController player, EnemyManager? enemies,
        TileWorld3D world, SpellRunner runner)
    {
        if (_stack.Count == 0) return;

        var snap = _stack.Pop();

        // 還原實體（玩家 + 所有敵人）
        foreach (var es in snap.Entities)
        {
            if (es.EntityId == EntitySnapshot.PlayerId)
            {
                player.RestoreFromSnapshot(es);
            }
            else if (enemies != null)
            {
                var e = enemies.Enemies.FirstOrDefault(x => x.Id == es.EntityId);
                e?.RestoreFromSnapshot(es);
            }
        }

        // 還原 Tile 區域
        world.RestoreRegion(snap.Tiles);

        // 清除錨點後提交的技能整構並退還 MP
        runner.PruneAfter(snap.AnchorTimestamp);
    }

    /// <summary>清除所有 Anchor（Main._Ready() / 場景重啟時呼叫）。</summary>
    public static void Clear() => _stack.Clear();
}
