namespace SkillCreator.World;

using Godot;

/// <summary>
/// 天空背景控制器。
/// 使用 WorldEnvironment + sky 著色器，確保在所有相機模式（正交側卷軸、透視第一人稱等）
/// 都能正確渲染在所有幾何體之後。
/// 著色器使用螢幕空間 SCREEN_UV.y 做垂直漸層，與相機投影模式無關。
/// 藉由修改 Config 屬性（天氣、時間、地下深度）即可在執行時改變外觀。
/// </summary>
public partial class SkyController : Node3D
{
    public SkyConfig Config { get; } = new();

    private ShaderMaterial _mat   = null!;
    private float          _cloudTime;

    public void Initialize()
    {
        var shader = new Godot.Shader { Code = SkyShaderSrc };
        _mat = new ShaderMaterial { Shader = shader };

        var sky = new Sky { SkyMaterial = _mat };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky            = sky,
            SkyRotation    = Vector3.Zero,
            // 關閉 tone mapping，避免壓縮天空顏色
            TonemapMode     = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            // 關閉霧氣（若需要霧效在天氣系統中另行添加）
            FogEnabled = false,
        };

        var we = new WorldEnvironment { Environment = env };
        AddChild(we);

        PushConfig();
    }

    public override void _Process(double delta)
    {
        if (_mat == null) return;
        _cloudTime += Config.CloudSpeed * (float)delta;
        PushConfig();
    }

    private void PushConfig()
    {
        _mat.SetShaderParameter("sky_top",        Config.TopColor     * Config.Brightness);
        _mat.SetShaderParameter("sky_horizon",    Config.HorizonColor * Config.Brightness);
        _mat.SetShaderParameter("cloud_color",    Config.CloudColor);
        _mat.SetShaderParameter("cloud_coverage", Config.CloudCoverage);
        _mat.SetShaderParameter("cloud_time",     _cloudTime);
    }

    // ── Sky Shader ────────────────────────────────────────────────────────────
    // 使用 shader_type sky（Godot 4.3+ 支援 SCREEN_UV / FRAGCOORD）
    // SCREEN_UV.y = 0 為螢幕頂部, 1 為底部
    // 垂直漸層：天空藍（頂部）→ 淡藍白（地平線/底部）
    // 雲層：FBM 噪音，僅在上半螢幕顯示，隨時間橫向捲動
    private const string SkyShaderSrc = @"
shader_type sky;

uniform vec4 sky_top     : source_color = vec4(0.28, 0.50, 0.90, 1.0);
uniform vec4 sky_horizon : source_color = vec4(0.72, 0.86, 1.00, 1.0);
uniform vec4 cloud_color : source_color = vec4(1.0, 1.0, 1.0, 0.80);
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.32;
uniform float cloud_time = 0.0;

float hash21(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i),           hash21(i + vec2(1.0, 0.0)), f.x),
               mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbm(vec2 p) {
    return vnoise(p) * 0.5
         + vnoise(p * 2.1 + 1.73) * 0.25
         + vnoise(p * 4.3 + 3.17) * 0.125;
}

void sky() {
    // 螢幕空間垂直漸層（SCREEN_UV.y: 0=頂, 1=底）
    float t = clamp(1.0 - SCREEN_UV.y, 0.0, 1.0);
    t = t * t;
    vec3 sky = mix(sky_horizon.rgb, sky_top.rgb, t);

    // 雲層（上半螢幕）
    vec2 uv = SCREEN_UV * vec2(6.0, 3.0) + vec2(cloud_time, 0.0);
    float cloud = fbm(uv);
    float thresh = 1.0 - cloud_coverage;
    cloud = smoothstep(thresh - 0.12, thresh + 0.12, cloud);
    float cloud_mask = smoothstep(0.15, 0.55, 1.0 - SCREEN_UV.y);
    cloud *= cloud_mask;

    COLOR = mix(sky, cloud_color.rgb, cloud * cloud_color.a);
}
";
}
