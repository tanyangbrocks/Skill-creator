namespace SkillCreator.World;

public enum DestroyReason
{
    Mining,
    Explosion,
    Slash,
    Crush,
    // TODO: R-6 DestroyReason.Collapse — 結構崩塌（懸空 tile-group FloodFill 判斷）
}
