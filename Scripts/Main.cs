using Godot;
using SkillCreator.AbilitySystem;
using SkillCreator.UI;
using SkillCreator.World;
using SkillCreator.World.Materials;

namespace SkillCreator;

public partial class Main : Node
{
    private TileWorldRenderer _world  = null!;
    private AbilityEditorUI   _editor = null!;
    private PlayerController  _player = null!;
    private Label             _mpLabel = null!;
    private bool _editorOpen = false;

    public override void _Ready()
    {
        // ── 世界渲染器 ──────────────────────────────────────────
        _world = new TileWorldRenderer();
        AddChild(_world);

        // ── 玩家 ───────────────────────────────────────────────
        _player = new PlayerController(new GridPos(100, 130));
        _world.Player = _player;

        // ── HUD（CanvasLayer，永遠疊在最上層）─────────────────
        var hud = new CanvasLayer();
        AddChild(hud);
        BuildHUD(hud);

        // ── 法陣編輯器（預設隱藏）─────────────────────────────
        _editor = new AbilityEditorUI();
        _editor.Visible = false;
        hud.AddChild(_editor);
    }

    private void BuildHUD(CanvasLayer hud)
    {
        // 右上角工具列
        var toolbar = new VBoxContainer();
        toolbar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        toolbar.Position = new Vector2(-200, 8);
        toolbar.CustomMinimumSize = new Vector2(192, 0);
        toolbar.AddThemeConstantOverride("separation", 4);
        hud.AddChild(toolbar);

        // 切換編輯器按鈕
        var editorBtn = MakeBtn("法陣編輯器 [E]", new Color(0.2f, 0.3f, 0.5f));
        editorBtn.Pressed += ToggleEditor;
        toolbar.AddChild(editorBtn);

        toolbar.AddChild(new HSeparator());
        toolbar.AddChild(MakeLbl("材質選擇（左鍵繪製 / 右鍵清除）"));

        // 材質選擇按鈕
        var mats = new (MaterialType, string)[]
        {
            (MaterialType.Sand,  "沙"),
            (MaterialType.Water, "水"),
            (MaterialType.Stone, "石"),
            (MaterialType.Wood,  "木"),
            (MaterialType.Fire,  "火"),
            (MaterialType.Lava,  "岩漿"),
        };

        var grp = new ButtonGroup();
        foreach (var (mat, name) in mats)
        {
            var c = MaterialRegistry.GetColor(mat, 128);
            var btn = MakeBtn(name, new Color(c.R * 0.6f, c.G * 0.6f, c.B * 0.6f));
            btn.ToggleMode = true;
            btn.ButtonGroup = grp;
            btn.ButtonPressed = (mat == MaterialType.Sand);
            var m = mat;
            btn.Toggled += on => { if (on) _world.SelectedMaterial = m; };
            toolbar.AddChild(btn);
        }

        toolbar.AddChild(new HSeparator());
        toolbar.AddChild(MakeLbl("筆刷大小"));

        var brushRow = new HBoxContainer();
        foreach (var (label, size) in new (string, int)[] { ("1", 0), ("3", 1), ("5", 2), ("9", 4) })
        {
            var btn = MakeBtn(label, new Color(0.22f, 0.22f, 0.28f));
            btn.ToggleMode = true;
            btn.ButtonGroup = new ButtonGroup();
            btn.ButtonPressed = (size == 2);
            var s = size;
            btn.Toggled += on => { if (on) _world.BrushSize = s; };
            brushRow.AddChild(btn);
        }
        toolbar.AddChild(brushRow);

        toolbar.AddChild(new HSeparator());

        // 模擬速度
        toolbar.AddChild(MakeLbl("模擬速度"));
        var speedRow = new HBoxContainer();
        foreach (var (label, steps) in new (string, int)[] { ("×1", 1), ("×2", 2), ("×4", 4) })
        {
            var btn = MakeBtn(label, new Color(0.22f, 0.22f, 0.28f));
            btn.ToggleMode = true;
            btn.ButtonGroup = new ButtonGroup();
            btn.ButtonPressed = (steps == 1);
            var sp = steps;
            btn.Toggled += on => { if (on) _world.SimStepsPerFrame = sp; };
            speedRow.AddChild(btn);
        }
        toolbar.AddChild(speedRow);

        // ── 左下角：MP 條 + 操作提示 ──────────────────────────
        _mpLabel = new Label();
        _mpLabel.Text = $"MP  {PlayerController.MaxMp:F0} / {PlayerController.MaxMp:F0}";
        _mpLabel.AnchorTop    = 1f;
        _mpLabel.AnchorBottom = 1f;
        _mpLabel.AnchorLeft   = 0f;
        _mpLabel.AnchorRight  = 0f;
        _mpLabel.Position = new Vector2(10, -52);
        _mpLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.75f, 1.0f));
        _mpLabel.AddThemeFontSizeOverride("font_size", 16);
        hud.AddChild(_mpLabel);

        var hint = new Label();
        hint.Text = "WASD 移動　空白鍵 施放法陣　E 法陣編輯器";
        hint.AnchorTop    = 1f;
        hint.AnchorBottom = 1f;
        hint.Position = new Vector2(10, -28);
        hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(hint);
    }

    // ── 每幀更新 ───────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _player.Tick(dt);

        _mpLabel.Text = $"MP  {_player.Mp:F0} / {PlayerController.MaxMp:F0}";

        if (_editorOpen) return;

        // WASD 移動（按住持續移動，由 moveCooldown 控速）
        int dx = 0, dy = 0;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  dx = -1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dx =  1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))    dy = -1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))  dy =  1;

        if (dx != 0 || dy != 0)
            _player.TryMove(_world.World, dx, dy);
    }

    // ── 鍵盤快捷鍵 ────────────────────────────────────────────────
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;

        switch (k.Keycode)
        {
            case Key.E:
                ToggleEditor();
                break;

            case Key.Space:
                if (!_editorOpen && _editor.SavedSpell != null)
                {
                    bool ok = SpellCaster.TryCast(_editor.SavedSpell, _player, _world.World);
                    if (!ok) GD.Print("[施放] 失敗：MP 不足或冷卻中");
                }
                else if (!_editorOpen)
                {
                    GD.Print("[施放] 尚未儲存任何法陣，請先開啟編輯器（E）設計並儲存");
                }
                break;
        }
    }

    private void ToggleEditor()
    {
        _editorOpen = !_editorOpen;
        _editor.Visible = _editorOpen;
        _world.Visible  = !_editorOpen;
    }

    // ── Helper ────────────────────────────────────────────────────
    private static Button MakeBtn(string text, Color bg)
    {
        var b = new Button { Text = text };
        var s = new StyleBoxFlat { BgColor = bg };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
        b.AddThemeStyleboxOverride("normal", s);
        b.AddThemeStyleboxOverride("hover",  new StyleBoxFlat { BgColor = bg.Lightened(0.15f) });
        b.AddThemeStyleboxOverride("pressed",new StyleBoxFlat { BgColor = bg.Darkened(0.1f)  });
        return b;
    }

    private static Label MakeLbl(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
        l.AddThemeFontSizeOverride("font_size", 11);
        return l;
    }
}
