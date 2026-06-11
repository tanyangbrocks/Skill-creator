using Godot;
using System.Linq;
using SkillCreator.AbilitySystem;
using VmContext = SkillCreator.AbilitySystem.VM.ExecutionContext;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.Snapshot;
using SkillCreator.UI;
using SkillCreator.World;
using SkillCreator.World.Items;
using SkillCreator.World.Materials;

namespace SkillCreator;

public partial class Main : Node
{
    private TileWorldRenderer3D      _renderer3d  = null!;
    private TileWorld3D              _world3d     = null!;
    private CameraController         _camera3d    = null!;
    private int                      _simStepsPerFrame = 1;
    private bool                     _simPaused        = false;
    private AbilityEditorUI          _editor     = null!;
    private SpellListUI              _spellList  = null!;
    private PlayerController         _player     = null!;
    private EnemyManager             _enemies    = new();

    // Phase 2-C：實體 3D 視覺（玩家 + 敵人）
    private Node3D         _entitiesRoot = null!;         // 統一開關用父節點
    private MeshInstance3D _playerMesh   = null!;
    private readonly Dictionary<int, MeshInstance3D> _enemyMeshes = new();
    private readonly List<SpellProjectile> _projectiles = new();
    private readonly SpellRunner     _runner     = new();
    private readonly DroppedItemManager _droppedItems = new();

    private Label   _hpLabel    = null!;
    private Label   _mpLabel    = null!;
    private Label[]  _slotLabels      = null!;
    private string[] _slotLabelCache  = null!;

    // 物品熱鍵欄 HUD
    private Panel[]          _hotbarPanels  = null!;
    private StyleBoxFlat[]   _hotbarStyles  = null!;
    private Panel[]          _hotbarIcons   = null!;  // 物品色塊縮圖
    private StyleBoxFlat[]   _iconStyles    = null!;
    private Label[]          _hotbarCounts  = null!;  // 右下角數量
    private float            _placeCooldown   = 0f;
    private bool             _mouseOverHotbar = false; // 滑鼠在熱鍵欄上時暫停採掘/放置
    private Label            _paintModeLabel  = null!;
    private PanelContainer   _paintToolPanel  = null!;
    private PanelContainer   _devToolsPanel   = null!;

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

    // 生存數值 HUD
    private Panel[]        _survivalBarFills   = null!;
    private StyleBoxFlat[] _survivalFillStyles = null!;
    private Label[]        _survivalValLabels  = null!;
    private Label          _bodyTempLabel      = null!;

    // 角色數值面板
    private PanelContainer _statsPanel         = null!;
    private Label          _statsPanelContent  = null!;
    private bool           _statsPanelOpen     = false;

    // 物品欄面板（I 鍵）
    private Panel            _inventoryPanel = null!;
    private Panel[]          _invSlotPanels  = null!;
    private StyleBoxFlat[]   _invSlotStyles  = null!;
    private StyleBoxFlat[]   _invIconStyles  = null!;
    private Label[]          _invSlotCounts  = null!;
    private bool             _inventoryOpen  = false;

    // 裝備欄面板（P 鍵）
    private Panel            _equipPanel       = null!;
    private StyleBoxFlat[]   _eqSlotStyles     = new StyleBoxFlat[3];
    private StyleBoxFlat[]   _eqIconStyles     = new StyleBoxFlat[3];
    private Label[]          _eqNameLabels     = new Label[3];
    private bool             _equipPanelOpen   = false;

    // 通用懸浮 Tooltip（跟隨游標，物品欄/裝備欄/熱鍵欄共用）
    private PanelContainer   _floatTooltip      = null!;
    private Label            _floatTooltipLabel = null!;

    // 物品欄拖曳
    private int            _dragSrcSlot   = -1;
    private Panel?         _dragFloatIcon  = null;
    private StyleBoxFlat?  _dragFloatStyle = null;

    // 偵錯 overlay（F2 座標驗證）
    private Panel  _debugCoordPanel   = null!;
    private Label  _debugCoordLabel   = null!;
    private bool   _debugCoordEnabled = false;

    // 偵錯 overlay（F4 生存速率）
    private Panel  _debugSurvivalPanel   = null!;
    private Label  _debugSurvivalLabel   = null!;
    private bool   _debugSurvivalEnabled = false;

    // 偵錯（F5 快照取樣 / F6 回滾並比對）
    private sealed record SnapCompare(float PlayerHp, int[] EnemyIds, float[] EnemyHps, MaterialType TileUnderPlayer);
    private SnapCompare? _snapBefore = null;

    // 傷害數字（浮動文字池）
    private const int   DmgPoolSize     = 24;
    private const float DmgNumDuration  = 1.2f;
    private const float DmgNumRiseSpeed = 28f; // screen px/s
    private struct ActiveDmgNum
    {
        public Label   Lbl;
        public Vector2 WorldPx;
        public float   Timer;
        public float   RiseY;
        public bool    Active;
        public Color   BaseColor;
    }
    private ActiveDmgNum[] _dmgPool     = null!;
    public  bool           ShowDamageNumbers { get; set; } = true;

    // U/I/O/P 組合鍵施放 — 上一幀按壓狀態（rising-edge 偵測）
    private bool _prevCastU, _prevCastI, _prevCastO, _prevCastP;

    // 組合鍵對應槽位（多鍵優先 → 最長組合排最前）
    private static readonly (Key[] Keys, int Slot)[] _castCombos =
    {
        (new[] { Key.U, Key.I, Key.O, Key.P }, 9),
        (new[] { Key.I, Key.O, Key.P },        8),
        (new[] { Key.U, Key.I, Key.O },        7),
        (new[] { Key.O, Key.P },               6),
        (new[] { Key.I, Key.O },               5),
        (new[] { Key.U, Key.I },               4),
        (new[] { Key.P },                      3),
        (new[] { Key.O },                      2),
        (new[] { Key.I },                      1),
        (new[] { Key.U },                      0),
    };

    // 槽位鍵位提示文字（對應 _castCombos 的 Slot 順序）
    private static readonly string[] _slotKeys =
        { "U", "I", "O", "P", "U+I", "I+O", "O+P", "U+I+O", "I+O+P", "U+I+O+P" };

    // 鏡頭縮放（3D 模式：調整正交尺寸）
    private float _orthoZoom = 30f;
    private const float ZoomMin  = 8f;
    private const float ZoomMax  = 80f;
    private const float ZoomStep = 1.2f;

    // 世界尺寸（3D）
    // WorldD：SideScroll2D 只需薄層（Z=0 可見），保持 4 讓 CA 有鄰居，
    // 未來切換真 3D 視角時再調高。
    private const int WorldW = 600;
    private const int WorldH = 200;
    private const int WorldD = 4;

    private bool _editorOpen = false;

