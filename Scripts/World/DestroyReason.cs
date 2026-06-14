namespace SkillCreator.World;

public enum DestroyReason
{
    Mining,       // 形狀中心格：進度條滿後破壞，觸發掉落事件
    ShapeMining,  // 形狀其餘 N-1 格：靜默破壞，不觸發 per-tile 掉落
    Explosion,
    Slash,
    Crush,
    // TODO: R-6 DestroyReason.Collapse — 結構崩塌（懸空 tile-group FloodFill 判斷）
}
