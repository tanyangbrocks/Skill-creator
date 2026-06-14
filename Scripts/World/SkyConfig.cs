namespace SkillCreator.World;
using Godot;

/// <summary>
/// 天空外觀參數。由遊戲機制（時間、天氣、深度、狀態）動態修改。
/// SkyController 每幀讀取此物件並同步至 shader uniform。
/// </summary>
public sealed class SkyConfig
{
    // 天空漸層（上方 → 地平線）
    public Color TopColor     = new Color(0.28f, 0.50f, 0.90f);
    public Color HorizonColor = new Color(0.72f, 0.86f, 1.00f);

    // 雲層
    public float CloudCoverage = 0.32f;   // 0=晴天 1=烏雲密布
    public float CloudSpeed    = 0.010f;  // UV/秒，控制雲移速
    public Color CloudColor    = new Color(1.0f, 1.0f, 1.0f, 0.80f);

    // 亮度（影響整體天空明暗，可由地下深度或時間縮放）
    public float Brightness = 1.0f;
}
