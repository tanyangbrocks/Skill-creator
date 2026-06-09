using Godot;
using SkillCreator.AbilitySystem;
using VmContext = SkillCreator.AbilitySystem.VM.ExecutionContext;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.UI;
using SkillCreator.World;
using SkillCreator.World.Items;
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
    private readonly DroppedItemManager _droppedItems = new();

    private Label   _hpLabel    = null!;
    private Label   _mpLabel    = null!;
    private Label[] _slotLabels = null!;

    // 物品熱鍵欄 HUD
    private Panel[]        _hotbarPanels  = null!;
    private Label[]        _hotbarLabels  = null!;
    private StyleBoxFlat[] _hotbarStyles  = null!;
    private float          _placeCooldown = 0f;
    private Label          _paintModeLabel = null!;

    // 鏡頭縮放（1 = 最遠/全覽，10 = 最近/預設）
    private float _cameraZoom = 10f;
    private const float ZoomMin  = 1f;
    private const float ZoomMax  = 10f;
    private const float ZoomStep = 1.2f; // 每格滾輪縮放倍率（約 12 步橫跨全範圍）

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
        // 初始工具（prototype 用；合成系統完成後可移除）
        _player.Inventory.TryAdd(ItemId.ToolBasicPick, 1);
        _player.Inventory.TryAdd(ItemId.ToolBasicAxe,  1);
        _world.Player          = _player;
        _world.Enemies         = _enemies;
        _world.Projectiles     = _projectiles;
        _world.DroppedItems    = _droppedItems;
        _world.PaintingEnabled = false;  // 遊玩模式下禁用 debug 繪圖筆，改由採掘/放置系統接手

        _world.World.OnExplosion += (center, radius) =>
            _enemies.ApplyExplosionDamage(center, radius, 40f);

        // 方塊被摧毀時產生掉落物
        _world.World.OnTileDestroyed += (pos, mat) => _droppedItems.Spawn(pos, mat);

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
        hint.Text = "A/D 移動  W 跳躍  空白 施放  E 編輯器  1-5 技能  左鍵 採掘  右鍵 放置  滾輪 切換物品  Ctrl+滾輪 縮放  F1 畫筆";
        hint.AnchorTop = hint.AnchorBottom = 1f;
        hint.Position  = new Vector2(10, -28);
        hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(hint);

        BuildHotbar(hud);

        // 畫筆模式指示器（F1 切換時顯示）
        _paintModeLabel = new Label();
        _paintModeLabel.Text = "⚒ 畫筆模式  [F1 關閉]";
        _paintModeLabel.AnchorLeft = _paintModeLabel.AnchorRight = 0.5f;
        _paintModeLabel.AnchorTop  = _paintModeLabel.AnchorBottom = 0f;
        _paintModeLabel.Position   = new Vector2(-100f, 6f);
        _paintModeLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.2f));
        _paintModeLabel.AddThemeFontSizeOverride("font_size", 13);
        _paintModeLabel.Visible = false;
        hud.AddChild(_paintModeLabel);
    }

    private void BuildHotbar(CanvasLayer hud)
    {
        const int   count  = Inventory.HotbarSize;
        const float slotW  = 46f;
        const float slotH  = 36f;
        const float gap    = 3f;
        const float startX = 10f;
        const float startY = -120f; // HP 標籤上方

        _hotbarPanels = new Panel[count];
        _hotbarLabels = new Label[count];
        _hotbarStyles = new StyleBoxFlat[count];

        for (int i = 0; i < count; i++)
        {
            var style = new StyleBoxFlat();
            style.BgColor     = new Color(0.10f, 0.10f, 0.15f);
            style.BorderWidthTop = style.BorderWidthBottom =
            style.BorderWidthLeft = style.BorderWidthRight = 1;
            style.BorderColor = new Color(0.30f, 0.30f, 0.40f);
            _hotbarStyles[i] = style;

            var panel = new Panel();
            panel.AnchorTop  = panel.AnchorBottom = 1f;
            panel.AnchorLeft = panel.AnchorRight  = 0f;
            panel.Position   = new Vector2(startX + i * (slotW + gap), startY);
            panel.Size       = new Vector2(slotW, slotH);
            panel.AddThemeStyleboxOverride("panel", style);
            hud.AddChild(panel);
            _hotbarPanels[i] = panel;

            var lbl = new Label();
            lbl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.VerticalAlignment   = VerticalAlignment.Center;
            lbl.AddThemeFontSizeOverride("font_size", 9);
            panel.AddChild(lbl);
            _hotbarLabels[i] = lbl;
        }
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
        RefreshHotbar();
        _paintModeLabel.Visible = _world.PaintingEnabled;

        // 滑鼠世界格座標（FocalPoint 積木用）
        var mp = _world.GetLocalMousePosition();
        _player.MouseGridPos = new GridPos(
            Math.Clamp((int)(mp.X / TileWorldRenderer.TilePixels), 0, _world.World.Width  - 1),
            Math.Clamp((int)(mp.Y / TileWorldRenderer.TilePixels), 0, _world.World.Height - 1));

        if (_editorOpen)
        {
            _player.CancelMining();
            return;
        }

        _player.Tick(dt);
        _enemies.Update(_world.World, _player, dt);
        _runner.Update(dt);

        // 投射物更新
        _projectiles.RemoveAll(p => !p.IsAlive);
        foreach (var p in _projectiles) p.Update(_world.World, _enemies, dt);

        // 物理
        _player.ApplyPhysics(_world.World, dt);

        // 鏡頭跟隨玩家（Camera2D.Limit 自動限制於世界邊界）
        _world.Camera.Position = new Vector2(
            (_player.Position.X + 0.5f) * TileWorldRenderer.TilePixels,
            (_player.Position.Y + 0.5f) * TileWorldRenderer.TilePixels);
        _world.Camera.Zoom = new Vector2(_cameraZoom, _cameraZoom);

        // 掉落物（重力 + 壽命 + 自動拾取）
        _droppedItems.Update(_world.World, _player, dt);

        // A/D 移動
        int dx = 0;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  dx = -1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dx =  1;
        if (dx != 0) _player.TryMove(_world.World, dx, 0);

        // 採掘（按住左鍵，距離 ≤ MiningRange）
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var target = _player.MouseGridPos;
            if (_player.Position.DistanceTo(target) <= PlayerController.MiningRange)
                _player.TickMining(_world.World, target, dt);
            else
                _player.CancelMining();
        }
        else
        {
            _player.CancelMining();
        }

        // 放置（右鍵，含冷卻避免過快連放）
        if (_placeCooldown > 0f) _placeCooldown -= dt;

        if (Input.IsMouseButtonPressed(MouseButton.Right) && _placeCooldown <= 0f)
        {
            var target = _player.MouseGridPos;
            var active = _player.Inventory.ActiveItem;
            if (!active.IsEmpty)
            {
                var itemData = ItemRegistry.Get(active.ItemId);
                if (itemData.IsPlaceable && itemData.PlaceAs.HasValue
                    && target != _player.Position
                    && _player.Position.DistanceTo(target) <= PlayerController.MiningRange
                    && _world.World.TypeAt(target.X, target.Y) == MaterialType.Air)
                {
                    _world.World.Set(target.X, target.Y, itemData.PlaceAs.Value);
                    _player.Inventory.Consume(_player.Inventory.ActiveHotbarIndex);
                    _placeCooldown = 0.12f;
                }
            }
        }
    }

    // ── 鍵盤快捷鍵 ────────────────────────────────────────────────
    public override void _Input(InputEvent e)
    {
        // 滾輪：Ctrl 按住 → 調整鏡頭縮放；否則 → 切換物品熱鍵欄槽位
        if (!_editorOpen && e is InputEventMouseButton mw)
        {
            bool ctrl = Input.IsKeyPressed(Key.Ctrl);
            if (mw.ButtonIndex == MouseButton.WheelUp)
            {
                if (ctrl) _cameraZoom = Math.Clamp(_cameraZoom * ZoomStep, ZoomMin, ZoomMax);
                else _player.Inventory.ActiveHotbarIndex =
                    (_player.Inventory.ActiveHotbarIndex - 1 + Inventory.HotbarSize) % Inventory.HotbarSize;
                return;
            }
            if (mw.ButtonIndex == MouseButton.WheelDown)
            {
                if (ctrl) _cameraZoom = Math.Clamp(_cameraZoom / ZoomStep, ZoomMin, ZoomMax);
                else _player.Inventory.ActiveHotbarIndex =
                    (_player.Inventory.ActiveHotbarIndex + 1) % Inventory.HotbarSize;
                return;
            }
        }

        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;

        // 數字鍵 1-5：切換技能槽
        if (k.Keycode >= Key.Key1 && k.Keycode <= Key.Key5)
        {
            _editor.Loadout.ActiveIndex = (int)k.Keycode - (int)Key.Key1;
            return;
        }

        switch (k.Keycode)
        {
            case Key.F1:
                _world.PaintingEnabled = !_world.PaintingEnabled;
                break;

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

    private void RefreshHotbar()
    {
        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            bool active = i == _player.Inventory.ActiveHotbarIndex;
            var  style  = _hotbarStyles[i];

            style.BgColor     = active ? new Color(0.20f, 0.20f, 0.30f) : new Color(0.10f, 0.10f, 0.15f);
            style.BorderColor = active ? new Color(0.95f, 0.80f, 0.20f) : new Color(0.30f, 0.30f, 0.40f);
            style.BorderWidthTop = style.BorderWidthBottom =
            style.BorderWidthLeft = style.BorderWidthRight = active ? 2 : 1;

            var stack = _player.Inventory.Slots[i];
            if (stack.IsEmpty)
            {
                _hotbarLabels[i].Text = $"{i + 1}";
                _hotbarLabels[i].AddThemeColorOverride("font_color", new Color(0.35f, 0.35f, 0.40f));
            }
            else
            {
                var data = ItemRegistry.Get(stack.ItemId);
                _hotbarLabels[i].Text = stack.Count > 1
                    ? $"{data.DisplayName}\n×{stack.Count}"
                    : data.DisplayName;
                _hotbarLabels[i].AddThemeColorOverride("font_color",
                    active ? new Color(1.0f, 1.0f, 0.9f) : new Color(0.75f, 0.75f, 0.80f));
            }
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
