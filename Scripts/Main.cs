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
    private Panel[]          _hotbarPanels  = null!;
    private StyleBoxFlat[]   _hotbarStyles  = null!;
    private Panel[]          _hotbarIcons   = null!;  // 物品色塊縮圖
    private StyleBoxFlat[]   _iconStyles    = null!;
    private Label[]          _hotbarCounts  = null!;  // 右下角數量
    private PanelContainer   _tooltip       = null!;  // 懸停提示框
    private Label            _tooltipLabel  = null!;
    private float            _placeCooldown   = 0f;
    private bool             _mouseOverHotbar = false; // 滑鼠在熱鍵欄上時暫停採掘/放置
    private Label            _paintModeLabel  = null!;

    // 等級 / XP / 境界 / 裝備 HUD
    private Label         _lvLabel           = null!;
    private Label         _tierLabel         = null!;
    private Label         _xpLabel           = null!;
    private Panel         _xpBarFill         = null!;
    private StyleBoxFlat  _xpFillStyle       = null!;
    private Label         _equipLabel        = null!;
    private Label         _breakthroughLabel = null!;
    private float         _breakthroughTimer = 0f;
    private string        _lastTierName      = "學徒";

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
        VmContext.TaskCounters.Clear();
        VmContext.TaskCounterReached.Clear();
        EventBus.ClearAll();
        GameClock.Reset();
        CombatState.Reset();

        // ── 世界渲染器 ──────────────────────────────────────────
        _world = new TileWorldRenderer();
        AddChild(_world);

        // ── 程序生成地圖 ───────────────────────────────────────
        var spawnData = MapGenerator.Generate(_world.World);

        // ── 玩家 ───────────────────────────────────────────────
        _player = new PlayerController(spawnData.PlayerSpawn);
        // 初始道具（prototype 用；合成系統完成後可移除）
        _player.Inventory.TryAdd(ItemId.ToolBasicPick,      1);
        _player.Inventory.TryAdd(ItemId.ToolBasicAxe,       1);
        _player.Inventory.TryAdd(ItemId.EquipBasicSword,    1);
        _player.Inventory.TryAdd(ItemId.EquipLeatherArmor,  1);
        _player.Inventory.TryAdd(ItemId.EquipAmulet,        1);
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
        hint.Text = "A/D 移動  W 跳躍  空白 施放  E 編輯器  1-5 技能  左鍵 採掘  右鍵 放置  滾輪 切換物品  Ctrl+滾輪 縮放  F1 畫筆  Q 裝備";
        hint.AnchorTop = hint.AnchorBottom = 1f;
        hint.Position  = new Vector2(10, -28);
        hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(hint);

        BuildHotbar(hud);
        BuildLevelHUD(hud);

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
        const float slotW  = 48f;
        const float slotH  = 48f;
        const float gap    = 3f;
        const float startX = 10f;
        const float startY = -132f; // HP 標籤上方

        _hotbarPanels = new Panel[count];
        _hotbarStyles = new StyleBoxFlat[count];
        _hotbarIcons  = new Panel[count];
        _iconStyles   = new StyleBoxFlat[count];
        _hotbarCounts = new Label[count];

        for (int i = 0; i < count; i++)
        {
            // ── 槽位外框 ──────────────────────────────────────────
            var slotStyle = new StyleBoxFlat();
            slotStyle.BgColor     = new Color(0.10f, 0.10f, 0.15f);
            slotStyle.BorderWidthTop = slotStyle.BorderWidthBottom =
            slotStyle.BorderWidthLeft = slotStyle.BorderWidthRight = 1;
            slotStyle.BorderColor = new Color(0.30f, 0.30f, 0.40f);
            _hotbarStyles[i] = slotStyle;

            var panel = new Panel();
            panel.AnchorTop  = panel.AnchorBottom = 1f;
            panel.AnchorLeft = panel.AnchorRight  = 0f;
            panel.Position   = new Vector2(startX + i * (slotW + gap), startY);
            panel.Size       = new Vector2(slotW, slotH);
            panel.AddThemeStyleboxOverride("panel", slotStyle);
            hud.AddChild(panel);
            _hotbarPanels[i] = panel;

            // ── 物品色塊縮圖 ──────────────────────────────────────
            var iconStyle = new StyleBoxFlat { BgColor = new Color(0.18f, 0.18f, 0.22f) };
            _iconStyles[i] = iconStyle;

            var icon = new Panel();
            icon.Position    = new Vector2(6f, 6f);
            icon.Size        = new Vector2(36f, 28f);
            icon.AddThemeStyleboxOverride("panel", iconStyle);
            icon.MouseFilter = Control.MouseFilterEnum.Ignore; // 不攔截滑鼠，讓事件傳到父槽
            panel.AddChild(icon);
            _hotbarIcons[i] = icon;

            // ── 右下角數量標籤 ────────────────────────────────────
            var countLbl = new Label();
            countLbl.Position = new Vector2(28f, 34f);
            countLbl.Size     = new Vector2(18f, 12f);
            countLbl.HorizontalAlignment = HorizontalAlignment.Right;
            countLbl.AddThemeFontSizeOverride("font_size", 9);
            countLbl.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.85f));
            countLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
            panel.AddChild(countLbl);
            _hotbarCounts[i] = countLbl;

            // ── 點擊選取 + 懸停提示框 ────────────────────────────
            int idx = i;
            panel.GuiInput += (InputEvent e) =>
            {
                if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                    _player.Inventory.ActiveHotbarIndex = idx;
            };
            panel.MouseEntered += () => ShowTooltip(idx);
            panel.MouseExited  += HideTooltip;
        }

        // ── 提示框（PanelContainer 自動依文字寬度縮放）───────────
        var tipStyle = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.18f, 0.95f) };
        tipStyle.BorderWidthTop = tipStyle.BorderWidthBottom =
        tipStyle.BorderWidthLeft = tipStyle.BorderWidthRight = 1;
        tipStyle.BorderColor = new Color(0.50f, 0.50f, 0.75f);
        tipStyle.ContentMarginLeft = tipStyle.ContentMarginRight = 8f;
        tipStyle.ContentMarginTop  = tipStyle.ContentMarginBottom = 5f;

        _tooltip = new PanelContainer();
        _tooltip.AnchorTop  = _tooltip.AnchorBottom = 1f;
        _tooltip.AnchorLeft = _tooltip.AnchorRight  = 0f;
        _tooltip.AddThemeStyleboxOverride("panel", tipStyle);
        _tooltip.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tooltip.Visible     = false;
        hud.AddChild(_tooltip);

        _tooltipLabel = new Label();
        _tooltipLabel.AddThemeFontSizeOverride("font_size", 11);
        _tooltipLabel.AddThemeColorOverride("font_color", new Color(0.90f, 0.90f, 0.95f));
        _tooltip.AddChild(_tooltipLabel);
    }

    private void BuildLevelHUD(CanvasLayer hud)
    {
        // 裝備欄文字（熱鍵欄上方 y=-185）
        _equipLabel = new Label();
        _equipLabel.AnchorTop = _equipLabel.AnchorBottom = 1f;
        _equipLabel.Position  = new Vector2(10, -185f);
        _equipLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 0.65f));
        _equipLabel.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(_equipLabel);

        // 等級標籤（y=-170）
        _lvLabel = new Label();
        _lvLabel.AnchorTop = _lvLabel.AnchorBottom = 1f;
        _lvLabel.Position  = new Vector2(10, -170f);
        _lvLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.25f));
        _lvLabel.AddThemeFontSizeOverride("font_size", 13);
        hud.AddChild(_lvLabel);

        // 境界標籤（等級右側，顏色隨境界動態更新）
        _tierLabel = new Label();
        _tierLabel.AnchorTop = _tierLabel.AnchorBottom = 1f;
        _tierLabel.Position  = new Vector2(58f, -170f);
        _tierLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        _tierLabel.AddThemeFontSizeOverride("font_size", 13);
        hud.AddChild(_tierLabel);

        // XP 數字標籤（境界標籤右側）
        _xpLabel = new Label();
        _xpLabel.AnchorTop = _xpLabel.AnchorBottom = 1f;
        _xpLabel.Position  = new Vector2(120f, -169f);
        _xpLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.6f));
        _xpLabel.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(_xpLabel);

        // XP 進度條背景（y=-157）
        var xpBgStyle = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.18f) };
        var xpBg = new Panel();
        xpBg.AnchorTop  = xpBg.AnchorBottom = 1f;
        xpBg.Position   = new Vector2(10f, -157f);
        xpBg.Size       = new Vector2(200f, 7f);
        xpBg.AddThemeStyleboxOverride("panel", xpBgStyle);
        hud.AddChild(xpBg);

        // XP 進度條填充（子節點，每幀更新 Size.X）
        _xpFillStyle = new StyleBoxFlat { BgColor = new Color(0.25f, 0.65f, 0.95f) };
        _xpBarFill = new Panel();
        _xpBarFill.Position = Vector2.Zero;
        _xpBarFill.Size     = new Vector2(0f, 7f);
        _xpBarFill.AddThemeStyleboxOverride("panel", _xpFillStyle);
        xpBg.AddChild(_xpBarFill);

        // 境界突破通知（畫面中央，短暫顯示後淡出）
        _breakthroughLabel = new Label();
        _breakthroughLabel.AnchorLeft  = _breakthroughLabel.AnchorRight  = 0.5f;
        _breakthroughLabel.AnchorTop   = _breakthroughLabel.AnchorBottom = 0.5f;
        _breakthroughLabel.Position    = new Vector2(-120f, -60f);
        _breakthroughLabel.AddThemeFontSizeOverride("font_size", 20);
        _breakthroughLabel.Visible     = false;
        hud.AddChild(_breakthroughLabel);
    }

    // ── 每幀更新 ───────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        EventBus.ClearFrame(); // 清除上一幀的廣播訊號
        GameClock.Advance(dt);
        CombatState.Advance(dt);

        // 標籤永遠更新
        _hpLabel.Text = $"HP  {_player.Hp:F0} / {PlayerController.MaxHp:F0}";
        _mpLabel.Text = $"MP  {_player.Mp:F0} / {_player.MaxMp:F0}";
        RefreshSlotLabels();
        RefreshHotbar();
        RefreshLevelHUD();
        _paintModeLabel.Visible = _world.PaintingEnabled;

        // 境界門檻同步至刻印庫（讓鎖定狀態即時反映）
        _editor.PlayerLevel = _player.Level;

        // 境界突破通知
        if (_player.JustBrokeThrough)
        {
            _player.JustBrokeThrough = false;
            _lastTierName = _player.TierName;
            var (r, g, b) = PlayerController.GetTierColor(_player.Level);
            _breakthroughLabel.Text    = $"⬆ 境界突破：{_player.TierName}";
            _breakthroughLabel.AddThemeColorOverride("font_color", new Color(r, g, b));
            _breakthroughLabel.Visible = true;
            _breakthroughTimer         = 3f;
        }
        if (_breakthroughTimer > 0f)
        {
            _breakthroughTimer -= dt;
            float alpha = Math.Clamp(_breakthroughTimer / 1f, 0f, 1f); // 最後 1 秒淡出
            var col = _breakthroughLabel.GetThemeColor("font_color");
            _breakthroughLabel.AddThemeColorOverride("font_color",
                new Color(col.R, col.G, col.B, alpha));
            if (_breakthroughTimer <= 0f) _breakthroughLabel.Visible = false;
        }

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

        // 採掘（按住左鍵，距離 ≤ MiningRange；滑鼠在 HUD 上時不觸發）
        if (Input.IsMouseButtonPressed(MouseButton.Left) && !_mouseOverHotbar)
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

        if (Input.IsMouseButtonPressed(MouseButton.Right) && _placeCooldown <= 0f && !_mouseOverHotbar)
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
            case Key.Q:
                if (!_editorOpen)
                {
                    int hi = _player.Inventory.ActiveHotbarIndex;
                    var activeStack = _player.Inventory.Slots[hi];
                    if (!activeStack.IsEmpty && ItemRegistry.Get(activeStack.ItemId).EquipSlot != EquipmentSlotType.None)
                        _player.Equipment.TryEquip(_player.Inventory, hi);
                }
                break;

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

            // 槽位外框
            var slotStyle = _hotbarStyles[i];
            slotStyle.BgColor = active ? new Color(0.18f, 0.18f, 0.28f) : new Color(0.10f, 0.10f, 0.15f);
            slotStyle.BorderColor = active ? new Color(0.95f, 0.80f, 0.20f) : new Color(0.30f, 0.30f, 0.40f);
            slotStyle.BorderWidthTop = slotStyle.BorderWidthBottom =
            slotStyle.BorderWidthLeft = slotStyle.BorderWidthRight = active ? 2 : 1;

            // 色塊縮圖 + 數量
            var stack = _player.Inventory.Slots[i];
            if (stack.IsEmpty)
            {
                _iconStyles[i].BgColor  = new Color(0.18f, 0.18f, 0.22f); // 空槽暗色
                _hotbarCounts[i].Text   = "";
            }
            else
            {
                _iconStyles[i].BgColor  = GetItemIconColor(stack.ItemId);
                _hotbarCounts[i].Text   = stack.Count > 1 ? $"×{stack.Count}" : "";
            }
        }
    }

    // 滑鼠移入槽位 → 顯示提示框 + 標記滑鼠在 HUD 上
    private void ShowTooltip(int slotIndex)
    {
        _mouseOverHotbar = true;
        var stack = _player.Inventory.Slots[slotIndex];
        if (stack.IsEmpty) { HideTooltip(); return; }

        _tooltipLabel.Text = ItemRegistry.Get(stack.ItemId).DisplayName;

        const float slotW  = 48f;
        const float gap    = 3f;
        const float startX = 10f;
        const float startY = -132f;

        float slotLeft = startX + slotIndex * (slotW + gap);
        _tooltip.Position = new Vector2(slotLeft, startY - 30f); // 槽位上方
        _tooltip.Visible  = true;
    }

    private void HideTooltip() { _mouseOverHotbar = false; _tooltip.Visible = false; }

    private void RefreshLevelHUD()
    {
        int xpReq = PlayerController.XpRequired(_player.Level);
        _lvLabel.Text  = $"LV {_player.Level}";
        _xpLabel.Text  = $"{_player.Xp:F0} / {xpReq} XP";
        float ratio    = xpReq > 0 ? Math.Clamp(_player.Xp / xpReq, 0f, 1f) : 1f;
        _xpBarFill.Size = new Vector2(200f * ratio, 7f);

        // 境界名稱 + 顏色
        _tierLabel.Text = $"【{_player.TierName}】";
        var (r, g, b) = PlayerController.GetTierColor(_player.Level);
        _tierLabel.AddThemeColorOverride("font_color", new Color(r, g, b));

        var eq = _player.Equipment;
        string wn  = eq.WeaponId    != ItemId.None ? ItemRegistry.Get(eq.WeaponId).DisplayName    : "─";
        string an  = eq.ArmorId     != ItemId.None ? ItemRegistry.Get(eq.ArmorId).DisplayName     : "─";
        string acn = eq.AccessoryId != ItemId.None ? ItemRegistry.Get(eq.AccessoryId).DisplayName : "─";
        _equipLabel.Text = $"W:{wn}  A:{an}  飾:{acn}";
    }

    // 依 ItemId 決定縮圖顏色：方塊物品取材質基礎色；工具/裝備用固定色
    private static Color GetItemIconColor(ItemId id)
    {
        var data = ItemRegistry.Get(id);
        if (data.IsPlaceable && data.PlaceAs.HasValue)
            return MaterialRegistry.Get(data.PlaceAs.Value).BaseColor;
        return id switch
        {
            ItemId.ToolBasicPick      => new Color(0.75f, 0.75f, 0.82f), // 銀灰
            ItemId.ToolBasicAxe       => new Color(0.58f, 0.38f, 0.16f), // 木棕
            ItemId.EquipBasicSword    => new Color(0.80f, 0.80f, 0.95f), // 淡藍銀
            ItemId.EquipLeatherArmor  => new Color(0.60f, 0.40f, 0.20f), // 皮革棕
            ItemId.EquipAmulet        => new Color(0.90f, 0.30f, 0.85f), // 紫紅
            _                         => new Color(0.85f, 0.75f, 0.15f), // 金色通用
        };
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
