namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.VM;
using SkillCreator.World;

public partial class AbilityEditorUI : Control
{
    private enum Mode { Idle, TotemSelected, SlotSelected }

    // 從圓球列表返回時發出
    [Signal] public delegate void BackPressedEventHandler();

    // 技能欄位（共 MaxSlots 個槽位）
    public SpellLoadout Loadout { get; } = new();

    // ── 狀態 ──────────────────────────────────────────────────────
    private readonly SpellArray[] _spells =
        new SpellArray[SpellLoadout.MaxSlots];
    private int _activeEditorSlot = 0;
    private SpellArray _spell => _spells[_activeEditorSlot];

    private Mode      _mode         = Mode.Idle;
    private TotemData? _pendingTotem;
    private int       _activeSlot   = -1;
    private const int MaxSlots      = 8;
    // 由 Main.cs 每幀更新，用於刻印庫境界門檻顯示
    public  int       PlayerLevel   { get; set; } = 1;

    // 調色盤拖放狀態
    private BlockType? _palDragType;
    private Vector2    _palDragStart;

    // ── UI 節點引用 ───────────────────────────────────────────────
    private LineEdit      _nameInput      = null!;
    private HBoxContainer _slotsRow       = null!;
    private Label         _apValue        = null!;
    private ProgressBar   _apBar          = null!;
    private Label         _mpValue        = null!;
    private Label         _status         = null!;
    private ScriptCanvas  _canvas         = null!;
    // Header 槽位按鈕（1-10），供 OpenSlot 同步視覺選中狀態
    private readonly Button[] _headerSlotBtns = new Button[SpellLoadout.MaxSlots];
    // Header 主/被動切換（供 RefreshAll 同步）
    private Button _activeModeBtn  = null!;
    private Button _passiveModeBtn = null!;
    // 左側面板：0=圖騰 1=積木 2=刻印
    private int           _activeLeftTab  = 1;
    private VBoxContainer _leftContent    = null!;
    private readonly Button[] _leftTabBtns = new Button[3];

