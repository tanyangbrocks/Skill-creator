namespace SkillCreator.Snapshot;

using SkillCreator.World;

/// <summary>S-4：CharacterState 的不可變狀態快照（7 個生存數值）。</summary>
public sealed record CharStateSnapshot(
    float Stamina,
    float MentalEnergy,
    float Mood,
    float BodyTemperature,
    float Thirst,
    float Hunger,
    float Oxygen
)
{
    public static CharStateSnapshot From(CharacterState s) => new(
        Stamina:         s.Stamina,
        MentalEnergy:    s.MentalEnergy,
        Mood:            s.Mood,
        BodyTemperature: s.BodyTemperature,
        Thirst:          s.Thirst,
        Hunger:          s.Hunger,
        Oxygen:          s.Oxygen
    );
}
