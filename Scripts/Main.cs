using Godot;
using SkillCreator.AbilitySystem;
using VmContext = SkillCreator.AbilitySystem.VM.ExecutionContext;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.UI;
using SkillCreator.World;
using SkillCreator.World.Materials;

namespace SkillCreator;

public partial class Main : Node
{
    private TileWorldRenderer        _world      = null!;
    private AbilityEditorUI          _editor     = null!;
    private PlayerController         _player     = null!;
    private EnemyManager             _enemies    = new();
    private readonly List<SpellProjectile> _projectiles = new();
    private readonly SpellRunner     _runner     = new();

    private Label   _hpLabel    = null!;
    private Label   _mpLabel    = null!;
    private Label[] _slotLabels = null!;

    private bool _editorOpen = false;

    public override void _Ready()
    {
        // 場景重啟時清除跨局狀態
        VmContext.GlobalVars.Clear();
        VmContext.GlobalLists.Clear();
        EventBus.ClearAll();

        // ── 世界渲染器 ──────────────────────────────────────────
        _world = new TileWorldRenderer();
        AddChild(_world);

        // ── 程序生成地圖 ───────────────────────────────────────
        var spawnData = MapGenerator.Generate(_world.World);

        // ── 玩家 ───────────────────────────────────────────────
        _player = new PlayerController(spawnData.PlayerSpawn);
        _world.Player      = _player;
        _world.Enemies     = _enemies;
        _world.Projectiles = _projectiles;

        _world.World.OnExplosion += (center, radius) =>
            _enemies.ApplyExplosionDamage(center, radius, 40f);

        SpawnEnemies(spawnData.EnemySpawns);

        // ── HUD ────────────────────────────────────────────────
        var hud = new CanvasLayer();
        AddChild(hud);
        BuildHUD(hud);

        // ── 法陣編輯器 ─────────────────────────────────────────
        _editor = new AbilityEditorUI();
        _editor.Visible = false;
        hud.AddChild(_editor);
    }