    // ── 初始化 ────────────────────────────────────────────────────
    public override void _Ready()
    {
        for (int i = 0; i < _spells.Length; i++) _spells[i] = new SpellArray();

        // 讀取上次存檔
        var totemMap   = TotemLibrary.AllTotems.ToDictionary(t => t.Id);
        var engraveMap = TotemLibrary.AllEngravings.ToDictionary(e => e.Id);
        var (saved, savedActive, savedPassive) = SaveSystem.Load(totemMap, engraveMap);
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            if (saved[i] is { } s)
            {
                _spells[i] = s;
                Loadout.SetSlot(i, s);
            }
        }
        foreach (var p in savedPassive)
            Loadout.AddPassive(p);
        _activeEditorSlot = savedActive;

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUI();
        RefreshAll();
    }

    // ════════════════════════════════════════════════════════════
    //  UI 建構
    // ════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        // 深色背景
        var bg = new ColorRect { Color = new Color(0.11f, 0.11f, 0.14f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        BuildHeader(root);

        var body = new HBoxContainer();
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 2);
        root.AddChild(body);

        BuildLeftPanel(body);
        BuildCenter(body);
        BuildRightPanel(body);
    }

    // ── Header ────────────────────────────────────────────────────

    private void BuildHeader(VBoxContainer root)
    {
        var bar = Tinted(new Color(0.16f, 0.16f, 0.20f));
        bar.CustomMinimumSize = new Vector2(0, 50);
        root.AddChild(bar);

        var row = new HBoxContainer();
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.AddThemeConstantOverride("separation", 10);
        bar.AddChild(row);

        // ← 返回按鈕
        var backBtn = Btn("← 返回", new Color(0.20f, 0.20f, 0.28f));
        backBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        backBtn.CustomMinimumSize = new Vector2(80, 30);
        backBtn.Pressed += () => EmitSignal(SignalName.BackPressed);
        row.AddChild(backBtn);

        HSpacer(row, 4);

        row.AddChild(Lbl("法陣名稱：", vcenter: true));

        _nameInput = new LineEdit();
        _nameInput.PlaceholderText = "輸入法陣名稱（必填）";
        _nameInput.CustomMinimumSize = new Vector2(180, 34);
        _nameInput.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _nameInput.TextChanged += t => _spell.Name = t;
        row.AddChild(_nameInput);

        HSpacer(row, 8);
        row.AddChild(Lbl("主被動：", vcenter: true));
        var passiveGrp = new ButtonGroup();

        _activeModeBtn = Btn("主動", new Color(0.22f, 0.30f, 0.22f));
        _activeModeBtn.ToggleMode = true; _activeModeBtn.ButtonGroup = passiveGrp;
        _activeModeBtn.ButtonPressed = !_spell.IsPassive;
        _activeModeBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _activeModeBtn.Toggled += on => { if (on) _spell.IsPassive = false; };
        row.AddChild(_activeModeBtn);

        _passiveModeBtn = Btn("被動", new Color(0.30f, 0.22f, 0.22f));
        _passiveModeBtn.ToggleMode = true; _passiveModeBtn.ButtonGroup = passiveGrp;
        _passiveModeBtn.ButtonPressed = _spell.IsPassive;
        _passiveModeBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _passiveModeBtn.Toggled += on => { if (on) _spell.IsPassive = true; };
        row.AddChild(_passiveModeBtn);

        HSpacer(row, 12);
        row.AddChild(Lbl("發動：", vcenter: true));

        var actGrp = new ButtonGroup();
        foreach (var (atype, albl) in new (AbilityActivationType, string)[]
        {
            (AbilityActivationType.Instant,   "即時"),
            (AbilityActivationType.Declare,   "宣告"),
            (AbilityActivationType.Sustained, "持續"),
        })
        {
            var btn = Btn(albl, new Color(0.22f, 0.22f, 0.30f));
            btn.ToggleMode = true; btn.ButtonGroup = actGrp;
            btn.ButtonPressed = _spell.ActivationType == atype;
            btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            var t2 = atype;
            btn.Toggled += on => { if (on) { _spell.ActivationType = t2; RefreshCost(); } };
            row.AddChild(btn);
        }

        HSpacer(row, 12);
        row.AddChild(Lbl("施放方式：", vcenter: true));

        var ctGrp = new ButtonGroup();
        foreach (var (ct, ctLbl, ctColor) in new (ContainerType, string, Color)[]
        {
            // 直接施放：非容器，玩家本體直接執行
            (ContainerType.DirectCast,     "直接施放", new Color(0.22f, 0.28f, 0.22f)),
            // 容器類型：裝載效果的實體
            (ContainerType.Projectile,     "投射物",   new Color(0.18f, 0.26f, 0.36f)),
            (ContainerType.SummonMinion,   "精靈",     new Color(0.28f, 0.22f, 0.35f)),
            (ContainerType.SummonTurret,   "砲台",     new Color(0.28f, 0.22f, 0.35f)),
            (ContainerType.SummonGuardian, "護衛",     new Color(0.28f, 0.22f, 0.35f)),
        })
        {
            var btn = Btn(ctLbl, ctColor);
            btn.ToggleMode = true; btn.ButtonGroup = ctGrp;
            btn.ButtonPressed = _spell.Container == ct;
            btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            var cap = ct;
            btn.Toggled += on => { if (on) _spell.Container = cap; };
            row.AddChild(btn);
        }

        HSpacer(row, 16);
        row.AddChild(Lbl("槽位：", vcenter: true));

        var slotGrp = new ButtonGroup();
        for (int si = 0; si < SpellLoadout.MaxSlots; si++)
        {
            var btn = Btn((si + 1).ToString(), new Color(0.26f, 0.22f, 0.36f));
            btn.ToggleMode = true; btn.ButtonGroup = slotGrp;
            btn.ButtonPressed = si == _activeEditorSlot;
            btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            btn.CustomMinimumSize = new Vector2(32, 30);
            _headerSlotBtns[si] = btn;
            var captSi = si;
            btn.Toggled += on => { if (on) SelectEditorSlot(captSi); };
            row.AddChild(btn);
        }

        var flex = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(flex);

        _status = new Label();
        _status.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.4f));
        _status.AddThemeFontSizeOverride("font_size", 12);
        _status.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_status);

        HSpacer(row, 12);
    }

    // ── 左側面板（母分類 tabs：圖騰 / 積木 / 刻印） ──────────────

    private void BuildLeftPanel(HBoxContainer body)
    {
        var panel = Tinted(new Color(0.13f, 0.13f, 0.17f));
        panel.CustomMinimumSize = new Vector2(185, 0);
        body.AddChild(panel);

        var outer = new VBoxContainer();
        outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("separation", 0);
        panel.AddChild(outer);

        // ── 母分類 Tab Row ──
        var tabRow = new HBoxContainer();
        tabRow.CustomMinimumSize = new Vector2(0, 30);
        tabRow.AddThemeConstantOverride("separation", 0);
        outer.AddChild(tabRow);

        var tabGrp  = new ButtonGroup();
        var tabNames = new[] { "圖騰", "積木", "刻印" };
        for (int ti = 0; ti < 3; ti++)
        {
            var captTi = ti;
            var tabBtn = new Button { Text = tabNames[ti], ToggleMode = true, ButtonGroup = tabGrp };
            tabBtn.ButtonPressed = (ti == _activeLeftTab);
            tabBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            tabBtn.CustomMinimumSize = new Vector2(0, 30);
            tabBtn.AddThemeFontSizeOverride("font_size", 11);

            var normSt  = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.18f) };
            normSt.CornerRadiusTopLeft = normSt.CornerRadiusTopRight = 0;
            var hovSt   = new StyleBoxFlat { BgColor = new Color(0.17f, 0.17f, 0.24f) };
            var actSt   = new StyleBoxFlat { BgColor = new Color(0.20f, 0.22f, 0.32f) };
            actSt.BorderWidthBottom = 2;
            actSt.BorderColor = new Color(0.48f, 0.68f, 1.0f);
            tabBtn.AddThemeStyleboxOverride("normal",     normSt);
            tabBtn.AddThemeStyleboxOverride("hover",      hovSt);
            tabBtn.AddThemeStyleboxOverride("pressed",    actSt);
            tabBtn.AddThemeStyleboxOverride("focus",      actSt);
            tabBtn.Toggled += on => { if (on) { _activeLeftTab = captTi; RebuildLeftContent(); } };
            tabRow.AddChild(tabBtn);
            _leftTabBtns[ti] = tabBtn;
        }

        outer.AddChild(new HSeparator());

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        outer.AddChild(scroll);

        _leftContent = new VBoxContainer();
        _leftContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _leftContent.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_leftContent);

        RebuildLeftContent();
    }

    // 根據 _activeLeftTab 填充左側可捲動內容區
    private void RebuildLeftContent()
    {
        if (_leftContent is null) return;
        foreach (var c in _leftContent.GetChildren().ToArray())
            c.QueueFree();

        VSpacer(_leftContent, 4);
        switch (_activeLeftTab)
        {
            case 0: BuildTotemContent();   break;
            case 1: BuildBlockContent();   break;
            case 2: BuildEngraveContent(); break;
        }
        VSpacer(_leftContent, 8);
    }

    // 圖騰庫（按 TotemType 分組）
    private void BuildTotemContent()
    {
        TotemType? lastType = null;
        foreach (var totem in TotemLibrary.AllTotems)
        {
            if (totem.Type != lastType)
            {
                var sep = new Label { Text = TotemTypeName(totem.Type) };
                sep.AddThemeColorOverride("font_color", TotemClr(totem.Type).Darkened(0.2f));
                sep.AddThemeFontSizeOverride("font_size", 11);
                _leftContent.AddChild(sep);
                lastType = totem.Type;
            }
            var t = totem;
            string lvTag = totem.RequiredPlayerLevel > 1 ? $" LV{totem.RequiredPlayerLevel}+" : "";
            var btn = Btn($"  {totem.DisplayName}{lvTag}  {totem.BaseAbilityPointCost}pt",
                          new Color(0.18f, 0.22f, 0.30f));
            btn.Alignment = HorizontalAlignment.Left;
            btn.CustomMinimumSize = new Vector2(0, 30);
            btn.AddThemeColorOverride("font_color", TotemClr(totem.Type));
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.Pressed += () => SelectTotem(t);
            _leftContent.AddChild(btn);
        }
    }

    // 積木庫（按功能分組，可拖放至畫布）
    private void BuildBlockContent()
    {
        var cats = new (string cat, BlockType[] bts)[]
        {
            ("── 呼叫 ──",     new[] { BlockType.InvokeTotem, BlockType.InvokeSpell }),
            ("── 控制流 ──",   new[] { BlockType.If, BlockType.Evaluate, BlockType.RepeatN,
                                       BlockType.RepeatWhile, BlockType.ForEachNearby,
                                       BlockType.Wait, BlockType.Sleep, BlockType.Die,
                                       BlockType.RandomChoice, BlockType.SequentialGate }),
            ("── 觸發條件 ──", new[] { BlockType.RisingEdge, BlockType.FallingEdge,
                                       BlockType.SinglePulse, BlockType.AlternateTrigger }),
            ("── 偵測 ──",     new[] { BlockType.DetectHpThreshold, BlockType.DetectMpThreshold,
                                       BlockType.DetectHitReceived, BlockType.DetectEntityEnter,
                                       BlockType.DetectProjectile, BlockType.DetectAttack,
                                       BlockType.DetectStatusChange }),
            ("── 發動模式 ──", new[] { BlockType.SetActivationInstant, BlockType.SetActivationDeclare,
                                       BlockType.SetActivationSustained }),
            ("── 效果標示 ──", new[] { BlockType.EffectLabel, BlockType.OnEffectStart,
                                       BlockType.OnEffectEnd }),
            ("── 執行時機 ──", new[] { BlockType.EndOfChain, BlockType.Discard }),
            ("── 變數 ──",     new[] { BlockType.SetVar, BlockType.GetVar, BlockType.Compare,
                                       BlockType.SetVarBool, BlockType.GetVarBool }),
            ("── 列表 ──",     new[] { BlockType.ListCreate, BlockType.ListAppend, BlockType.ListPop,
                                       BlockType.ListDequeue, BlockType.ListGet, BlockType.ListSet,
                                       BlockType.ListLength, BlockType.ListContains,
                                       BlockType.ListRemoveAt, BlockType.ListClear }),
            ("── 實體 ──",     new[] { BlockType.QueryNear, BlockType.QueryNearest,
                                       BlockType.GetEntityProp, BlockType.SetEntityProp }),
            ("── 廣播 ──",     new[] { BlockType.Broadcast, BlockType.BroadcastAndWait,
                                       BlockType.OnReceive }),
            ("── 計數器 ──",   new[] { BlockType.TaskCounterSet, BlockType.TaskCounterAdd,
                                       BlockType.TaskCounterGet, BlockType.TaskCounterOnReach,
                                       BlockType.TaskCounterReset }),
            ("── 統計 ──",     new[] { BlockType.GetBattleStat, BlockType.GetComboCount,
                                       BlockType.LoopcastIndex, BlockType.SuccessCount }),
            ("── 向量 ──",     new[] { BlockType.VecMake, BlockType.VecGetComp, BlockType.VecAdd,
                                       BlockType.VecSub, BlockType.VecScale, BlockType.VecNegate,
                                       BlockType.VecNorm, BlockType.VecLength, BlockType.VecDot,
                                       BlockType.VecCross, BlockType.VecFromEntity,
                                       BlockType.FocalPoint, BlockType.Raycast }),
            ("── 攔截 ──",     new[] { BlockType.DamageShield, BlockType.DeathGuard }),
            ("── 快照 ──",     new[] { BlockType.Anchor, BlockType.Rollback }),
        };

        foreach (var (cat, bts) in cats)
        {
            var catLbl = new Label { Text = cat };
            catLbl.AddThemeColorOverride("font_color", new Color(0.50f, 0.50f, 0.62f));
            catLbl.AddThemeFontSizeOverride("font_size", 11);
            _leftContent.AddChild(catLbl);

            foreach (var bt in bts)
            {
                var captBt = bt;
                var btn = Btn($"  {BlockTypeName(bt)}", new Color(0.14f, 0.18f, 0.26f));
                btn.Alignment = HorizontalAlignment.Left;
                btn.CustomMinimumSize = new Vector2(0, 24);
                btn.AddThemeFontSizeOverride("font_size", 11);
                btn.AddThemeColorOverride("font_color", BlockTypeColor(bt));
                btn.Pressed += () =>
                {
                    if (!BlockDrag.Active)
                    {
                        _spell.Blocks.Add(ScratchCanvas.MakeDefaultBlock(captBt));
                        SyncCanvas();
                    }
                };
                btn.GuiInput += @event =>
                {
                    if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                    {
                        _palDragType  = captBt;
                        _palDragStart = mb.GlobalPosition;
                    }
                };
                _leftContent.AddChild(btn);
            }
        }
    }

    // 刻印庫（按 EngraveColor 分組，含境界門檻鎖定）
    private void BuildEngraveContent()
    {
        EngraveColor? lastClr = null;
        foreach (var eng in TotemLibrary.AllEngravings)
        {
            if (eng.Color != lastClr)
            {
                var clrLbl = new Label { Text = ColorGroupName(eng.Color) };
                clrLbl.AddThemeColorOverride("font_color", EngraveClr(eng.Color));
                clrLbl.AddThemeFontSizeOverride("font_size", 11);
                _leftContent.AddChild(clrLbl);
                lastClr = eng.Color;
            }
            var e    = eng;
            bool locked  = eng.RequiredPlayerLevel > PlayerLevel;
            string cost  = eng.IsRestriction ? $"+{eng.BaseCost}pt" : $"{eng.BaseCost}pt";
            string lock_ = locked ? $"  🔒{PlayerController.GetTierName(eng.RequiredPlayerLevel)}" : "";
            var btn = Btn($"  {eng.DisplayName}  {cost}{lock_}", new Color(0.18f, 0.20f, 0.20f));
            btn.Alignment = HorizontalAlignment.Left;
            btn.CustomMinimumSize = new Vector2(0, 26);
            btn.Disabled = locked;
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.AddThemeColorOverride("font_color",
                locked ? new Color(0.40f, 0.40f, 0.42f) : EngraveClr(eng.Color));
            btn.Pressed += () => AttachEngrave(e);
            _leftContent.AddChild(btn);
        }
    }

    // ── 中央插槽區 ─────────────────────────────────────────────────

    private void BuildCenter(HBoxContainer body)
    {
        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        body.AddChild(vbox);

        VSpacer(vbox, 8);

        var hdr = new HBoxContainer();
        HSpacer(hdr, 10);
        hdr.AddChild(SectionLbl("插槽排列（由左至右依序執行）"));
        vbox.AddChild(hdr);

        // 可橫向捲動的插槽列
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        vbox.AddChild(scroll);

        var slotRow = new HBoxContainer();
        slotRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        slotRow.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(slotRow);

        HSpacer(slotRow, 10);

        _slotsRow = new HBoxContainer();
        _slotsRow.AddThemeConstantOverride("separation", 8);
        slotRow.AddChild(_slotsRow);

        // 新增插槽按鈕（固定，不在 RefreshSlots 中重建）
        var addBtn = Btn("＋\n插槽", new Color(0.15f, 0.28f, 0.15f));
        addBtn.CustomMinimumSize = new Vector2(70, 180);
        addBtn.Pressed += AddSlot;
        slotRow.AddChild(addBtn);

        HSpacer(slotRow, 10);

        var hint = new Label();
        hint.Text = "① 點左側圖騰  ② 點插槽放入  ③ 選中插槽後點刻印附加  ④ 為插槽命名後可在積木中引用";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(hint);

        // ── 積木序列（玩家設計）──────────────────────────────────
        VSpacer(vbox, 4);
        vbox.AddChild(new HSeparator());

        var bhdr = new HBoxContainer();
        bhdr.AddThemeConstantOverride("separation", 4);
        HSpacer(bhdr, 8);
        bhdr.AddChild(SectionLbl("▶ 積木序列（玩家設計邏輯）"));
        var bflex = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bhdr.AddChild(bflex);

        var autoFillBtn = Btn("自動填入", new Color(0.12f, 0.25f, 0.18f));
        autoFillBtn.CustomMinimumSize = new Vector2(72, 22);
        autoFillBtn.Pressed += AutoFillBlocks;
        bhdr.AddChild(autoFillBtn);

        var bClrBtn = Btn("清除", new Color(0.30f, 0.12f, 0.12f));
        bClrBtn.CustomMinimumSize = new Vector2(44, 22);
        bClrBtn.Pressed += () => { _spell.Blocks.Clear(); SyncCanvas(); };
        bhdr.AddChild(bClrBtn);
        HSpacer(bhdr, 8);
        vbox.AddChild(bhdr);

        _canvas = new ScriptCanvas();
        _canvas.CustomMinimumSize = new Vector2(0, 180);
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
        _canvas.Changed += () => RefreshCost();
        vbox.AddChild(_canvas);

        VSpacer(vbox, 6);
    }

    // ── 右側統計面板 ────────────────────────────────────────────────

    private void BuildRightPanel(HBoxContainer body)
    {
        var panel = Tinted(new Color(0.14f, 0.14f, 0.18f));
        panel.CustomMinimumSize = new Vector2(175, 0);
        body.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 8);
        panel.AddChild(vbox);

        VSpacer(vbox, 12);
        vbox.AddChild(SectionLbl("  能力點統計"));
        VSpacer(vbox, 2);
        vbox.AddChild(new HSeparator());
        VSpacer(vbox, 4);

        // AP 數字
        var apRow = new HBoxContainer();
        HSpacer(apRow, 8);
        apRow.AddChild(Lbl("消耗："));
        _apValue = new Label();
        _apValue.Text = "0 點";
        _apValue.AddThemeFontSizeOverride("font_size", 22);
        apRow.AddChild(_apValue);
        vbox.AddChild(apRow);

        // AP 上限說明
        var capRow = new HBoxContainer();
        HSpacer(capRow, 8);
        var capLbl = Lbl($"上限（LV{PlayerLevel}）：{LvCap(PlayerLevel)} 點");
        capLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.55f));
        capRow.AddChild(capLbl);
        vbox.AddChild(capRow);

        // AP 進度條
        _apBar = new ProgressBar();
        _apBar.MinValue = 0;
        _apBar.MaxValue = LvCap(PlayerLevel);
        _apBar.Value = 0;
        _apBar.CustomMinimumSize = new Vector2(0, 14);
        var barMargin = new MarginContainer();
        barMargin.AddThemeConstantOverride("margin_left", 8);
        barMargin.AddThemeConstantOverride("margin_right", 8);
        barMargin.AddChild(_apBar);
        vbox.AddChild(barMargin);

        VSpacer(vbox, 4);
        vbox.AddChild(new HSeparator());
        VSpacer(vbox, 4);

        // MP
        var mpRow = new HBoxContainer();
        HSpacer(mpRow, 8);
        mpRow.AddChild(Lbl("MP 消耗："));
        _mpValue = new Label();
        _mpValue.Text = "0";
        _mpValue.AddThemeFontSizeOverride("font_size", 18);
        mpRow.AddChild(_mpValue);
        vbox.AddChild(mpRow);

        // 彈性空白 → 按鈕推到底
        var flex = new Control();
        flex.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(flex);

        // 儲存按鈕
        var saveMargin = new MarginContainer();
        saveMargin.AddThemeConstantOverride("margin_left", 8);
        saveMargin.AddThemeConstantOverride("margin_right", 8);
        saveMargin.AddThemeConstantOverride("margin_bottom", 12);
        var saveBtn = Btn("儲存法陣", new Color(0.15f, 0.35f, 0.55f));
        saveBtn.CustomMinimumSize = new Vector2(0, 38);
        saveBtn.Pressed += SaveSpell;
        saveMargin.AddChild(saveBtn);
        vbox.AddChild(saveMargin);
    }

    // ════════════════════════════════════════════════════════════
    //  刷新
    // ════════════════════════════════════════════════════════════

    private void RefreshAll()
    {
        if (_nameInput != null) _nameInput.Text = _spell.Name;
        if (_activeModeBtn  != null) _activeModeBtn.ButtonPressed  = !_spell.IsPassive;
        if (_passiveModeBtn != null) _passiveModeBtn.ButtonPressed = _spell.IsPassive;
        RefreshSlots();
        RefreshCost();
        RefreshStatus();
        SyncCanvas();
    }

    private void RefreshSlots()
    {
        foreach (var child in _slotsRow.GetChildren().ToArray())
            child.QueueFree();

        for (int i = 0; i < _spell.Slots.Count; i++)
            _slotsRow.AddChild(BuildSlot(i));
    }

    private void RefreshCost()
    {
        int ap   = AbilityPointCalculator.CalculateTotalCost(_spell);
        float mp = AbilityPointCalculator.CalculateMpCost(_spell);
        bool over = AbilityPointCalculator.ExceedsLevelCap(_spell, PlayerLevel);
        int cap = LvCap(PlayerLevel);

        _apValue.Text = $"{ap} 點";
        _apValue.AddThemeColorOverride("font_color", over
            ? new Color(1f, 0.3f, 0.3f)
            : new Color(0.9f, 0.9f, 0.9f));

        _apBar.MaxValue = cap;
        _apBar.Value = Math.Min(ap, cap);

        _mpValue.Text = $"{mp:F0}";
    }

    private void RefreshStatus()
    {
        _status.Text = _mode switch
        {
            Mode.TotemSelected => $"已選擇：{_pendingTotem?.DisplayName}　→ 點擊空插槽放入",
            Mode.SlotSelected  => $"插槽 {_activeSlot + 1} 選中　→ 點擊刻印附加 ／ 再點一次取消選中",
            _                  => "",
        };
    }

    // ════════════════════════════════════════════════════════════
    //  插槽 Widget 建構（每次 RefreshSlots 重建）
    // ════════════════════════════════════════════════════════════

    private Control BuildSlot(int idx)
    {
        var slot = _spell.Slots[idx];
        bool selected = _mode == Mode.SlotSelected && _activeSlot == idx;
        bool canDrop = _mode == Mode.TotemSelected && slot.IsEmpty;

        var bg = new Color(
            selected ? 0.18f : canDrop ? 0.14f : 0.16f,
            selected ? 0.26f : canDrop ? 0.28f : 0.16f,
            selected ? 0.38f : canDrop ? 0.14f : 0.20f);

        var panel = Tinted(bg);
        panel.CustomMinimumSize = new Vector2(145, 220);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 3);
        panel.AddChild(vbox);

        VSpacer(vbox, 4);

        // 插槽編號
        var num = Lbl($"插槽 {idx + 1}", 11, new Color(0.45f, 0.45f, 0.5f));
        num.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(num);

        // 插槽命名（供積木 InvokeTotem 引用）
        var nameLE = new LineEdit();
        nameLE.PlaceholderText = "命名（積木引用）";
        nameLE.Text = slot.Name;
        nameLE.CustomMinimumSize = new Vector2(0, 20);
        nameLE.AddThemeFontSizeOverride("font_size", 10);
        var captSlot = idx;
        nameLE.TextChanged += text =>
        {
            if (captSlot < _spell.Slots.Count)
                _spell.Slots[captSlot].Name = text;
            SyncCanvas();
        };
        vbox.AddChild(nameLE);

        if (slot.IsEmpty)
        {
            var empty = Lbl(canDrop ? "點擊\n放入圖騰" : "空",
                align: HorizontalAlignment.Center,
                clr: canDrop ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.3f, 0.3f, 0.35f));
            empty.VerticalAlignment = VerticalAlignment.Center;
            empty.SizeFlagsVertical = SizeFlags.ExpandFill;
            vbox.AddChild(empty);

            // 整個 panel 可點擊
            OverlayClick(panel, () => ClickSlot(idx));
        }
        else
        {
            // 圖騰名稱 + 類型
            var tName = Lbl(slot.Totem!.DisplayName, 15, TotemClr(slot.Totem.Type));
            tName.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(tName);

            var tType = Lbl(TotemTypeTag(slot.Totem.Type),
                11, TotemClr(slot.Totem.Type));
            tType.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(tType);

            vbox.AddChild(new HSeparator());

            // 附掛刻印列表
            for (int ei = 0; ei < slot.LocalEngravings.Count; ei++)
                vbox.AddChild(BuildEngraveRow(idx, ei));

            // 選中時顯示提示
            if (selected)
            {
                var hint = Lbl("← 點刻印附加", 11, new Color(0.5f, 0.85f, 0.5f));
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                vbox.AddChild(hint);
            }

            var flex = new Control();
            flex.SizeFlagsVertical = SizeFlags.ExpandFill;
            vbox.AddChild(flex);

            // 底部按鈕列
            var btmRow = new HBoxContainer();
            btmRow.AddThemeConstantOverride("separation", 2);
            HSpacer(btmRow, 2);

            var selBtn = Btn(selected ? "✓ 選中" : "選中",
                selected ? new Color(0.18f, 0.32f, 0.50f) : new Color(0.18f, 0.20f, 0.26f));
            selBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            selBtn.CustomMinimumSize = new Vector2(0, 26);
            selBtn.Pressed += () => ClickSlot(idx);
            btmRow.AddChild(selBtn);

            var rmBtn = Btn("✕", new Color(0.38f, 0.16f, 0.16f));
            rmBtn.CustomMinimumSize = new Vector2(28, 26);
            rmBtn.Pressed += () => RemoveSlotTotem(idx);
            btmRow.AddChild(rmBtn);

            HSpacer(btmRow, 2);
            vbox.AddChild(btmRow);
        }

        VSpacer(vbox, 4);
        return panel;
    }

    private Control BuildEngraveRow(int slotIdx, int engIdx)
    {
        var eng = _spell.Slots[slotIdx].LocalEngravings[engIdx];
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 1);

        // 刻印名稱 + 移除
        var nameRow = new HBoxContainer();
        var nameLbl = Lbl(eng.DisplayName, 12, EngraveClr(eng.Color));
        nameLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameRow.AddChild(nameLbl);

        var rmBtn = new Button();
        rmBtn.Text = "✕";
        rmBtn.CustomMinimumSize = new Vector2(20, 18);
        rmBtn.AddThemeFontSizeOverride("font_size", 10);
        rmBtn.Pressed += () => RemoveEngrave(slotIdx, engIdx);
        nameRow.AddChild(rmBtn);
        vbox.AddChild(nameRow);

        // 投入點數 +/-
        var ptRow = new HBoxContainer();
        ptRow.AddThemeConstantOverride("separation", 2);

        var minusBtn = new Button();
        minusBtn.Text = "−";
        minusBtn.CustomMinimumSize = new Vector2(20, 18);
        minusBtn.Pressed += () => AdjustPoints(slotIdx, engIdx, -1);
        ptRow.AddChild(minusBtn);

        var ptLbl = Lbl(eng.PointsInvested.ToString(), 11);
        ptLbl.CustomMinimumSize = new Vector2(20, 0);
        ptLbl.HorizontalAlignment = HorizontalAlignment.Center;
        ptRow.AddChild(ptLbl);

        var plusBtn = new Button();
        plusBtn.Text = "＋";
        plusBtn.CustomMinimumSize = new Vector2(20, 18);
        plusBtn.Pressed += () => AdjustPoints(slotIdx, engIdx, +1);
        ptRow.AddChild(plusBtn);

        // 效果值
        float fx = eng.CalculateEffect();
        var fxLbl = Lbl(eng.ScalingType == ScalingType.Hyperbolic
            ? $"→{fx:P0}" : $"→{fx:F0}", 11, new Color(0.6f, 0.9f, 0.6f));
        ptRow.AddChild(fxLbl);

        vbox.AddChild(ptRow);
        return vbox;
    }

    // ════════════════════════════════════════════════════════════
    //  互動事件
    // ════════════════════════════════════════════════════════════

    private void SelectTotem(TotemData totem)
    {
        _pendingTotem = totem;
        _mode = Mode.TotemSelected;
        _activeSlot = -1;
        RefreshAll();
    }

    private void ClickSlot(int idx)
    {
        var slot = _spell.Slots[idx];

        if (slot.IsEmpty && _mode == Mode.TotemSelected && _pendingTotem != null)
        {
            slot.Totem = _pendingTotem;
            _pendingTotem = null;
            _mode = Mode.SlotSelected;
            _activeSlot = idx;
        }
        else if (!slot.IsEmpty)
        {
            // 已選中 → 取消；未選中 → 選中
            if (_mode == Mode.SlotSelected && _activeSlot == idx)
            {
                _mode = Mode.Idle;
                _activeSlot = -1;
            }
            else
            {
                _mode = Mode.SlotSelected;
                _activeSlot = idx;
            }
        }

        RefreshAll();
    }

    private void AttachEngrave(EngraveData template)
    {
        if (_mode != Mode.SlotSelected || _activeSlot < 0) return;
        var slot = _spell.Slots[_activeSlot];
        if (slot.IsEmpty) return;

        slot.LocalEngravings.Add(new EngraveData
        {
            Id = template.Id, DisplayName = template.DisplayName,
            Color = template.Color, ScalingType = template.ScalingType,
            ScalingCoefficient = template.ScalingCoefficient,
            BaseEffect = template.BaseEffect, BaseCost = template.BaseCost,
            IsGlobal = template.IsGlobal, IsRestriction = template.IsRestriction,
            PointsInvested = 0,
        });
        RefreshAll();
    }

    private void RemoveSlotTotem(int idx)
    {
        var slot = _spell.Slots[idx];
        slot.Totem = null;
        slot.LocalEngravings.Clear();
        if (_activeSlot == idx) { _mode = Mode.Idle; _activeSlot = -1; }
        RefreshAll();
    }

    private void RemoveEngrave(int slotIdx, int engIdx)
    {
        _spell.Slots[slotIdx].LocalEngravings.RemoveAt(engIdx);
        RefreshAll();
    }

    private void AdjustPoints(int slotIdx, int engIdx, int delta)
    {
        var eng = _spell.Slots[slotIdx].LocalEngravings[engIdx];
        eng.PointsInvested = Math.Max(0, eng.PointsInvested + delta);
        RefreshAll();
    }

    private void AddSlot()
    {
        if (_spell.Slots.Count >= MaxSlots) return;
        _spell.Slots.Add(new SpellSlot());
        RefreshAll();
    }

    // ── 積木編輯器 ───────────────────────────────────────────────

    private void AutoFillBlocks()
    {
        _spell.Blocks.Clear();
        _spell.Blocks.AddRange(BlockAutoGenerator.Generate(_spell));
        SyncCanvas();
    }

    private void SyncCanvas()
    {
        if (_canvas is null) return;
        _canvas.SyncFrom(_spell.Blocks, GetSlotOptions);
    }


    private List<(string display, string refKey)> GetSlotOptions()
    {
        var result = new List<(string, string)>();
        for (int i = 0; i < _spell.Slots.Count; i++)
        {
            var s = _spell.Slots[i];
            if (s.IsEmpty) continue;
            string refKey  = string.IsNullOrEmpty(s.Name) ? $"slot_{i}" : s.Name;
            string display = $"{s.Totem!.DisplayName}（{refKey}）";
            result.Add((display, refKey));
        }
        return result;
    }


    // 積木名稱統一委託給 ScratchCanvas._descs，避免重複定義與英文 fallback
    private static string BlockTypeName(BlockType type) => ScratchCanvas.BlockName(type);

    // 顏色同樣委託給 ScratchCanvas._descs，與積木頭部色塊保持一致
    private static Color BlockTypeColor(BlockType type) => ScratchCanvas.BlockColor(type);

    private void SaveSpell()
    {
        // ── 收集所有驗證錯誤 ──
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(_spell.Name))
            errors.Add("• 請填寫法陣名稱（必填）");

        // 主動技能必須有發動方式（此處用 ActivationType 有無 None 值判斷）
        // ActivationType 預設為 Instant，無 None 值，故無需額外驗證

        if (AbilityPointCalculator.ExceedsLevelCap(_spell, PlayerLevel))
        {
            int ap  = AbilityPointCalculator.CalculateTotalCost(_spell);
            int cap = LvCap(PlayerLevel);
            errors.Add($"• 能力點 {ap} 超過境界上限 {cap}");
        }

        if (errors.Count > 0)
        {
            var dlg = new AcceptDialog
            {
                Title      = "⚠ 儲存失敗",
                DialogText = string.Join("\n", errors),
            };
            AddChild(dlg);
            dlg.PopupCentered(new Vector2I(300, 0));
            dlg.Confirmed    += () => dlg.QueueFree();
            dlg.Canceled     += () => dlg.QueueFree();
            dlg.CloseRequested += () => dlg.QueueFree();
            return;
        }

        // ── 儲存 ──
        Loadout.SetSlot(_activeEditorSlot, _spell);
        var allSpells = Enumerable.Range(0, SpellLoadout.MaxSlots)
                                  .Select(i => Loadout.GetSlot(i))
                                  .ToArray();
        SaveSystem.Save(allSpells, _activeEditorSlot, Loadout.PassiveSpells);

        GD.Print($"[儲存] 槽位 {_activeEditorSlot + 1} ← 法陣「{_spell.Name}」  " +
                 $"主被動：{(_spell.IsPassive ? "被動" : "主動")}  " +
                 $"AP：{AbilityPointCalculator.CalculateTotalCost(_spell)}  " +
                 $"MP：{AbilityPointCalculator.CalculateMpCost(_spell):F0}");
        _status.Text = $"✓ 槽位 {_activeEditorSlot + 1}「{_spell.Name}」（{(_spell.IsPassive ? "被動" : "主動")}）已存";
    }

    private void SelectEditorSlot(int i)
    {
        _activeEditorSlot = i;
        _mode       = Mode.Idle;
        _activeSlot = -1;
        RefreshAll();
    }

    // 供 SpellListUI 呼叫，從圓球列表導覽到指定槽位
    public void OpenSlot(int index)
    {
        if (index < 0 || index >= _spells.Length) return;
        _activeEditorSlot = index;
        _mode       = Mode.Idle;
        _activeSlot = -1;
        if (_headerSlotBtns[index] is { } b) b.ButtonPressed = true;
        RefreshAll();
    }

    // ════════════════════════════════════════════════════════════
    //  Helper 工廠
    // ════════════════════════════════════════════════════════════

    // 有色背景的 Panel（子節點自行用 FullRect anchor 填滿）
    // ── 調色盤拖放輸入處理 ────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        if (_palDragType == null && !BlockDrag.Active) return;

        if (@event is InputEventMouseMotion mm)
        {
            // 移動超過 4px 才啟動拖放（避免普通點擊誤觸）
            if (_palDragType != null && !BlockDrag.Active)
            {
                if (mm.GlobalPosition.DistanceTo(_palDragStart) > 4f)
                {
                    BlockDrag.BeginNew(ScratchCanvas.MakeDefaultBlock(_palDragType.Value));
                    _palDragType = null;
                }
            }
            return;
        }

        if (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // 放開滑鼠：清除待拖狀態（ScriptCanvas 的 _Input 先於此處處理真正的拖放落點）
            _palDragType = null;
        }
    }

    private static Panel Tinted(Color c)
    {
        var p = new Panel();
        var s = new StyleBoxFlat { BgColor = c };
        p.AddThemeStyleboxOverride("panel", s);
        return p;
    }

    // 按鈕，帶 StyleBox 顏色
    private static Button Btn(string text, Color bg)
    {
        var b = new Button { Text = text };
        var s = new StyleBoxFlat { BgColor = bg };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 4;
        var h = new StyleBoxFlat { BgColor = bg.Lightened(0.15f) };
        h.CornerRadiusTopLeft = h.CornerRadiusTopRight =
        h.CornerRadiusBottomLeft = h.CornerRadiusBottomRight = 4;
        b.AddThemeStyleboxOverride("normal", s);
        b.AddThemeStyleboxOverride("hover", h);
        b.AddThemeStyleboxOverride("pressed", s);
        b.AddThemeStyleboxOverride("focus", s);
        return b;
    }

    // 文字標籤
    private static Label Lbl(string text, int size = 13,
        Color? clr = null, HorizontalAlignment align = HorizontalAlignment.Left,
        bool vcenter = false)
    {
        var l = new Label { Text = text };
        if (size != 13) l.AddThemeFontSizeOverride("font_size", size);
        if (clr.HasValue) l.AddThemeColorOverride("font_color", clr.Value);
        l.HorizontalAlignment = align;
        if (vcenter) l.VerticalAlignment = VerticalAlignment.Center;
        return l;
    }

    private static Label SectionLbl(string text)
        => Lbl(text, 13, new Color(0.8f, 0.8f, 0.45f));

    private static void VSpacer(Control parent, int h)
    {
        var s = new Control { CustomMinimumSize = new Vector2(0, h) };
        parent.AddChild(s);
    }

    private static void HSpacer(Control parent, int w)
    {
        var s = new Control { CustomMinimumSize = new Vector2(w, 0) };
        parent.AddChild(s);
    }

    // 透明按鈕蓋在 panel 上，捕捉點擊
    private static void OverlayClick(Control parent, Action onPress)
    {
        var btn = new Button();
        btn.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        btn.Flat = true;
        btn.Modulate = new Color(1, 1, 1, 0); // 完全透明
        btn.Pressed += onPress;
        parent.AddChild(btn);
    }

    // ── 顏色映射 ──────────────────────────────────────────────────

    private static Color TotemClr(TotemType t) => t switch
    {
        TotemType.Area         => new Color(0.55f, 0.80f, 1.0f),  // 藍
        TotemType.Technique    => new Color(1.0f,  0.72f, 0.35f), // 橙
        TotemType.Projectile   => new Color(0.90f, 0.60f, 1.0f),  // 淺紫
        TotemType.Morph        => new Color(0.40f, 0.90f, 0.80f), // 青
        TotemType.Displacement => new Color(0.65f, 0.95f, 0.30f), // 黃綠
        TotemType.Summon       => new Color(1.0f,  0.75f, 0.20f), // 金
        TotemType.Domain       => new Color(0.85f, 0.40f, 1.0f),  // 紫
        _                      => new Color(0.8f,  0.8f,  0.8f),
    };

    private static string TotemTypeTag(TotemType t) => t switch
    {
        TotemType.Area         => "[範圍]",
        TotemType.Technique    => "[武技]",
        TotemType.Projectile   => "[投射]",
        TotemType.Morph        => "[變幻]",
        TotemType.Displacement => "[位移]",
        TotemType.Summon       => "[召喚]",
        TotemType.Domain       => "[領域]",
        _                      => "[?]",
    };

    private static string TotemTypeName(TotemType t) => t switch
    {
        TotemType.Area         => "── 範圍圖騰 ──",
        TotemType.Technique    => "── 武技圖騰 ──",
        TotemType.Projectile   => "── 投射物圖騰 ──",
        TotemType.Morph        => "── 變幻圖騰 ──",
        TotemType.Displacement => "── 位移圖騰 ──",
        TotemType.Summon       => "── 召喚圖騰 ──",
        TotemType.Domain       => "── 領域圖騰 ──",
        _                      => "──",
    };

    private static Color EngraveClr(EngraveColor c) => c switch
    {
        EngraveColor.White     => new Color(0.93f, 0.93f, 0.93f),
        EngraveColor.Red       => new Color(1.0f,  0.38f, 0.38f),
        EngraveColor.Green     => new Color(0.38f, 0.88f, 0.48f),
        EngraveColor.Blue      => new Color(0.38f, 0.60f, 1.0f),
        EngraveColor.Yellow    => new Color(1.0f,  0.88f, 0.28f),
        EngraveColor.Orange    => new Color(1.0f,  0.58f, 0.18f),
        EngraveColor.Purple    => new Color(0.80f, 0.38f, 1.0f),
        EngraveColor.Black     => new Color(0.68f, 0.48f, 0.88f),
        EngraveColor.Elemental => new Color(1.0f,  0.78f, 0.25f), // 金色
        EngraveColor.Law       => new Color(0.78f, 0.78f, 0.95f), // 銀紫
        _                      => new Color(1, 1, 1),
    };

    private static string ColorGroupName(EngraveColor c) => c switch
    {
        EngraveColor.White     => "  ── 白（傷害）",
        EngraveColor.Orange    => "  ── 橙（控制）",
        EngraveColor.Blue      => "  ── 藍（改造）",
        EngraveColor.Red       => "  ── 紅（侵略）",
        EngraveColor.Green     => "  ── 綠（輔助）",
        EngraveColor.Purple    => "  ── 紫（額外操作）",
        EngraveColor.Yellow    => "  ── 黃（限制）",
        EngraveColor.Black     => "  ── 黑（邏輯）",
        EngraveColor.Elemental => "  ── 屬性（元素）",
        EngraveColor.Law       => "  ── 法則（高等）",
        _                      => "  ──",
    };

    private static int LvCap(int lv) => lv switch { < 20 => 50, < 30 => 120, < 50 => 250, < 70 => 500, _ => 900 };
}
