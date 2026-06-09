namespace SkillCreator.Snapshot;

/// <summary>S-1：實體可快照介面。</summary>
public interface ISnapshottable
{
    EntitySnapshot TakeSnapshot();
    void RestoreFromSnapshot(EntitySnapshot snapshot);
}
