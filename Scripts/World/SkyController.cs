namespace SkillCreator.World;

using Godot;

/// <summary>
/// 天空背景控制器。
/// 在世界最後方（Z = WorldD * TileSize + 偏移）放置一個全世界寬高的 QuadMesh，
/// 使用 spatial shader 繪製垂直漸層 + 程序雲層。
/// 深度緩衝確保實心方塊正確遮擋天空。
/// 藉由修改 Config 屬性（天氣、時間、地下深度）即可在執行時改變外觀。
/// </summary>
public partial class SkyController : Node3D
{
    public SkyConfig Config { get; } = new();

    private ShaderMaterial _mat   = null!;
    private float          _cloudTime;

    public void Initialize()
    {
        float T    = TileWorldConstants.TileSize;
        float worldW     = WorldScale.WorldW * T;
        float worldH     = WorldScale.WorldH * T;
        float bgZ        = WorldScale.WorldD * T + T * 2f;  // 恰好在世界背面之後

        var shader = new Godot.Shader { Code = ShaderSrc };
        _mat = new ShaderMaterial { Shader = shader };

        // QuadMesh 預設朝 +Z；需要翻轉讓面向相機（-Z）
        var mesh = new QuadMesh
        {
            Size             = new Vector2(worldW * 2f, worldH * 2f),
            CenterOffset     = Vector3.Zero,
            FlipFaces        = true,
        };

        var mi = new MeshInstance3D
        {
            Mesh             = mesh,
            MaterialOverride = _mat,
            // 置中在世界 XY，靠後 Z
            Position         = new Vector3(worldW * 0.5f, worldH * 0.5f, bgZ),
        };
        AddChild(mi);

        PushConfig();
    }

    public override void _Process(double delta)
    {
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

    // ── Shader ────────────────────────────────────────────────────────────────
    // UV.y = 0 → 四邊形頂部（= 世界頂部）, UV.y = 1 → 底部（= 地底）
    // 垂直漸層：天空藍（頂部）→ 淡藍白（地平線）
    // 雲層：FBM 噪音，只出現在 UV.y < 0.5 的上半段，隨時間橫向捲動
    private const string ShaderSrc = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque;

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
    return mix(mix(hash21(i), hash21(i + vec2(1,0)), f.x),
               mix(hash21(i + vec2(0,1)), hash21(i + vec2(1,1)), f.x), f.y);
}
float fbm(vec2 p) {
    return vnoise(p) * 0.5
         + vnoise(p * 2.1 + 1.73) * 0.25
         + vnoise(p * 4.3 + 3.17) * 0.125;
}

void fragment() {
    // 垂直天空漸層（UV.y 0 = 頂部天空藍，1 = 底部地平線淡藍）
    float t = clamp(1.0 - UV.y, 0.0, 1.0);
    t = t * t;
    vec3 sky = mix(sky_horizon.rgb, sky_top.rgb, t);

    // 雲層（只在上半段天空）
    vec2 uv = UV * vec2(6.0, 3.0) + vec2(cloud_time, 0.0);
    float cloud = fbm(uv);
    float thresh = 1.0 - cloud_coverage;
    cloud = smoothstep(thresh - 0.12, thresh + 0.12, cloud);
    float cloud_mask = smoothstep(0.15, 0.55, 1.0 - UV.y);  // 靠近地平線漸淡
    cloud *= cloud_mask;

    COLOR = vec4(mix(sky, cloud_color.rgb, cloud * cloud_color.a), 1.0);
}
";
}