    public override void _Ready()
    {
        // 場景重啟時清除跨局狀態
        InputBindings.RegisterAll();
        VmContext.GlobalVars.Clear();
        VmContext.GlobalLists.Clear();
        VmContext.TaskCounters.Clear();
        VmContext.TaskCounterReached.Clear();
        VmContext.TraceMode = false;
        EventBus.ClearAll();
        ActionBus.ClearAll();
        SnapshotManager.Clear();
        GameClock.Reset();
        CombatState.Reset();

        // ── 3D 世界 + 渲染器 + 鏡頭 ───────────────────────────
        _world3d = new TileWorld3D(WorldW, WorldH, WorldD);
        var spawnData = MapGenerator3D.Generate(_world3d);

        _renderer3d = new TileWorldRenderer3D();
        _renderer3d.Initialize(_world3d);
        AddChild(_renderer3d);

        _camera3d = new CameraController();
        AddChild(_camera3d);
        // 遊戲預設使用 2D 側捲視角（Phase 2-B），Tab 可切換
        _camera3d.SetMode(CameraController.CameraMode.SideScroll2D);
        _camera3d.SetOrthoSize(_orthoZoom);

        // ── 實體 3D 視覺（Phase 2-C）────────────────────────────────
        _entitiesRoot = new Node3D();
        AddChild(_entitiesRoot);

        _playerMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.65f, 0.9f, 0.65f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 0.55f, 1.0f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _entitiesRoot.AddChild(_playerMesh);

        // ── 玩家 ───────────────────────────────────────────────
        _player = new PlayerController(spawnData.PlayerSpawn);
        // 初始道具（prototype 用；合成系統完成後可移除）
        _player.Inventory.TryAdd(ItemId.ToolBasicPick,      1);
        _player.Inventory.TryAdd(ItemId.ToolBasicAxe,       1);
        _player.Inventory.TryAdd(ItemId.ToolIronPick,       1);
        _player.Inventory.TryAdd(ItemId.EquipBasicSword,    1);
        _player.Inventory.TryAdd(ItemId.EquipLeatherArmor,  1);
        _player.Inventory.TryAdd(ItemId.EquipAmulet,        1);

        // 初始裝備自動穿戴（對應裝備欄空時）
        for (int i = 0; i < Inventory.TotalSize; i++)
        {
            var s = _player.Inventory.Slots[i];
            if (s.IsEmpty) continue;
            var itemData = ItemRegistry.Get(s.ItemId);
            if (itemData.EquipSlot == EquipmentSlotType.None) continue;
            bool slotEmpty = itemData.EquipSlot switch
            {
                EquipmentSlotType.Weapon    => _player.Equipment.WeaponId    == ItemId.None,
                EquipmentSlotType.Armor     => _player.Equipment.ArmorId     == ItemId.None,
                EquipmentSlotType.Accessory => _player.Equipment.AccessoryId == ItemId.None,
                _                           => false,
            };
            if (slotEmpty) _player.Equipment.TryEquip(_player.Inventory, i);
        }

        _world3d.OnExplosion += (center, radius) =>
            _enemies.ApplyExplosionDamage(center, radius, 40f);

        // 方塊被摧毀時產生掉落物
        _world3d.OnTileDestroyed += (pos, mat) => _droppedItems.Spawn(pos, mat);

        SpawnEnemies(spawnData.EnemySpawns);

        // ── HUD ────────────────────────────────────────────────
        var hud = new CanvasLayer();
        AddChild(hud);
        BuildHUD(hud);

        // ── 技能整構編輯器 ─────────────────────────────────────────
        _editor = new AbilityEditorUI();
        _editor.Visible = false;
        hud.AddChild(_editor);
        _editor.BackPressed += OnEditorBack;

        // ── 技能創建空間（圓球列表）───────────────────────────
        _spellList = new SpellListUI();
        _spellList.Loadout  = _editor.Loadout;
        _spellList.Visible  = false;
        hud.AddChild(_spellList);
        _spellList.ActiveSpellClicked += OnListActiveSpellClicked;
        _spellList.AddSpellRequested  += OnListAddSpellRequested;

        CombatState.OnHit = (pos, amount, isPlayer) => SpawnDmgNum(pos, amount, isPlayer);
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

        var editorBtn = MakeBtn("技能整構編輯器 [E]", new Color(0.2f, 0.3f, 0.5f));
        editorBtn.Pressed += ToggleEditor;
        toolbar.AddChild(editorBtn);

        var statsBtn = MakeBtn("角色數值 [C]", new Color(0.2f, 0.35f, 0.25f));
        statsBtn.Pressed += ToggleStatsPanel;
        toolbar.AddChild(statsBtn);

        toolbar.AddChild(new HSeparator());
        toolbar.AddChild(MakeLbl("模擬速度"));
        var speedRow = new HBoxContainer();
        foreach (var (label, steps) in new (string, int)[] { ("×1", 1), ("×2", 2), ("×4", 4) })
        {
            var btn = MakeBtn(label, new Color(0.22f, 0.22f, 0.28f));
            btn.ToggleMode = true; btn.ButtonGroup = new ButtonGroup();
            btn.ButtonPressed = (steps == 1);
            var sp = steps;
            btn.Toggled += on => { if (on) _simStepsPerFrame = sp; };
            speedRow.AddChild(btn);
        }
        toolbar.AddChild(speedRow);

        toolbar.AddChild(new HSeparator());
        var devBtn = MakeBtn("🛠 開發者工具", new Color(0.22f, 0.18f, 0.28f));
        devBtn.Pressed += () => _devToolsPanel.Visible = !_devToolsPanel.Visible;
        toolbar.AddChild(devBtn);

        // ── 開發者工具清單面板（🛠 按鈕展開） ──────────────────────
        var devPanelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.12f, 0.96f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor = new Color(0.38f, 0.30f, 0.48f),
            ContentMarginLeft = 10f, ContentMarginRight = 10f,
            ContentMarginTop = 8f, ContentMarginBottom = 8f,
        };
        _devToolsPanel = new PanelContainer();
        _devToolsPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _devToolsPanel.Position = new Vector2(-200, 185);
        _devToolsPanel.AddThemeStyleboxOverride("panel", devPanelStyle);
        _devToolsPanel.Visible = false;
        var devLbl = new Label();
        devLbl.Text =
            "F1  開發者筆刷（材質繪製）\n" +
            "F2  座標驗證 overlay\n" +
            "F3  VM 執行追蹤\n" +
            "F4  生存速率 overlay\n" +
            "F5  快照取樣（半徑 5）\n" +
            "F6  快照回滾並比對";
        devLbl.AddThemeFontSizeOverride("font_size", 11);
        devLbl.AddThemeColorOverride("font_color", new Color(0.80f, 0.78f, 0.88f));
        _devToolsPanel.AddChild(devLbl);
        hud.AddChild(_devToolsPanel);

