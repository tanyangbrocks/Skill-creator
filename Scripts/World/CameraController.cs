namespace SkillCreator.World;

using Godot;

/// <summary>
/// Phase 2-A：四視角鏡頭控制器。
/// 掛在玩家附近的 Node3D 上；Tab 鍵循環切換模式。
/// 世界座標：Y+ = 向下（CA 重力方向），因此世界「上方」= -Y。
/// </summary>
public partial class CameraController : Node3D
{
    public enum CameraMode
    {
        ThirdPerson,   // 第三人稱（SpringArm 風格，可水平旋轉）
        FirstPerson,   // 第一人稱（眼睛高度，隨 _yaw 轉向）
        Isometric,     // 俯視 45°/等角（正交投影）
        SideScroll2D,  // 2D 側捲（正交投影，沿 -Z 看，Z=0 鎖定玩家）
    }

    // ── 可調參數 ─────────────────────────────────────────────────────────────
    [Export] public float TpsArmLength  = 12f;   // 第三人稱臂長
    [Export] public float TpsPitchDeg   = 30f;   // 第三人稱俯仰角（0=水平，90=正上方）
    [Export] public float FpEyeY        = -1.5f; // 第一人稱眼睛 Y 偏移（Y+=向下，故眼睛在 -Y）
    [Export] public float IsoArmLength  = 25f;   // 等角臂長
    [Export] public float IsoPitchDeg   = 45f;   // 等角俯仰角
    [Export] public float IsoYawDeg     = 45f;   // 等角水平旋轉角
    [Export] public float PerspFov      = 70f;   // 透視視角（度）
    [Export] public float OrthoSize     = 30f;   // 正交投影尺寸（tiles）
    [Export] public float SideDist      = 40f;   // 2D 視角相機距離（沿 +Z 偏移）
    [Export] public float MouseSens     = 0.25f; // 滑鼠靈敏度（TPS/FPS）

    // ── 狀態 ─────────────────────────────────────────────────────────────────
    public CameraMode Mode { get; private set; } = CameraMode.ThirdPerson;

    /// <summary>設定此節點每幀跟隨的目標（玩家 Node3D）。</summary>
    public Node3D? Target { get; set; }

    private Camera3D _cam = null!;
    private float    _yaw; // 水平旋轉角（度），TPS / FPS 共用

    // ── 生命週期 ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _cam = new Camera3D { Fov = PerspFov };
        AddChild(_cam);
        ApplyProjection();
    }

    public override void _Process(double _)
    {
        if (Target != null)
            GlobalPosition = Target.GlobalPosition;
        UpdateCameraTransform();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        // Tab：循環切換視角
        if (ev is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Tab)
        {
            CycleMode();
            GetViewport().SetInputAsHandled();
            return;
        }

        // 滑鼠移動：TPS / FPS 水平旋轉（需要 Captured 模式）
        if (ev is InputEventMouseMotion mm &&
            Input.MouseMode == Input.MouseModeEnum.Captured &&
            Mode is CameraMode.ThirdPerson or CameraMode.FirstPerson)
        {
            _yaw -= mm.Relative.X * MouseSens;
        }
    }

    // ── 公開方法 ─────────────────────────────────────────────────────────────

    public void CycleMode()
    {
        Mode = (CameraMode)(((int)Mode + 1) % 4);
        ApplyProjection();
        GD.Print($"[Camera] 切換到：{ModeLabel()}");
    }

    public void SetMode(CameraMode m)
    {
        Mode = m;
        ApplyProjection();
    }

    // ── 內部 ─────────────────────────────────────────────────────────────────

    private void ApplyProjection()
    {
        bool persp = Mode is CameraMode.ThirdPerson or CameraMode.FirstPerson;
        _cam.Projection = persp
            ? Camera3D.ProjectionType.Perspective
            : Camera3D.ProjectionType.Orthogonal;
        _cam.Fov  = PerspFov;
        _cam.Size = OrthoSize;
    }

    // 「世界上方」向量：Y+ 向下，故 -Y 為上
    private static readonly Vector3 WorldUp = new(0f, -1f, 0f);

    private void UpdateCameraTransform()
    {
        switch (Mode)
        {
            case CameraMode.ThirdPerson:
            {
                float y = Mathf.DegToRad(_yaw);
                float p = Mathf.DegToRad(TpsPitchDeg);
                // 相機在玩家的「上方後方」：Y- 方向（= 世界上方）
                var offset = new Vector3(
                    MathF.Sin(y) * TpsArmLength * MathF.Cos(p),
                    -MathF.Sin(p) * TpsArmLength,              // Y- = 上方
                    MathF.Cos(y) * TpsArmLength * MathF.Cos(p));
                _cam.Position = offset;
                _cam.LookAt(GlobalPosition, WorldUp);
                break;
            }

            case CameraMode.FirstPerson:
            {
                _cam.Position = new Vector3(0f, FpEyeY, 0f);
                // 面向 _yaw 方向（繞 Y 軸旋轉，Y+ 向下）
                _cam.Rotation = new Vector3(0f, Mathf.DegToRad(_yaw), 0f);
                break;
            }

            case CameraMode.Isometric:
            {
                float p = Mathf.DegToRad(IsoPitchDeg);
                float y = Mathf.DegToRad(IsoYawDeg);
                var offset = new Vector3(
                    MathF.Sin(y) * IsoArmLength * MathF.Cos(p),
                    -MathF.Sin(p) * IsoArmLength,
                    MathF.Cos(y) * IsoArmLength * MathF.Cos(p));
                _cam.Position = offset;
                _cam.LookAt(GlobalPosition, WorldUp);
                break;
            }

            case CameraMode.SideScroll2D:
            {
                // 相機在 +Z 側，面向 -Z（Godot 預設朝向），XY 平面可見
                // 玩家 Z 鎖定邏輯由 PlayerController 負責（Phase 2-B 實作）
                _cam.Position = new Vector3(0f, 0f, SideDist);
                _cam.Rotation = Vector3.Zero; // 預設朝 -Z 看
                break;
            }
        }
    }

    private string ModeLabel() => Mode switch
    {
        CameraMode.ThirdPerson  => "第三人稱",
        CameraMode.FirstPerson  => "第一人稱",
        CameraMode.Isometric    => "俯視 45°",
        CameraMode.SideScroll2D => "2D 側捲",
        _                       => Mode.ToString(),
    };
}