    private void BuildHUD(CanvasLayer hud)
    {
        // ── 右上角工具列 ─────────────────────────────────────────
        var toolbar = new VBoxContainer();
        toolbar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        toolbar.Position = new Vector2(-200, 8);
        toolbar.CustomMinimumSize = new Vector2(192, 0);
        toolbar.AddThemeConstantOverride("separation", 4);
        hud.AddChild(toolbar);

        var editorBtn = MakeBtn("法陣編輯器 [E]", new Color(0.2f, 0.3f, 0.5f));
        editorBtn.Pressed += ToggleEditor;
        toolbar.AddChild(editorBtn);

        toolbar.AddChild(new HSeparator());
        toolbar.AddChild(MakeLbl("材質選擇（左鍵繪製 / 右鍵清除）"));

        var mats = new (MaterialType, string)[]
        {
            (MaterialType.Sand, "沙"), (MaterialType.Water, "水"),
            (MaterialType.Stone,"石"), (MaterialType.Wood,  "木"),
            (MaterialType.Fire, "火"), (MaterialType.Lava,  "岩漿"),
        };
        var grp = new ButtonGroup();
        foreach (var (mat, name) in mats)
        {
            var c   = MaterialRegistry.GetColor(mat, 128);
            var btn = MakeBtn(name, new Color(c.R * 0.6f, c.G * 0.6f, c.B * 0.6f));
            btn.ToggleMode = true; btn.ButtonGroup = grp;
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
            btn.ToggleMode = true; btn.ButtonGroup = new ButtonGroup();
            btn.ButtonPressed = (size == 2);
            var s = size;
            btn.Toggled += on => { if (on) _world.BrushSize = s; };
            brushRow.AddChild(btn);
        }
        toolbar.AddChild(brushRow);

        toolbar.AddChild(new HSeparator());
        toolbar.AddChild(MakeLbl("模擬速度"));
        var speedRow = new HBoxContainer();
        foreach (var (label, steps) in new (string, int)[] { ("×1", 1), ("×2", 2), ("×4", 4) })
        {
            var btn = MakeBtn(label, new Color(0.22f, 0.22f, 0.28f));
            btn.ToggleMode = true; btn.ButtonGroup = new ButtonGroup();
            btn.ButtonPressed = (steps == 1);
            var sp = steps;
            btn.Toggled += on => { if (on) _world.SimStepsPerFrame = sp; };
            speedRow.AddChild(btn);
        }
        toolbar.AddChild(speedRow);

        // ── 左下角：HP / MP ──────────────────────────────────────
        _hpLabel = new Label();
        _hpLabel.AnchorTop = _hpLabel.AnchorBottom = 1f;
        _hpLabel.Position = new Vector2(10, -74);
        _hpLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.35f, 0.35f));
        _hpLabel.AddThemeFontSizeOverride("font_size", 16);
        hud.AddChild(_hpLabel);

        _mpLabel = new Label();
        _mpLabel.AnchorTop = _mpLabel.AnchorBottom = 1f;
        _mpLabel.Position = new Vector2(10, -52);
        _mpLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.75f, 1.0f));
        _mpLabel.AddThemeFontSizeOverride("font_size", 16);
        hud.AddChild(_mpLabel);

        // ── 技能欄位（底部中間）──────────────────────────────────
        _slotLabels = new Label[SpellLoadout.MaxSlots];
        float slotW   = 120f;
        float totalW  = slotW * SpellLoadout.MaxSlots + 4f * (SpellLoadout.MaxSlots - 1);
        float startX  = (800f - totalW) / 2f;   // 畫面寬 800 px

        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            var lbl = new Label();
            lbl.AnchorTop    = lbl.AnchorBottom = 1f;
            lbl.AnchorLeft   = lbl.AnchorRight  = 0f;
            lbl.Position     = new Vector2(startX + i * (slotW + 4f), -28f);
            lbl.CustomMinimumSize = new Vector2(slotW, 22);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.AddThemeFontSizeOverride("font_size", 11);
            hud.AddChild(lbl);
            _slotLabels[i] = lbl;
        }

        var hint = new Label();
        hint.Text = "A/D 移動　W 跳躍　空白鍵 施放　E 編輯器　1-5 切換技能槽";
        hint.AnchorTop = hint.AnchorBottom = 1f;
        hint.Position  = new Vector2(10, -28);
        hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(hint);
    }

    // ── 每幀更新 ───────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        EventBus.ClearFrame(); // 清除上一幀的廣播訊號

        // 標籤永遠更新
        _hpLabel.Text = $"HP  {_player.Hp:F0} / {PlayerController.MaxHp:F0}";
        _mpLabel.Text = $"MP  {_player.Mp:F0} / {PlayerController.MaxMp:F0}";
        RefreshSlotLabels();

        if (_editorOpen) return;  // ← 編輯器開啟：其餘全暫停

        _player.Tick(dt);
        _enemies.Update(_world.World, _player, dt);
        _runner.Update(dt);

        // 投射物更新
        _projectiles.RemoveAll(p => !p.IsAlive);
        foreach (var p in _projectiles) p.Update(_world.World, _enemies, dt);

        // 物理
        _player.ApplyPhysics(_world.World, dt);

        // A/D 移動
        int dx = 0;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  dx = -1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dx =  1;
        if (dx != 0) _player.TryMove(_world.World, dx, 0);
    }

    // ── 鍵盤快捷鍵 ────────────────────────────────────────────────
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;

        // 數字鍵 1-5：切換技能槽
        if (k.Keycode >= Key.Key1 && k.Keycode <= Key.Key5)
        {
            _editor.Loadout.ActiveIndex = (int)k.Keycode - (int)Key.Key1;
            return;
        }

        switch (k.Keycode)
        {
            case Key.E:
                ToggleEditor();
                break;

            case Key.W:
            case Key.Up:
                if (!_editorOpen && _player.IsOnGround(_world.World))
                    _player.StartJump();
                break;

            case Key.Space:
                if (_editorOpen) break;
                var spell = _editor.Loadout.ActiveSpell;
                if (spell != null)
                {
                    var result = SpellCaster.TryCast(spell, _player, _world.World, _enemies, _editor.Loadout, _runner);
                    if (!result.Ok)
                        GD.Print("[施放] 失敗：MP 不足或冷卻中");
                    else if (result.Projectile != null)
                        _projectiles.Add(result.Projectile);
                }
                else
                {
                    GD.Print("[施放] 槽位空白，請先在編輯器（E）設計並儲存法陣");
                }
                break;
        }
    }

    private void RefreshSlotLabels()
    {
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            bool   active = i == _editor.Loadout.ActiveIndex;
            string name   = _editor.Loadout.SlotLabel(i);
            _slotLabels[i].Text = active ? $"[{i+1}] {name}" : $" {i+1}  {name}";
            _slotLabels[i].AddThemeColorOverride("font_color",
                active ? new Color(1.0f, 0.9f, 0.3f) : new Color(0.5f, 0.5f, 0.55f));
        }
    }

    private void SpawnEnemies(List<(GridPos Pos, EnemyType Type)> spawns)
    {
        foreach (var (pos, type) in spawns)
            _enemies.Spawn(pos, type);
    }

    private void ToggleEditor()
    {
        _editorOpen    = !_editorOpen;
        _editor.Visible = _editorOpen;
        _world.Visible  = !_editorOpen;
        _world.Paused   = _editorOpen;
    }

    // ── Helper ────────────────────────────────────────────────────
    private static Button MakeBtn(string text, Color bg)
    {
        var b = new Button { Text = text };
        var s = new StyleBoxFlat { BgColor = bg };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
        b.AddThemeStyleboxOverride("normal",  s);
        b.AddThemeStyleboxOverride("hover",   new StyleBoxFlat { BgColor = bg.Lightened(0.15f) });
        b.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = bg.Darkened(0.1f)   });
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