        // ── 開發者筆刷面板（F1 切換） ────────────────────────────────
        var paintBgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.10f, 0.14f, 0.95f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor = new Color(0.30f, 0.50f, 0.30f),
            ContentMarginLeft = 6f, ContentMarginRight = 6f,
            ContentMarginTop = 6f, ContentMarginBottom = 6f,
        };
        _paintToolPanel = new PanelContainer();
        _paintToolPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _paintToolPanel.Position = new Vector2(-200, 185);
        _paintToolPanel.CustomMinimumSize = new Vector2(186, 0);
        _paintToolPanel.AddThemeStyleboxOverride("panel", paintBgStyle);
        _paintToolPanel.Visible = false;
        var paintVBox = new VBoxContainer();
        paintVBox.AddThemeConstantOverride("separation", 3);
        _paintToolPanel.AddChild(paintVBox);
        paintVBox.AddChild(MakeLbl("材質選擇（左鍵繪製 / 右鍵清除）"));
        var mats = new (MaterialType, string)[]
        {
            (MaterialType.Sand, "沙"), (MaterialType.Water, "水"),
            (MaterialType.Stone,"石"), (MaterialType.Wood,  "木"),
            (MaterialType.Fire, "火"), (MaterialType.Lava,  "岩漿"),
        };
        var grp = new ButtonGroup();
        foreach (var (mat, matName) in mats)
        {
            var c   = MaterialRegistry.GetColor(mat, 128);
            var btn = MakeBtn(matName, new Color(c.R * 0.6f, c.G * 0.6f, c.B * 0.6f));
            btn.ToggleMode = true; btn.ButtonGroup = grp;
            btn.ButtonPressed = (mat == MaterialType.Sand);
            var m = mat;
            btn.Toggled += on => { if (on) GD.Print($"[繪圖] 材質切換（3D 模式暫未實作）: {m}"); };
            paintVBox.AddChild(btn);
        }
        paintVBox.AddChild(new HSeparator());
        paintVBox.AddChild(MakeLbl("筆刷大小"));
        var brushRow = new HBoxContainer();
        foreach (var (blbl, size) in new (string, int)[] { ("1", 0), ("3", 1), ("5", 2), ("9", 4) })
        {
            var btn = MakeBtn(blbl, new Color(0.22f, 0.22f, 0.28f));
            btn.ToggleMode = true; btn.ButtonGroup = new ButtonGroup();
            btn.ButtonPressed = (size == 2);
            var s = size;
            btn.Toggled += on => { _ = s; }; // stub：3D 繪圖筆暫未實作
            brushRow.AddChild(btn);
        }
        paintVBox.AddChild(brushRow);
        hud.AddChild(_paintToolPanel);

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
        const float slotW2  = 74f;
        const float slotGap = 3f;
        float totalW  = slotW2 * SpellLoadout.MaxSlots + slotGap * (SpellLoadout.MaxSlots - 1);
        float startX  = (800f - totalW) / 2f;   // 畫面寬 800 px

        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            var lbl = new Label();
            lbl.AnchorTop    = lbl.AnchorBottom = 1f;
            lbl.AnchorLeft   = lbl.AnchorRight  = 0f;
            lbl.Position     = new Vector2(startX + i * (slotW2 + slotGap), -28f);
            lbl.CustomMinimumSize = new Vector2(slotW2, 22);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.AddThemeFontSizeOverride("font_size", 10);
            lbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.55f));
            hud.AddChild(lbl);
            _slotLabels[i] = lbl;
        }
        _slotLabelCache = new string[SpellLoadout.MaxSlots];

        var hint = new Label();
        hint.Text = "A/D 移動  W 跳躍  U/I/O/P 組合鍵施放  E 編輯器  C 數值  左鍵 採掘  右鍵 放置  滾輪 切換物品  Ctrl+滾輪 縮放  F1 畫筆  Q 裝備";
        hint.AnchorTop = hint.AnchorBottom = 1f;
        hint.Position  = new Vector2(10, -28);
        hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(hint);

        BuildHotbar(hud);
        BuildLevelHUD(hud);
        BuildSurvivalHUD(hud);
        BuildStatsPanel(hud);
        BuildDebugOverlay(hud);
        BuildSurvivalDebugOverlay(hud);
        BuildInventoryPanel(hud);
        BuildEquipPanel(hud);
        BuildFloatTooltip(hud);
        BuildDragIcon(hud);

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

        // 傷害數字物件池
        _dmgPool = new ActiveDmgNum[DmgPoolSize];
        for (int i = 0; i < DmgPoolSize; i++)
        {
            var lbl = new Label();
            lbl.AddThemeFontSizeOverride("font_size", 13);
            lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
            lbl.Visible     = false;
            hud.AddChild(lbl);
            _dmgPool[i] = new ActiveDmgNum { Lbl = lbl };
        }
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

            // ── 數字鍵提示（左上角小字）──────────────────────────
            var keyLbl = new Label();
            keyLbl.Position = new Vector2(3f, 2f);
            keyLbl.Text     = (i == 9) ? "0" : $"{i + 1}";
            keyLbl.AddThemeFontSizeOverride("font_size", 9);
            keyLbl.AddThemeColorOverride("font_color", new Color(0.50f, 0.50f, 0.60f));
            keyLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
            panel.AddChild(keyLbl);

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

        // ── 背包按鈕（熱鍵欄最右側）────────────────────────────
        {
            const float bagW = 34f;
            const float bagH = 30f;
            var bagBtn = MakeBtn("▣", new Color(0.18f, 0.18f, 0.28f));
            bagBtn.AnchorTop  = bagBtn.AnchorBottom = 1f;
            bagBtn.AnchorLeft = bagBtn.AnchorRight  = 0f;
            bagBtn.Position   = new Vector2(startX + count * (slotW + gap) + 4f,
                                            startY + (slotH - bagH) / 2f);
            bagBtn.CustomMinimumSize = new Vector2(bagW, bagH);
            bagBtn.AddThemeFontSizeOverride("font_size", 14);
            bagBtn.Pressed      += ToggleInventoryPanel;
            bagBtn.MouseEntered += () => _mouseOverHotbar = true;
            bagBtn.MouseExited  += () => _mouseOverHotbar = false;
            hud.AddChild(bagBtn);
        }

    }

    private void BuildLevelHUD(CanvasLayer hud)
    {
        // 裝備欄文字（熱鍵欄上方 y=-185）
        _equipLabel = new Label();
        _equipLabel.AnchorTop = _equipLabel.AnchorBottom = 1f;
        _equipLabel.Position  = new Vector2(10, -290f);
        _equipLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 0.65f));
        _equipLabel.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(_equipLabel);

        // 等級標籤（y=-170）
        _lvLabel = new Label();
        _lvLabel.AnchorTop = _lvLabel.AnchorBottom = 1f;
        _lvLabel.Position  = new Vector2(10, -275f);
        _lvLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.25f));
        _lvLabel.AddThemeFontSizeOverride("font_size", 13);
        hud.AddChild(_lvLabel);

        // 境界標籤（等級右側，顏色隨境界動態更新）
        _tierLabel = new Label();
        _tierLabel.AnchorTop = _tierLabel.AnchorBottom = 1f;
        _tierLabel.Position  = new Vector2(58f, -275f);
        _tierLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        _tierLabel.AddThemeFontSizeOverride("font_size", 13);
        hud.AddChild(_tierLabel);

        // XP 數字標籤（境界標籤右側）
        _xpLabel = new Label();
        _xpLabel.AnchorTop = _xpLabel.AnchorBottom = 1f;
        _xpLabel.Position  = new Vector2(120f, -274f);
        _xpLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.6f));
        _xpLabel.AddThemeFontSizeOverride("font_size", 11);
        hud.AddChild(_xpLabel);

        // XP 進度條背景（y=-157）
        var xpBgStyle = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.18f) };
        var xpBg = new Panel();
        xpBg.AnchorTop  = xpBg.AnchorBottom = 1f;
        xpBg.Position   = new Vector2(10f, -262f);
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
        _hpLabel.Text = $"HP  {_player.Hp:F0} / {_player.MaxHp:F0}";
        _mpLabel.Text = $"MP  {_player.Mp:F0} / {_player.MaxMp:F0}";
        RefreshSlotLabels();
        RefreshHotbar();
        RefreshLevelHUD();
        RefreshSurvivalHUD();
        if (_statsPanelOpen)    RefreshStatsPanel();
        if (_inventoryOpen)    RefreshInventoryPanel();
        if (_equipPanelOpen)   RefreshEquipPanel();
        if (_floatTooltip.Visible) UpdateFloatTooltipPos();
        if (_dragSrcSlot >= 0 && _dragFloatIcon != null)
        {
            var mouse = GetViewport().GetMousePosition();
            _dragFloatIcon.Position = mouse - new Vector2(15f, 15f);
        }
        _paintModeLabel.Visible = false; // 3D 模式暫不支援繪圖筆

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

        // 滑鼠世界格座標：透過 CameraController 將螢幕座標投影到 Z=0 平面取得世界座標
        {
            var screenMouse = GetViewport().GetMousePosition();
            var worldPos3   = _camera3d.ProjectScreenToWorld(screenMouse);
            _player.MouseGridPos = new GridPos(
                Math.Clamp((int)worldPos3.X, 0, _world3d.Width  - 1),
                Math.Clamp((int)worldPos3.Y, 0, _world3d.Height - 1));

            if (_debugCoordEnabled)
            {
                var mgp = _player.MouseGridPos;
                var pp  = _player.Position;
                int ddx = Math.Sign(mgp.X - pp.X);
                int ddy = Math.Sign(mgp.Y - pp.Y);
                if (ddx == 0 && ddy == 0) ddx = _player.Facing.X;
                _debugCoordLabel.Text =
                    $"[偵錯]  F2=座標  F3=VM追蹤\n" +
                    $"螢幕:  ({screenMouse.X:F0}, {screenMouse.Y:F0}) px\n" +
                    $"世界:  ({worldPos3.X:F2}, {worldPos3.Y:F2}, {worldPos3.Z:F2})\n" +
                    $"格:    ({mgp.X}, {mgp.Y})\n" +
                    $"玩家:  ({pp.X}, {pp.Y})\n" +
                    $"方向:  ({ddx}, {ddy})\n" +
                    $"OrthoZoom: {_orthoZoom:F1}";
            }

            if (_debugSurvivalEnabled) RefreshSurvivalDebug();
        }

        if (_editorOpen)
        {
            _player.CancelMining();
            _prevCastU = _prevCastI = _prevCastO = _prevCastP = false;
            return;
        }

        // U/I/O/P 組合鍵施放（rising-edge 偵測，多鍵優先）
        {
            bool curU = Input.IsKeyPressed(Key.U);
            bool curI = Input.IsKeyPressed(Key.I);
            bool curO = Input.IsKeyPressed(Key.O);
            bool curP = Input.IsKeyPressed(Key.P);

            int triggered = -1;
            foreach (var (keys, slot) in _castCombos)
            {
                bool allNow  = keys.All(k => k switch
                    { Key.U => curU, Key.I => curI, Key.O => curO, Key.P => curP, _ => false });
                bool allPrev = keys.All(k => k switch
                    { Key.U => _prevCastU, Key.I => _prevCastI, Key.O => _prevCastO, Key.P => _prevCastP, _ => false });
                if (allNow && !allPrev) { triggered = slot; break; }
            }
            if (triggered >= 0) TryCastSlot(triggered);

            _prevCastU = curU; _prevCastI = curI; _prevCastO = curO; _prevCastP = curP;
        }

        _player.UpdateEnvironment(_world3d);  // W-5b：設置氧氣/環境溫度旗標
        _player.Tick(dt);

        // 世界 CA 模擬（SimStepsPerFrame 步/幀，暫停時跳過）
        if (!_simPaused)
        {
            _world3d.ClearOccupied();
            _world3d.SetOccupied(_player.Position.X, _player.Position.Y, 0);
            foreach (var e in _enemies.Enemies)
                if (e.IsAlive) _world3d.SetOccupied(e.Position.X, e.Position.Y, 0);

            // 以玩家為中心的 Chunk 座標（裁剪用）
            int pCX = _player.Position.X / Chunk3D.Size;
            int pCY = _player.Position.Y / Chunk3D.Size;

            for (int _s = 0; _s < _simStepsPerFrame; _s++)
                _world3d.Tick(centerCX: pCX, centerCY: pCY, simRadius: 6); // 半徑 6 chunk = 96 格

            _renderer3d.RebuildDirtyMeshes(
                maxPerFrame:  30,
                sideScroll2D: true,
                viewCX: pCX, viewCY: pCY, viewRadius: 5); // 半徑 5 chunk = 80 格（≈畫面 1.5 倍）
        }

        _enemies.Update(_world3d, _player, dt);
        _runner.Update(dt);

        // 投射物更新
        for (int i = _projectiles.Count - 1; i >= 0; i--)
            if (!_projectiles[i].IsAlive) _projectiles.RemoveAt(i);
        foreach (var p in _projectiles) p.Update(_world3d, _enemies, dt);

        // 物理
        _player.ApplyPhysics(_world3d, dt);

        // Phase 2-C：更新玩家與敵人的 3D 視覺位置
        _playerMesh.Position = new Vector3(_player.Position.X + 0.5f, _player.Position.Y + 0.45f, 0.5f);
        SyncEnemyMeshes();

        // 鏡頭跟隨玩家（CameraController 接管，只需更新目標位置）
        _camera3d.TargetPosition = new Vector3(
            _player.Position.X + 0.5f, _player.Position.Y + 0.5f, 0f);

        // 掉落物（重力 + 壽命 + 自動拾取）
        _droppedItems.Update(_world3d, _player, dt);

        // 傷害數字動畫更新
        if (ShowDamageNumbers && _dmgPool != null)
        {
            for (int i = 0; i < DmgPoolSize; i++)
            {
                if (!_dmgPool[i].Active) continue;
                _dmgPool[i].Timer -= dt;
                _dmgPool[i].RiseY += DmgNumRiseSpeed * dt;

                if (_dmgPool[i].Timer <= 0f)
                {
                    _dmgPool[i].Active      = false;
                    _dmgPool[i].Lbl.Visible = false;
                    continue;
                }

                // WorldPx 現在存放世界單位座標，透過 CameraController 投影到螢幕
                var worldPos3d = new Vector3(_dmgPool[i].WorldPx.X, _dmgPool[i].WorldPx.Y, 0f);
                var screenPos  = _camera3d.WorldToScreen(worldPos3d);
                screenPos.Y   -= _dmgPool[i].RiseY;
                _dmgPool[i].Lbl.Position = screenPos;

                float alpha = Math.Clamp(_dmgPool[i].Timer / 0.4f, 0f, 1f);
                var   col   = _dmgPool[i].BaseColor;
                _dmgPool[i].Lbl.AddThemeColorOverride("font_color", new Color(col.R, col.G, col.B, alpha));
            }
        }

        // A/D 移動
        int dx = 0;
        if (Input.IsActionPressed(InputBindings.MoveLeft))  dx = -1;
        if (Input.IsActionPressed(InputBindings.MoveRight)) dx =  1;
        if (dx != 0) _player.TryMove(_world3d, dx, 0);

        // 採掘（按住左鍵，距離 ≤ MiningRange；滑鼠在 HUD/面板上時不觸發）
        if (Input.IsMouseButtonPressed(MouseButton.Left) && !_mouseOverHotbar && !_inventoryOpen && !_equipPanelOpen)
        {
            var target = _player.MouseGridPos;
            if (_player.Position.DistanceTo(target) <= PlayerController.MiningRange)
                _player.TickMining(_world3d, target, dt);
            else
                _player.CancelMining();
        }
        else
        {
            _player.CancelMining();
        }

        // 放置（右鍵，含冷卻避免過快連放）
        if (_placeCooldown > 0f) _placeCooldown -= dt;

        if (Input.IsMouseButtonPressed(MouseButton.Right) && _placeCooldown <= 0f && !_mouseOverHotbar && !_inventoryOpen && !_equipPanelOpen)
        {
            var target = _player.MouseGridPos;
            var active = _player.Inventory.ActiveItem;
            if (!active.IsEmpty)
            {
                var itemData = ItemRegistry.Get(active.ItemId);
                if (itemData.IsPlaceable && itemData.PlaceAs.HasValue
                    && target != _player.Position
                    && _player.Position.DistanceTo(target) <= PlayerController.MiningRange
                    && _world3d.TypeAt(target.X, target.Y) == MaterialType.Air)
                {
                    _world3d.Set(target.X, target.Y, itemData.PlaceAs.Value);
                    _player.Inventory.Consume(_player.Inventory.ActiveHotbarIndex);
                    _placeCooldown = 0.12f;
                }
            }
        }
    }

    // ── 鍵盤快捷鍵 ────────────────────────────────────────────────
    public override void _Input(InputEvent e)
    {
        // 數字鍵 1~9 / 0 → 切換熱鍵欄槽位（0~9）
        if (e is InputEventKey ek && ek.Pressed && !ek.Echo)
        {
            int slot = ek.Keycode switch
            {
                Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3, Key.Key5 => 4,
                Key.Key6 => 5, Key.Key7 => 6, Key.Key8 => 7, Key.Key9 => 8, Key.Key0 => 9,
                _ => -1,
            };
            if (slot >= 0) { _player.Inventory.ActiveHotbarIndex = slot; return; }
        }

        // 滾輪：Ctrl 按住 → 調整鏡頭縮放；否則 → 切換物品熱鍵欄槽位
        if (!_editorOpen && e is InputEventMouseButton mw)
        {
            bool ctrl = Input.IsKeyPressed(Key.Ctrl);
            if (mw.ButtonIndex == MouseButton.WheelUp)
            {
                if (ctrl) { _orthoZoom = Math.Clamp(_orthoZoom / ZoomStep, ZoomMin, ZoomMax); _camera3d.SetOrthoSize(_orthoZoom); }
                else _player.Inventory.ActiveHotbarIndex =
                    (_player.Inventory.ActiveHotbarIndex - 1 + Inventory.HotbarSize) % Inventory.HotbarSize;
                return;
            }
            if (mw.ButtonIndex == MouseButton.WheelDown)
            {
                if (ctrl) { _orthoZoom = Math.Clamp(_orthoZoom * ZoomStep, ZoomMin, ZoomMax); _camera3d.SetOrthoSize(_orthoZoom); }
                else _player.Inventory.ActiveHotbarIndex =
                    (_player.Inventory.ActiveHotbarIndex + 1) % Inventory.HotbarSize;
                return;
            }
        }

        // 物品欄拖曳 / 格內點擊裝備（左鍵，物品欄開啟時）
        if (!_editorOpen && _inventoryOpen && e is InputEventMouseButton imb && imb.ButtonIndex == MouseButton.Left)
        {
            if (imb.Pressed)
            {
                int src = GetInvSlotUnderMouse();
                if (src >= 0)
                {
                    _dragSrcSlot = src;
                    var srcStack = _player.Inventory.Slots[src];
                    if (!srcStack.IsEmpty)
                    {
                        _dragFloatStyle!.BgColor = GetItemIconColor(srcStack.ItemId);
                        _dragFloatIcon!.Visible  = true;
                    }
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (_dragSrcSlot >= 0)
            {
                if (_dragFloatIcon != null) _dragFloatIcon.Visible = false;
                int dst = GetInvSlotUnderMouse();
                if (dst >= 0 && dst != _dragSrcSlot)
                {
                    _player.Inventory.SwapSlots(_dragSrcSlot, dst);
                    RefreshInventoryPanel();
                    RefreshEquipPanel();
                }
                else if (dst == _dragSrcSlot)
                {
                    var s = _player.Inventory.Slots[_dragSrcSlot];
                    if (!s.IsEmpty && ItemRegistry.Get(s.ItemId).EquipSlot != EquipmentSlotType.None)
                    {
                        _player.Equipment.TryEquip(_player.Inventory, _dragSrcSlot);
                        RefreshInventoryPanel();
                        RefreshEquipPanel();
                    }
                }
                _dragSrcSlot = -1;
                GetViewport().SetInputAsHandled();
            }
        }

        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;

        // 其餘按鍵：全部透過 InputBindings 對應 action
        if (k.IsAction(InputBindings.EquipItem) && !_editorOpen)
        {
            int hi = _player.Inventory.ActiveHotbarIndex;
            var activeStack = _player.Inventory.Slots[hi];
            if (!activeStack.IsEmpty && ItemRegistry.Get(activeStack.ItemId).EquipSlot != EquipmentSlotType.None)
                _player.Equipment.TryEquip(_player.Inventory, hi);
        }
        else if (k.IsAction(InputBindings.TogglePaint))
        {
            _paintToolPanel.Visible  = !_paintToolPanel.Visible; // 3D 模式：只切換面板顯示
        }
        else if (k.IsAction(InputBindings.DebugCoord))
        {
            _debugCoordEnabled = !_debugCoordEnabled;
            _debugCoordPanel.Visible = _debugCoordEnabled;
        }
        else if (k.IsAction(InputBindings.DebugVmTrace))
        {
            VmContext.TraceMode = !VmContext.TraceMode;
            GD.Print($"[偵錯] VM 追蹤：{(VmContext.TraceMode ? "開啟" : "關閉")}");
        }
        else if (k.IsAction(InputBindings.DebugSurvival))
        {
            _debugSurvivalEnabled = !_debugSurvivalEnabled;
            _debugSurvivalPanel.Visible = _debugSurvivalEnabled;
        }
        else if (k.IsAction(InputBindings.DebugSnapTake))
        {
            DebugSnapTake();
        }
        else if (k.IsAction(InputBindings.DebugSnapRoll))
        {
            DebugSnapRollback();
        }
        else if (k.IsAction(InputBindings.OpenStats) && !_editorOpen)
        {
            ToggleStatsPanel();
        }
        else if (k.IsAction(InputBindings.OpenInventory) && !_editorOpen)
        {
            ToggleInventoryPanel();
        }
        else if (k.IsAction(InputBindings.OpenEquipment) && !_editorOpen)
        {
            ToggleEquipPanel();
        }
        else if (k.IsAction(InputBindings.OpenEditor))
        {
            ToggleEditor();
        }
        else if (k.IsAction(InputBindings.Jump) && !_editorOpen && _player.IsOnGround(_world3d))
        {
            _player.StartJump();
        }
        else if (k.Keycode == Key.Up && !_editorOpen && _player.IsOnGround(_world3d))
        {
            _player.StartJump(); // 方向鍵上仍可跳（不加入鍵位系統，固定輔助鍵）
        }
    }

    private void RefreshSlotLabels()
    {
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            string text = $"[{_slotKeys[i]}]{_editor.Loadout.SlotLabel(i)}";
            if (text == _slotLabelCache[i]) continue;
            _slotLabelCache[i]  = text;
            _slotLabels[i].Text = text;
        }
    }

    private void TryCastSlot(int slotIdx)
    {
        var spell = _editor.Loadout.GetSlot(slotIdx);
        if (spell == null)
        {
            GD.Print($"[施放] 槽位 {slotIdx} 空白");
            return;
        }
        var result = SpellCaster.TryCast(spell, _player, _world3d, _enemies, _editor.Loadout, _runner);
        if (!result.Ok)
            GD.Print($"[施放] 槽位 {slotIdx} 失敗：MP 不足或冷卻中");
        else if (result.Projectile != null)
            _projectiles.Add(result.Projectile);
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
                _hotbarCounts[i].Text   = $"×{stack.Count}";
            }
        }
    }

    // 滑鼠移入槽位 → 顯示提示框 + 標記滑鼠在 HUD 上
    private void ShowTooltip(int slotIndex)
    {
        _mouseOverHotbar = true;
        var stack = _player.Inventory.Slots[slotIndex];
        if (stack.IsEmpty) { HideTooltip(); return; }
        ShowFloatTooltip(ItemRegistry.Get(stack.ItemId).DisplayName);
    }

    private void HideTooltip() { _mouseOverHotbar = false; HideFloatTooltip(); }

    // ── 通用懸浮 Tooltip（跟隨游標）─────────────────────────────────────
    private void BuildFloatTooltip(CanvasLayer hud)
    {
        var bgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.12f, 0.95f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor = new Color(0.50f, 0.50f, 0.75f),
            ContentMarginLeft = 8f, ContentMarginRight = 8f,
            ContentMarginTop = 4f,  ContentMarginBottom = 4f,
        };
        bgStyle.CornerRadiusTopLeft = bgStyle.CornerRadiusTopRight =
        bgStyle.CornerRadiusBottomLeft = bgStyle.CornerRadiusBottomRight = 3;

        _floatTooltip = new PanelContainer();
        _floatTooltip.AddThemeStyleboxOverride("panel", bgStyle);
        _floatTooltip.Visible    = false;
        _floatTooltip.MouseFilter = Control.MouseFilterEnum.Ignore;
        hud.AddChild(_floatTooltip);

        _floatTooltipLabel = new Label();
        _floatTooltipLabel.AddThemeFontSizeOverride("font_size", 12);
        _floatTooltipLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.75f));
        _floatTooltip.AddChild(_floatTooltipLabel);
    }

    private void ShowFloatTooltip(string text)
    {
        if (string.IsNullOrEmpty(text)) { _floatTooltip.Visible = false; return; }
        _floatTooltipLabel.Text = text;
        _floatTooltip.Visible   = true;
        UpdateFloatTooltipPos();
    }

    private void HideFloatTooltip() => _floatTooltip.Visible = false;

    private void UpdateFloatTooltipPos()
    {
        var mouse    = GetViewport().GetMousePosition();
        var vpSize   = GetViewport().GetVisibleRect().Size;
        _floatTooltip.ResetSize();
        var tipSize  = _floatTooltip.Size;
        float x = mouse.X + 14f;
        float y = mouse.Y - tipSize.Y - 8f;
        if (x + tipSize.X > vpSize.X) x = mouse.X - tipSize.X - 6f;
        if (y < 0) y = mouse.Y + 18f;
        _floatTooltip.Position = new Vector2(x, y);
    }

    // ── 拖曳圖示（跟隨游標的小色塊）──────────────────────────────────────
    private void BuildDragIcon(CanvasLayer hud)
    {
        _dragFloatStyle = new StyleBoxFlat { BgColor = Colors.Transparent };
        _dragFloatIcon  = new Panel();
        _dragFloatIcon.Size        = new Vector2(30f, 30f);
        _dragFloatIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
        _dragFloatIcon.Visible     = false;
        _dragFloatIcon.AddThemeStyleboxOverride("panel", _dragFloatStyle);
        hud.AddChild(_dragFloatIcon);
    }

    private int GetInvSlotUnderMouse()
    {
        if (!_inventoryOpen || _invSlotPanels == null) return -1;
        var mouse = GetViewport().GetMousePosition();
        for (int i = 0; i < _invSlotPanels.Length; i++)
        {
            if (_invSlotPanels[i].GetGlobalRect().HasPoint(mouse)) return i;
        }
        return -1;
    }

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
            ItemId.ToolIronPick       => new Color(0.60f, 0.62f, 0.68f), // 鐵灰
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

    // Phase 2-C：敵人 3D Mesh 同步（建立 / 更新位置 / 隱藏死亡者）
    private void SyncEnemyMeshes()
    {
        foreach (var e in _enemies.Enemies)
        {
            if (!_enemyMeshes.TryGetValue(e.Id, out var mesh))
            {
                mesh = CreateEnemyMesh(e.Type);
                _entitiesRoot.AddChild(mesh);
                _enemyMeshes[e.Id] = mesh;
            }
            mesh.Visible  = e.IsAlive;
            if (!e.IsAlive) continue;

            float mh = e.Type is EnemyType.Heavy ? 1.8f : 0.9f;
            mesh.Position = new Vector3(e.Position.X + 0.5f, e.Position.Y + mh * 0.5f, 0.5f);
        }
    }

    private static MeshInstance3D CreateEnemyMesh(EnemyType type)
    {
        float w = type is EnemyType.Heavy ? 1.55f : 0.70f;
        float h = type is EnemyType.Heavy ? 1.80f : 0.90f;
        var col = type switch
        {
            EnemyType.Melee  => new Color(0.90f, 0.15f, 0.15f), // 紅
            EnemyType.Ranged => new Color(0.90f, 0.50f, 0.10f), // 橙
            EnemyType.Patrol => new Color(0.35f, 0.25f, 0.80f), // 藍紫
            EnemyType.Heavy  => new Color(0.55f, 0.08f, 0.08f), // 暗紅
            _                => new Color(0.60f, 0.60f, 0.60f),
        };
        return new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w, h, w) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = col,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
    }

    private void ToggleEditor()
    {
        _editorOpen = !_editorOpen;
        if (_editorOpen)
        {
            _spellList.Refresh();
            _spellList.Visible = true;
            _editor.Visible    = false;
            Input.MouseMode    = Input.MouseModeEnum.Visible; // 編輯器需要可見滑鼠
        }
        else
        {
            _spellList.Visible = false;
            _editor.Visible    = false;
            _camera3d.ApplyMouseCapture(); // 回到遊戲，視角需要時重新捕捉
        }
        _renderer3d.Visible   = !_editorOpen;
        _entitiesRoot.Visible = !_editorOpen;
        _simPaused            = _editorOpen;
    }

    // 圓球列表：點擊主動技能圓球 → 進入對應槽位編輯
    private void OnListActiveSpellClicked(int slotIndex)
    {
        _spellList.Visible = false;
        _editor.OpenSlot(slotIndex);
        _editor.Visible = true;
    }

    // 圓球列表：點擊「+」→ 新增技能
    private void OnListAddSpellRequested()
    {
        // 優先找第一個空主動槽（Loadout 中未儲存過的槽位）
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            if (_editor.Loadout.GetSlot(i) is null)
            {
                _spellList.Visible = false;
                _editor.OpenSlot(i);
                _editor.Visible = true;
                return;
            }
        }
        // 主動全滿：新增被動技能（尚無被動編輯 UI，先建立空物件並提示）
        if (_editor.Loadout.PassiveCount < SpellLoadout.MaxPassiveSlots)
        {
            var newPassive = new SpellArray();
            var pt = TotemLibrary.AllTotems.FirstOrDefault(t => t.Id == "passive_continuous");
            if (pt is not null) newPassive.Slots.Add(new SpellSlot { Totem = pt });
            _editor.Loadout.AddPassive(newPassive);
            _spellList.Refresh();
        }
    }

    // 技能整構編輯器：點擊「← 返回」→ 回到圓球列表
    private void OnEditorBack()
    {
        _editor.Visible = false;
        _spellList.Refresh();
        _spellList.Visible = true;
    }

    // ── Helper ────────────────────────────────────────────────────
    private static Button MakeBtn(string text, Color bg)
    {
        var b = new Button { Text = text };
        b.FocusMode = Control.FocusModeEnum.None; // 防止空白鍵意外觸發已聚焦按鈕
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

    // ── 偵錯 Overlay（F2 座標驗證）────────────────────────────────────
    private void BuildDebugOverlay(CanvasLayer hud)
    {
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.70f) };
        bgStyle.CornerRadiusTopLeft = bgStyle.CornerRadiusTopRight =
        bgStyle.CornerRadiusBottomLeft = bgStyle.CornerRadiusBottomRight = 3;

        _debugCoordPanel = new Panel();
        _debugCoordPanel.Position = new Vector2(8f, 8f);
        _debugCoordPanel.Size     = new Vector2(220f, 110f);
        _debugCoordPanel.AddThemeStyleboxOverride("panel", bgStyle);
        _debugCoordPanel.Visible  = false;
        hud.AddChild(_debugCoordPanel);

        _debugCoordLabel = new Label();
        _debugCoordLabel.Position = new Vector2(7f, 5f);
        _debugCoordLabel.Size     = new Vector2(206f, 100f);
        _debugCoordLabel.AddThemeFontSizeOverride("font_size", 10);
        _debugCoordLabel.AddThemeColorOverride("font_color", new Color(0.55f, 1.0f, 0.45f));
        _debugCoordPanel.AddChild(_debugCoordLabel);
    }

    // ── 生存速率偵錯 overlay（F4）────────────────────────────────────────
    private void BuildSurvivalDebugOverlay(CanvasLayer hud)
    {
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.70f) };
        bgStyle.CornerRadiusTopLeft = bgStyle.CornerRadiusTopRight =
        bgStyle.CornerRadiusBottomLeft = bgStyle.CornerRadiusBottomRight = 3;

        _debugSurvivalPanel = new Panel();
        _debugSurvivalPanel.Position = new Vector2(240f, 8f);  // coord overlay 右側
        _debugSurvivalPanel.Size     = new Vector2(250f, 155f);
        _debugSurvivalPanel.AddThemeStyleboxOverride("panel", bgStyle);
        _debugSurvivalPanel.Visible  = false;
        hud.AddChild(_debugSurvivalPanel);

        _debugSurvivalLabel = new Label();
        _debugSurvivalLabel.Position = new Vector2(7f, 5f);
        _debugSurvivalLabel.Size     = new Vector2(236f, 145f);
        _debugSurvivalLabel.AddThemeFontSizeOverride("font_size", 10);
        _debugSurvivalLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.45f));
        _debugSurvivalPanel.AddChild(_debugSurvivalLabel);
    }

    private void RefreshSurvivalDebug()
    {
        var s   = _player.State;
        bool ic = CombatState.InCombat;

        static string Bar(float v, float max) =>
            $"{v,6:F1}/{max:F0}";

        static string TimeUntil(float v, float drain) =>
            drain > 0f && v > 0f ? $" ~{v / drain:F0}s" : "";

        float thirstDrain = CharacterState.ThirstDrainPerSec * (s.IsHeatstroke ? CharacterState.ThirstHeatMultiplier : 1f);
        float staminaRate = ic ? -CharacterState.StaminaDrainCombat : CharacterState.StaminaRegenPerSec;
        float mentalRate  = ic ? -CharacterState.MentalEnergyDrainCombat : CharacterState.MentalEnergyRegenPerSec;
        float tempDir     = MathF.Sign(s.AmbientTemperature - s.BodyTemperature) * CharacterState.BodyTempAdaptRate;
        float tempToHypo  = s.BodyTemperature > CharacterState.HypothermiaThreshold
                            ? (s.BodyTemperature - CharacterState.HypothermiaThreshold) / CharacterState.BodyTempAdaptRate : 0f;
        float tempToHeat  = s.BodyTemperature < CharacterState.HeatstrokeThreshold
                            ? (CharacterState.HeatstrokeThreshold - s.BodyTemperature) / CharacterState.BodyTempAdaptRate : 0f;

        string flags = "";
        if (s.IsDehydrated)    flags += " 🔴脫水";
        if (s.IsStarving)      flags += " 🔴飢餓";
        if (s.IsSuffocating)   flags += " 🔴窒息";
        if (s.IsHypothermic)   flags += " 🔴低溫";
        if (s.IsHeatstroke)    flags += " 🔴中暑";
        if (s.IsStaminaDepleted) flags += " ⚠體力";
        if (flags == "") flags = " OK";

        _debugSurvivalLabel.Text =
            $"[偵錯 F4] 生存速率  [{(ic ? "戰鬥" : "休息")}]{flags}\n" +
            $"體力:  {Bar(s.Stamina, CharacterState.MaxStamina)} {staminaRate:+0.0;-0.0}/s{TimeUntil(staminaRate < 0 ? s.Stamina : CharacterState.MaxStamina - s.Stamina, MathF.Abs(staminaRate))}\n" +
            $"精力:  {Bar(s.MentalEnergy, CharacterState.MaxMentalEnergy)} {mentalRate:+0.0;-0.0}/s\n" +
            $"心情:  {Bar(s.Mood, CharacterState.MaxMood)}  (→{CharacterState.MoodDefaultValue:F0})\n" +
            $"體溫:  {s.BodyTemperature:F1}°C  env={s.AmbientTemperature:F0}°C  {tempDir:+0.0;-0.0}/s\n" +
            $"       低溫<{CharacterState.HypothermiaThreshold}°C{TimeUntil(tempToHypo, 1f)}  中暑>{CharacterState.HeatstrokeThreshold}°C{TimeUntil(tempToHeat, 1f)}\n" +
            $"口渴:  {Bar(s.Thirst, CharacterState.MaxThirst)} -{thirstDrain:F3}/s{TimeUntil(s.Thirst, thirstDrain)}  危:{CharacterState.ThirstCriticalThreshold}\n" +
            $"飢餓:  {Bar(s.Hunger, CharacterState.MaxHunger)} -{CharacterState.HungerDrainPerSec:F3}/s{TimeUntil(s.Hunger, CharacterState.HungerDrainPerSec)}\n" +
            $"氧氣:  {Bar(s.Oxygen, CharacterState.MaxOxygen)} {(s.IsOxygenDeprived ? $"-{CharacterState.OxygenDrainPerSec:F0}/s窒息{CharacterState.OxygenCriticalDamage:F0}dmg/s" : $"+{CharacterState.OxygenRegenPerSec:F0}/s回充")}\n" +
            $"氧殘:  {TimeUntil(s.Oxygen, s.IsOxygenDeprived ? CharacterState.OxygenDrainPerSec : 0f)}";
    }

    // ── 快照完整性偵錯（F5 取樣 / F6 回滾比對）──────────────────────────
    private void DebugSnapTake()
    {
        const int Radius = 5;
        var pp = _player.Position;

        SnapshotManager.TakeSnapshot(pp, Radius, _player, _enemies, _world3d);

        _snapBefore = new SnapCompare(
            _player.Hp,
            _enemies.Enemies.Where(e => e.IsAlive).Select(e => e.Id).ToArray(),
            _enemies.Enemies.Where(e => e.IsAlive).Select(e => e.Hp).ToArray(),
            _world3d.TypeAt(pp.X, pp.Y + 1)  // 腳下那格
        );

        GD.Print($"[Snap F5] Anchor @ ({pp.X},{pp.Y}) r={Radius}  HP={_player.Hp:F1}" +
                 $"  enemies={_snapBefore.EnemyIds.Length}" +
                 $"  tileUnder={_snapBefore.TileUnderPlayer}" +
                 $"  stackDepth={SnapshotManager.StackDepth}");
    }

    private void DebugSnapRollback()
    {
        if (_snapBefore == null)
        {
            GD.Print("[Snap F6] 尚未 F5 取樣，請先按 F5");
            return;
        }

        SnapshotManager.ApplyLatest(_player, _enemies, _world3d, _runner);

        float newHp   = _player.Hp;
        var pp        = _player.Position;
        var tileAfter = _world3d.TypeAt(pp.X, pp.Y + 1);
        bool hpOk     = MathF.Abs(newHp - _snapBefore.PlayerHp) < 0.5f;
        bool tileOk   = tileAfter == _snapBefore.TileUnderPlayer;

        GD.Print($"[Snap F6] Rollback 完成  HP: {_snapBefore.PlayerHp:F1}→{newHp:F1} {(hpOk ? "✓" : "✗MISMATCH")}");
        GD.Print($"          腳下材質: {_snapBefore.TileUnderPlayer}→{tileAfter} {(tileOk ? "✓" : "✗MISMATCH")}");

        int matched = 0, missing = 0;
        for (int i = 0; i < _snapBefore.EnemyIds.Length; i++)
        {
            int id  = _snapBefore.EnemyIds[i];
            float expectedHp = _snapBefore.EnemyHps[i];
            var enemy = _enemies.Enemies.FirstOrDefault(e => e.Id == id);
            if (enemy == null) { missing++; continue; }
            bool ok = MathF.Abs(enemy.Hp - expectedHp) < 0.5f;
            if (!ok)
                GD.Print($"          敵人 Id={id}: 預期 HP={expectedHp:F1} 實際 HP={enemy.Hp:F1} ✗");
            else
                matched++;
        }
        GD.Print($"          敵人: {matched}/{_snapBefore.EnemyIds.Length} 吻合, {missing} 消失");

        _snapBefore = null;
    }

    // ── 生存數值 HUD ──────────────────────────────────────────────────
    private void BuildSurvivalHUD(CanvasLayer hud)
    {
        const float startX = 10f;
        const float startY = -248f;
        const float rowH   = 13f;
        const float lblW   = 38f;
        const float barW   = 74f;
        const float barH   = 6f;
        const float valW   = 36f;

        var barDefs = new (string Name, Color Fill)[]
        {
            ("體力", new Color(1.00f, 0.60f, 0.22f)),
            ("精力", new Color(0.65f, 0.35f, 1.00f)),
            ("心情", new Color(1.00f, 0.55f, 0.65f)),
            ("口渴", new Color(0.28f, 0.78f, 1.00f)),
            ("飢餓", new Color(0.60f, 0.88f, 0.22f)),
            ("氧氣", new Color(0.45f, 0.65f, 1.00f)),
        };

        _survivalBarFills   = new Panel[barDefs.Length];
        _survivalFillStyles = new StyleBoxFlat[barDefs.Length];
        _survivalValLabels  = new Label[barDefs.Length];

        for (int i = 0; i < barDefs.Length; i++)
        {
            float y = startY + i * rowH;

            var nameLbl = new Label();
            nameLbl.AnchorTop = nameLbl.AnchorBottom = 1f;
            nameLbl.Position  = new Vector2(startX, y);
            nameLbl.Size      = new Vector2(lblW, rowH);
            nameLbl.Text      = barDefs[i].Name;
            nameLbl.AddThemeFontSizeOverride("font_size", 10);
            nameLbl.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.60f));
            hud.AddChild(nameLbl);

            var bgStyle = new StyleBoxFlat { BgColor = new Color(0.10f, 0.10f, 0.16f) };
            var barBg   = new Panel();
            barBg.AnchorTop = barBg.AnchorBottom = 1f;
            barBg.Position  = new Vector2(startX + lblW + 2f, y + (rowH - barH) * 0.5f);
            barBg.Size      = new Vector2(barW, barH);
            barBg.AddThemeStyleboxOverride("panel", bgStyle);
            hud.AddChild(barBg);

            var fillStyle = new StyleBoxFlat { BgColor = barDefs[i].Fill };
            _survivalFillStyles[i] = fillStyle;
            var fill = new Panel();
            fill.Position = Vector2.Zero;
            fill.Size     = new Vector2(barW, barH);
            fill.AddThemeStyleboxOverride("panel", fillStyle);
            barBg.AddChild(fill);
            _survivalBarFills[i] = fill;

            var valLbl = new Label();
            valLbl.AnchorTop = valLbl.AnchorBottom = 1f;
            valLbl.Position  = new Vector2(startX + lblW + 2f + barW + 3f, y);
            valLbl.Size      = new Vector2(valW, rowH);
            valLbl.HorizontalAlignment = HorizontalAlignment.Right;
            valLbl.AddThemeFontSizeOverride("font_size", 10);
            valLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
            hud.AddChild(valLbl);
            _survivalValLabels[i] = valLbl;
        }

        float tempY = startY + barDefs.Length * rowH;
        var tempName = new Label();
        tempName.AnchorTop = tempName.AnchorBottom = 1f;
        tempName.Position  = new Vector2(startX, tempY);
        tempName.Size      = new Vector2(lblW, rowH);
        tempName.Text      = "體溫";
        tempName.AddThemeFontSizeOverride("font_size", 10);
        tempName.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.60f));
        hud.AddChild(tempName);

        _bodyTempLabel = new Label();
        _bodyTempLabel.AnchorTop = _bodyTempLabel.AnchorBottom = 1f;
        _bodyTempLabel.Position  = new Vector2(startX + lblW + 2f, tempY);
        _bodyTempLabel.Size      = new Vector2(84f, rowH);
        _bodyTempLabel.AddThemeFontSizeOverride("font_size", 10);
        _bodyTempLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.80f, 0.75f));
        hud.AddChild(_bodyTempLabel);
    }

    private void RefreshSurvivalHUD()
    {
        var s = _player.State;
        const float barW = 74f;

        var data = new (float V, float Max, bool Danger)[]
        {
            (s.Stamina,      CharacterState.MaxStamina,      s.IsStaminaDepleted),
            (s.MentalEnergy, CharacterState.MaxMentalEnergy, s.IsMentalEnergyDepleted),
            (s.Mood,         CharacterState.MaxMood,         s.IsInsane),
            (s.Thirst,       CharacterState.MaxThirst,       s.IsDehydrated),
            (s.Hunger,       CharacterState.MaxHunger,       s.IsStarving),
            (s.Oxygen,       CharacterState.MaxOxygen,       s.IsSuffocating),
        };

        for (int i = 0; i < data.Length; i++)
        {
            float ratio = Math.Clamp(data[i].V / data[i].Max, 0f, 1f);
            _survivalBarFills[i].Size = _survivalBarFills[i].Size with { X = barW * ratio };
            _survivalValLabels[i].Text = $"{data[i].V:F0}";
            _survivalValLabels[i].AddThemeColorOverride("font_color",
                data[i].Danger ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.65f, 0.65f, 0.70f));
        }

        float t = s.BodyTemperature;
        Color tempCol = s.IsHypothermic ? new Color(0.40f, 0.65f, 1.00f)
                      : s.IsHeatstroke  ? new Color(1.00f, 0.40f, 0.20f)
                      :                   new Color(0.80f, 0.80f, 0.75f);
        _bodyTempLabel.Text = $"{t:F1}°C";
        _bodyTempLabel.AddThemeColorOverride("font_color", tempCol);
    }

    // ── 角色數值面板 ──────────────────────────────────────────────────
    private void BuildStatsPanel(CanvasLayer hud)
    {
        var bgStyle = new StyleBoxFlat
        {
            BgColor           = new Color(0.06f, 0.06f, 0.10f, 0.96f),
            BorderWidthTop    = 1, BorderWidthBottom = 1,
            BorderWidthLeft   = 1, BorderWidthRight  = 1,
            BorderColor       = new Color(0.35f, 0.40f, 0.55f),
            ContentMarginLeft  = 10f, ContentMarginRight  = 10f,
            ContentMarginTop   = 8f,  ContentMarginBottom = 8f,
        };
        _statsPanel = new PanelContainer();
        _statsPanel.AnchorLeft = _statsPanel.AnchorRight  = 0f;
        _statsPanel.AnchorTop  = _statsPanel.AnchorBottom = 0f;
        _statsPanel.Position   = new Vector2(8f, 8f);
        _statsPanel.AddThemeStyleboxOverride("panel", bgStyle);
        _statsPanel.Visible    = false;
        hud.AddChild(_statsPanel);

        _statsPanelContent = new Label();
        _statsPanelContent.AddThemeFontSizeOverride("font_size", 11);
        _statsPanelContent.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.90f));
        _statsPanel.AddChild(_statsPanelContent);
    }

    private void RefreshStatsPanel()
    {
        var st = _player.Stats;
        var eq = _player.Equipment;
        string wn  = eq.WeaponId    != ItemId.None ? ItemRegistry.Get(eq.WeaponId).DisplayName    : "─";
        string an  = eq.ArmorId     != ItemId.None ? ItemRegistry.Get(eq.ArmorId).DisplayName     : "─";
        string acn = eq.AccessoryId != ItemId.None ? ItemRegistry.Get(eq.AccessoryId).DisplayName : "─";

        _statsPanelContent.Text =
            "═══ 角色數值 [C 關閉] ═══\n" +
            "[生命/法力]\n" +
            $"  HP {st.MaxHpBase:F0}  MP {st.MaxMpBase + eq.TotalMpBonus:F0}（裝備+{eq.TotalMpBonus:F0}）\n" +
            $"  HP回復 {st.HpRegenRate:F1}/s  MP回復 {st.MpRegenRate:F1}/s\n" +
            "[戰鬥]\n" +
            $"  力量 {st.Power:F0}  物傷加成 {st.PhysicalDmgPct*100:F0}%\n" +
            $"  爆率 {st.CritRate*100:F0}%  爆傷倍率 ×{st.CritDmgMult:F1}\n" +
            $"  吸血 {st.Lifesteal*100:F0}%  反傷 {st.Thorns:F0}\n" +
            "[防禦]\n" +
            $"  基礎防禦 {st.BaseDefense:F0}  裝備防禦 +{eq.TotalDefFlat:F0}\n" +
            $"  減傷 {st.DamageReduction*100:F0}%  抗暴 {st.AntiCrit*100:F0}%\n" +
            "[機動]\n" +
            $"  移速 ×{st.MoveSpeedMult:F2}  攻速 ×{st.AtkSpeedMult:F2}\n" +
            $"  命中 {st.HitRate*100:F0}%  閃避 {st.DodgeRate*100:F0}%\n" +
            "[裝備]\n" +
            $"  武器：{wn}  防具：{an}  飾品：{acn}\n" +
            "[天賦]\n" +
            $"  體魄 {st.TalentConstitution}  肌力 {st.TalentStrength}  耐力 {st.TalentEndurance}\n" +
            $"  敏捷 {st.TalentAgility}  智慧 {st.TalentWisdom}  魅力 {st.TalentCharisma}  幸運 {st.TalentLuck}";
    }

    private void ToggleStatsPanel()
    {
        _statsPanelOpen = !_statsPanelOpen;
        _statsPanel.Visible = _statsPanelOpen;
        if (_statsPanelOpen) RefreshStatsPanel();
    }

    // ── 物品欄面板 ────────────────────────────────────────────────────

    private void BuildInventoryPanel(CanvasLayer hud)
    {
        const float slotW = 38f, slotH = 38f, gapX = 3f, gapY = 3f;
        const float padX  = 8f,  padY  = 8f;
        const int   cols  = 5,   rows  = 6;

        float innerW  = cols * slotW + (cols - 1) * gapX;
        float panelW  = innerW + 2 * padX;

        // 熱鍵欄佔 2 列（HotbarSize=10, cols=5），背包佔 4 列
        float row0Y   = padY + 20f + 14f;           // 熱鍵欄 row 0（標題 + 熱鍵標籤之後）
        float row1Y   = row0Y + slotH + gapY;        // 熱鍵欄 row 1
        float bagLblY = row1Y + slotH + gapY + 2f;  // 背包標籤
        float row2Y   = bagLblY + 14f;              // 背包 row 0

        float[] rowY  = new float[rows];
        rowY[0] = row0Y;
        rowY[1] = row1Y;
        rowY[2] = row2Y;
        for (int r = 3; r < rows; r++)
            rowY[r] = row2Y + (r - 2) * (slotH + gapY);

        float panelH = rowY[rows - 1] + slotH + padY;

        var bg = new StyleBoxFlat
        {
            BgColor                = new Color(0.05f, 0.05f, 0.10f, 0.92f),
            CornerRadiusTopLeft    = 4, CornerRadiusTopRight    = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor            = new Color(0.35f, 0.35f, 0.55f),
        };

        _inventoryPanel = new Panel();
        _inventoryPanel.AnchorLeft = _inventoryPanel.AnchorRight  = 0.5f;
        _inventoryPanel.AnchorTop  = _inventoryPanel.AnchorBottom = 0f;
        _inventoryPanel.Position   = new Vector2(-panelW - 5f, 20f);
        _inventoryPanel.Size       = new Vector2(panelW, panelH);
        _inventoryPanel.AddThemeStyleboxOverride("panel", bg);
        _inventoryPanel.Visible    = false;
        hud.AddChild(_inventoryPanel);

        var titleLbl = new Label();
        titleLbl.Position = new Vector2(padX, padY);
        titleLbl.Text     = "物品欄  [I 關閉]";
        titleLbl.AddThemeFontSizeOverride("font_size", 12);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.80f, 0.80f, 0.95f));
        _inventoryPanel.AddChild(titleLbl);

        var hotLbl = new Label();
        hotLbl.Position = new Vector2(padX, padY + 20f);
        hotLbl.Text     = "— 熱鍵欄 —";
        hotLbl.AddThemeFontSizeOverride("font_size", 10);
        hotLbl.AddThemeColorOverride("font_color", new Color(0.60f, 0.75f, 0.60f));
        _inventoryPanel.AddChild(hotLbl);

        var bagLbl = new Label();
        bagLbl.Position = new Vector2(padX, bagLblY);
        bagLbl.Text     = "— 背包 —";
        bagLbl.AddThemeFontSizeOverride("font_size", 10);
        bagLbl.AddThemeColorOverride("font_color", new Color(0.60f, 0.60f, 0.75f));
        _inventoryPanel.AddChild(bagLbl);

        int totalSlots = Inventory.TotalSize;
        _invSlotPanels = new Panel[totalSlots];
        _invSlotStyles = new StyleBoxFlat[totalSlots];
        _invIconStyles = new StyleBoxFlat[totalSlots];
        _invSlotCounts = new Label[totalSlots];

        for (int i = 0; i < totalSlots; i++)
        {
            int row = i / cols;
            int col = i % cols;

            var slotStyle = new StyleBoxFlat
            {
                BgColor      = new Color(0.10f, 0.10f, 0.15f),
                BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderColor  = new Color(0.30f, 0.30f, 0.40f),
            };
            _invSlotStyles[i] = slotStyle;

            var slot = new Panel();
            slot.Position = new Vector2(padX + col * (slotW + gapX), rowY[row]);
            slot.Size     = new Vector2(slotW, slotH);
            slot.AddThemeStyleboxOverride("panel", slotStyle);
            _inventoryPanel.AddChild(slot);
            _invSlotPanels[i] = slot;

            var iconStyle = new StyleBoxFlat { BgColor = new Color(0.10f, 0.10f, 0.12f) };
            _invIconStyles[i] = iconStyle;
            var icon = new Panel();
            icon.Position    = new Vector2(5f, 5f);
            icon.Size        = new Vector2(28f, 28f);
            icon.MouseFilter = Control.MouseFilterEnum.Ignore;
            icon.AddThemeStyleboxOverride("panel", iconStyle);
            slot.AddChild(icon);

            var cntLbl = new Label();
            cntLbl.Position = new Vector2(18f, 26f);
            cntLbl.Size     = new Vector2(18f, 12f);
            cntLbl.HorizontalAlignment = HorizontalAlignment.Right;
            cntLbl.AddThemeFontSizeOverride("font_size", 9);
            cntLbl.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.85f));
            cntLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
            slot.AddChild(cntLbl);
            _invSlotCounts[i] = cntLbl;

            int idx = i;
            slot.MouseEntered += () =>
            {
                var s = _player.Inventory.Slots[idx];
                if (!s.IsEmpty) ShowFloatTooltip(ItemRegistry.Get(s.ItemId).DisplayName);
            };
            slot.MouseExited  += () => HideFloatTooltip();
        }

    }

    private void RefreshInventoryPanel()
    {
        for (int i = 0; i < Inventory.TotalSize; i++)
        {
            var stack  = _player.Inventory.Slots[i];
            bool active = i < Inventory.HotbarSize && i == _player.Inventory.ActiveHotbarIndex;

            _invSlotStyles[i].BorderColor =
                active ? new Color(0.95f, 0.80f, 0.20f) : new Color(0.30f, 0.30f, 0.40f);

            if (stack.IsEmpty)
            {
                _invIconStyles[i].BgColor  = new Color(0.10f, 0.10f, 0.12f);
                _invSlotCounts[i].Text     = "";
            }
            else
            {
                _invIconStyles[i].BgColor  = GetItemIconColor(stack.ItemId);
                _invSlotCounts[i].Text     = stack.Count > 1 ? $"×{stack.Count}" : "";
            }
        }
    }

    private void ToggleInventoryPanel()
    {
        _inventoryOpen          = !_inventoryOpen;
        _inventoryPanel.Visible = _inventoryOpen;
        if (!_inventoryOpen)
        {
            _dragSrcSlot = -1;
            if (_dragFloatIcon != null) _dragFloatIcon.Visible = false;
        }
        if (_inventoryOpen) RefreshInventoryPanel();
    }

    // ── 裝備欄面板 ────────────────────────────────────────────────────

    private void BuildEquipPanel(CanvasLayer hud)
    {
        const float padX   = 8f,  padY   = 8f;
        const float iconS  = 38f;
        const float gapY   = 6f;
        const float typeW  = 36f, nameW  = 90f, midGap = 4f;
        const float titleH = 20f;
        float rowH   = iconS + gapY;
        float panelW = padX + typeW + midGap + iconS + midGap + nameW + padX;
        float panelH = padY + titleH + 3 * rowH + padY;

        var bg = new StyleBoxFlat
        {
            BgColor                = new Color(0.05f, 0.08f, 0.10f, 0.92f),
            CornerRadiusTopLeft    = 4, CornerRadiusTopRight    = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor            = new Color(0.30f, 0.50f, 0.30f),
        };

        _equipPanel = new Panel();
        _equipPanel.AnchorLeft = _equipPanel.AnchorRight  = 1f;
        _equipPanel.AnchorTop  = _equipPanel.AnchorBottom = 0f;
        _equipPanel.Position   = new Vector2(-(panelW + 8f), 20f);
        _equipPanel.Size       = new Vector2(panelW, panelH);
        _equipPanel.AddThemeStyleboxOverride("panel", bg);
        _equipPanel.Visible    = true;
        hud.AddChild(_equipPanel);
        _equipPanelOpen = true;

        var titleLbl = new Label();
        titleLbl.Position = new Vector2(padX, padY);
        titleLbl.Text     = "裝備欄  [X 關閉]";
        titleLbl.AddThemeFontSizeOverride("font_size", 12);
        titleLbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.90f, 0.75f));
        _equipPanel.AddChild(titleLbl);

        var slotLabels = new[] { "武器", "防具", "飾品" };
        for (int i = 0; i < 3; i++)
        {
            float rowY = padY + titleH + i * rowH;

            var typeLabel = new Label();
            typeLabel.Position = new Vector2(padX, rowY + 11f);
            typeLabel.Size     = new Vector2(typeW, 16f);
            typeLabel.Text     = slotLabels[i];
            typeLabel.AddThemeFontSizeOverride("font_size", 11);
            typeLabel.AddThemeColorOverride("font_color", new Color(0.60f, 0.70f, 0.65f));
            _equipPanel.AddChild(typeLabel);

            var slotStyle = new StyleBoxFlat
            {
                BgColor      = new Color(0.08f, 0.08f, 0.12f),
                BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderColor  = new Color(0.30f, 0.45f, 0.30f),
            };
            _eqSlotStyles[i] = slotStyle;

            var slotPanel = new Panel();
            slotPanel.Position = new Vector2(padX + typeW + midGap, rowY);
            slotPanel.Size     = new Vector2(iconS, iconS);
            slotPanel.AddThemeStyleboxOverride("panel", slotStyle);
            _equipPanel.AddChild(slotPanel);

            var iconStyle = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.10f) };
            _eqIconStyles[i] = iconStyle;
            var icon = new Panel();
            icon.Position    = new Vector2(4f, 4f);
            icon.Size        = new Vector2(30f, 30f);
            icon.MouseFilter = Control.MouseFilterEnum.Ignore;
            icon.AddThemeStyleboxOverride("panel", iconStyle);
            slotPanel.AddChild(icon);

            var nameLabel = new Label();
            nameLabel.Position    = new Vector2(padX + typeW + midGap + iconS + midGap, rowY + 10f);
            nameLabel.Size        = new Vector2(nameW, 20f);
            nameLabel.Text        = "─";
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.78f, 0.82f));
            nameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            _equipPanel.AddChild(nameLabel);
            _eqNameLabels[i] = nameLabel;

            int capturedI = i;
            slotPanel.MouseEntered += () =>
            {
                var eid = capturedI switch
                {
                    0 => _player.Equipment.WeaponId,
                    1 => _player.Equipment.ArmorId,
                    _ => _player.Equipment.AccessoryId,
                };
                ShowFloatTooltip(eid != ItemId.None
                    ? $"點擊卸下：{ItemRegistry.Get(eid).DisplayName}" : "");
            };
            slotPanel.MouseExited  += () => HideFloatTooltip();
            slotPanel.GuiInput     += (InputEvent ev) =>
            {
                if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    var stype = capturedI switch
                    {
                        0 => EquipmentSlotType.Weapon,
                        1 => EquipmentSlotType.Armor,
                        _ => EquipmentSlotType.Accessory,
                    };
                    _player.Equipment.TryUnequip(_player.Inventory, stype);
                    RefreshEquipPanel();
                    RefreshInventoryPanel();
                }
            };
        }
        RefreshEquipPanel();
    }

    private void RefreshEquipPanel()
    {
        var eq       = _player.Equipment;
        var equipped = new[] { eq.WeaponId, eq.ArmorId, eq.AccessoryId };
        for (int i = 0; i < 3; i++)
        {
            if (equipped[i] == ItemId.None)
            {
                _eqIconStyles[i].BgColor = new Color(0.08f, 0.08f, 0.10f);
                _eqNameLabels[i].Text    = "─";
            }
            else
            {
                _eqIconStyles[i].BgColor = GetItemIconColor(equipped[i]);
                _eqNameLabels[i].Text    = ItemRegistry.Get(equipped[i]).DisplayName;
            }
        }
    }

    private void ToggleEquipPanel()
    {
        _equipPanelOpen     = !_equipPanelOpen;
        _equipPanel.Visible = _equipPanelOpen;
        if (_equipPanelOpen) RefreshEquipPanel();
    }

    // ── 傷害數字 ──────────────────────────────────────────────────────
    private void SpawnDmgNum(GridPos pos, float amount, bool isPlayer)
    {
        if (!ShowDamageNumbers) return;

        int slot = -1;
        for (int i = 0; i < DmgPoolSize; i++)
        {
            if (!_dmgPool[i].Active) { slot = i; break; }
        }
        if (slot < 0) return;

        ref var d = ref _dmgPool[slot];
        d.WorldPx   = new Vector2(pos.X + 0.5f, pos.Y + 0.5f); // 世界單位（tile 中心）
        d.Timer     = DmgNumDuration;
        d.RiseY     = 0f;
        d.Active    = true;
        d.BaseColor = isPlayer ? new Color(1f, 0.30f, 0.30f) : new Color(1f, 0.85f, 0.15f);

        d.Lbl.Text = $"{amount:F0}";
        d.Lbl.AddThemeColorOverride("font_color", d.BaseColor);
        d.Lbl.Visible = true;
    }
}
