namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.VM;
using SkillCreator.World;

public partial class AbilityEditorUI : Control
{
    private enum Mode { Idle, TotemSelected, SlotSelected }

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

    // ── UI 節點引用 ───────────────────────────────────────────────
    private LineEdit      _nameInput = null!;
    private HBoxContainer _slotsRow  = null!;
    private Label         _apValue   = null!;
    private ProgressBar   _apBar     = null!;
    private Label         _mpValue   = null!;
    private Label         _status    = null!;
    private ScratchCanvas _canvas = null!;

    // ── 初始化 ────────────────────────────────────────────────────
    public override void _Ready()
    {
        for (int i = 0; i < _spells.Length; i++) _spells[i] = new SpellArray();

        // 讀取上次存檔
        var totemMap   = TotemLibrary.AllTotems.ToDictionary(t => t.Id);
        var engraveMap = TotemLibrary.AllEngravings.ToDictionary(e => e.Id);
        var (saved, savedActive) = SaveSystem.Load(totemMap, engraveMap);
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            if (saved[i] is { } s)
            {
                _spells[i] = s;
                Loadout.SetSlot(i, s);
            }
        }
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

        HSpacer(row, 12);

        row.AddChild(Lbl("法陣名稱：", vcenter: true));

        _nameInput = new LineEdit();
        _nameInput.PlaceholderText = "輸入法陣名稱（必填）";
        _nameInput.CustomMinimumSize = new Vector2(200, 34);
        _nameInput.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _nameInput.TextChanged += t => _spell.Name = t;
        row.AddChild(_nameInput);

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
        row.AddChild(Lbl("容器：", vcenter: true));

        var ctGrp = new ButtonGroup();
        foreach (var (ct, ctLbl) in new (ContainerType, string)[]
        {
            (ContainerType.PlayerBody,  "本體"),
            (ContainerType.Projectile,  "投射物"),
            (ContainerType.Contact,     "接觸"),
        })
        {
            var btn = Btn(ctLbl, new Color(0.22f, 0.28f, 0.22f));
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
            btn.ButtonPressed = si == 0;
            btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            btn.CustomMinimumSize = new Vector2(28, 30);
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

    // ── 左側面板（圖騰庫 + 刻印庫） ──────────────────────────────

    private void BuildLeftPanel(HBoxContainer body)
    {
        var panel = Tinted(new Color(0.14f, 0.14f, 0.18f));
        panel.CustomMinimumSize = new Vector2(175, 0);
        body.AddChild(panel);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        panel.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(vbox);

        VSpacer(vbox, 8);
        vbox.AddChild(SectionLbl("▶ 圖騰庫"));

        TotemType? lastType = null;
        foreach (var totem in TotemLibrary.AllTotems)
        {
            if (totem.Type != lastType)
            {
                var sep = new Label();
                sep.Text = TotemTypeName(totem.Type);
                sep.AddThemeColorOverride("font_color", TotemClr(totem.Type).Darkened(0.2f));
                sep.AddThemeFontSizeOverride("font_size", 11);
                vbox.AddChild(sep);
                lastType = totem.Type;
            }

            var t = totem;
            string lvTag = totem.RequiredPlayerLevel > 1 ? $" LV{totem.RequiredPlayerLevel}+" : "";
            var btn = Btn(
                $"  {totem.DisplayName}{lvTag}  {totem.BaseAbilityPointCost}pt",
                new Color(0.18f, 0.22f, 0.30f));
            btn.Alignment = HorizontalAlignment.Left;
            btn.CustomMinimumSize = new Vector2(0, 32);
            btn.AddThemeColorOverride("font_color", TotemClr(totem.Type));
            btn.Pressed += () => SelectTotem(t);
            vbox.AddChild(btn);
        }

        VSpacer(vbox, 6);
        vbox.AddChild(new HSeparator());
        VSpacer(vbox, 6);
        vbox.AddChild(SectionLbl("▶ 刻印庫"));

        EngraveColor? lastClr = null;
        foreach (var eng in TotemLibrary.AllEngravings)
        {
            if (eng.Color != lastClr)
            {
                var clrLbl = new Label();
                clrLbl.Text = ColorGroupName(eng.Color);
                clrLbl.AddThemeColorOverride("font_color", EngraveClr(eng.Color));
                clrLbl.AddThemeFontSizeOverride("font_size", 11);
                vbox.AddChild(clrLbl);
                lastClr = eng.Color;
            }
            var e = eng;
            bool locked = eng.RequiredPlayerLevel > PlayerLevel;
            string costTag  = eng.IsRestriction ? $"+{eng.BaseCost}pt" : $"{eng.BaseCost}pt";
            string lockTag  = locked
                ? $"  🔒{PlayerController.GetTierName(eng.RequiredPlayerLevel)}"
                : "";
            var btn = Btn($"  {eng.DisplayName}  {costTag}{lockTag}", new Color(0.18f, 0.20f, 0.20f));
            btn.Alignment = HorizontalAlignment.Left;
            btn.CustomMinimumSize = new Vector2(0, 26);
            btn.Disabled = locked;
            btn.AddThemeColorOverride("font_color",
                locked ? new Color(0.40f, 0.40f, 0.42f) : EngraveClr(eng.Color));
            btn.Pressed += () => AttachEngrave(e);
            vbox.AddChild(btn);
        }

        VSpacer(vbox, 6);
        vbox.AddChild(new HSeparator());
        VSpacer(vbox, 6);
        vbox.AddChild(SectionLbl("▶ 積木庫"));

        var blockLibCats = new (string cat, (string lbl, BlockType bt)[])[]
        {
            ("── 呼叫 ──", new[] {
                ("觸發圖騰",  BlockType.InvokeTotem),
                ("發動法陣",  BlockType.InvokeSpell),
            }),
            ("── 控制流 ──", new[] {
                ("If",        BlockType.If),
                ("Repeat×N",  BlockType.RepeatN),
                ("While",     BlockType.RepeatWhile),
                ("Wait",      BlockType.Wait),
                ("Random",    BlockType.RandomChoice),
                ("ForEach",   BlockType.ForEachNearby),
            }),
            ("── 邊緣觸發 ──", new[] {
                ("上升沿",    BlockType.RisingEdge),
                ("下降沿",    BlockType.FallingEdge),
                ("單次脈衝",  BlockType.SinglePulse),
            }),
            ("── 偵測 ──", new[] {
                ("HP%",       BlockType.DetectHpThreshold),
                ("MP%",       BlockType.DetectMpThreshold),
                ("偵測實體",  BlockType.DetectEntityEnter),
            }),
            ("── 變數 ──", new[] {
                ("設定",      BlockType.SetVar),
                ("讀取",      BlockType.GetVar),
                ("Compare",   BlockType.Compare),
                ("布林設",    BlockType.SetVarBool),
                ("布林讀",    BlockType.GetVarBool),
            }),
            ("── 實體 ──", new[] {
                ("查詢附近",  BlockType.QueryNear),
                ("讀屬性",    BlockType.GetEntityProp),
                ("寫屬性",    BlockType.SetEntityProp),
            }),
            ("── 廣播 ──", new[] {
                ("廣播",      BlockType.Broadcast),
                ("廣播等待",  BlockType.BroadcastAndWait),
                ("接收",      BlockType.OnReceive),
            }),
            ("── 計數器 ──", new[] {
                ("設定",      BlockType.TaskCounterSet),
                ("增加",      BlockType.TaskCounterAdd),
                ("到達",      BlockType.TaskCounterOnReach),
                ("歸零",      BlockType.TaskCounterReset),
            }),
        };

        foreach (var (cat, entries) in blockLibCats)
        {
            var catLbl = new Label { Text = cat };
            catLbl.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.65f));
            catLbl.AddThemeFontSizeOverride("font_size", 11);
            vbox.AddChild(catLbl);

            foreach (var (lbl, bt) in entries)
            {
                var captBt = bt;
                var btn = Btn($"  {BlockTypeName(bt)}", new Color(0.14f, 0.18f, 0.26f));
                btn.Alignment = HorizontalAlignment.Left;
                btn.CustomMinimumSize = new Vector2(0, 24);
                btn.AddThemeFontSizeOverride("font_size", 11);
                btn.AddThemeColorOverride("font_color", BlockTypeColor(bt));
                btn.Pressed += () => { _spell.Blocks.Add(ScratchCanvas.MakeDefaultBlock(captBt)); SyncCanvas(); };
                vbox.AddChild(btn);
            }
        }

        VSpacer(vbox, 8);
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

        _canvas = new ScratchCanvas();
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

            var tType = Lbl(slot.Totem.Type == TotemType.Trigger ? "[觸發]" : "[武技]",
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


    private static string BlockTypeName(BlockType type) => type switch
    {
        BlockType.InvokeTotem        => "使用技能",
        BlockType.InvokeSpell        => "施放其他法陣",
        BlockType.If                 => "如果",
        BlockType.RepeatN            => "重複 N 次",
        BlockType.RepeatWhile        => "條件成立，重複",
        BlockType.RandomChoice       => "隨機選擇",
        BlockType.ForEachNearby      => "對每個附近敵人",
        BlockType.Wait               => "等待",
        BlockType.RisingEdge         => "開始觸發",
        BlockType.FallingEdge        => "結束觸發",
        BlockType.SinglePulse        => "僅觸發一次",
        BlockType.SetVar             => "設定變數",
        BlockType.GetVar             => "讀取變數",
        BlockType.SetVarBool         => "設定布林",
        BlockType.GetVarBool         => "讀取布林",
        BlockType.Compare            => "比較數值",
        BlockType.QueryNear          => "查詢附近敵人",
        BlockType.GetEntityProp      => "讀取敵人屬性",
        BlockType.SetEntityProp      => "設定敵人屬性",
        BlockType.Broadcast          => "廣播訊號",
        BlockType.BroadcastAndWait   => "廣播訊號（等待）",
        BlockType.OnReceive          => "收到訊號時",
        BlockType.DetectHpThreshold  => "生命值低於 N%",
        BlockType.DetectMpThreshold  => "魔力值低於 N%",
        BlockType.DetectEntityEnter  => "偵測敵人進入範圍",
        BlockType.TaskCounterSet     => "計數器設定值",
        BlockType.TaskCounterAdd     => "計數器增加",
        BlockType.TaskCounterOnReach => "計數器到達時",
        BlockType.TaskCounterReset   => "計數器歸零",
        BlockType.EffectLabel        => "效果標記",
        _                            => type.ToString(),
    };

    private static Color BlockTypeColor(BlockType type) => type switch
    {
        BlockType.InvokeTotem or BlockType.InvokeSpell
                                     => new Color(1.0f,  0.72f, 0.35f),  // 橙
        BlockType.If or BlockType.RepeatN or BlockType.RepeatWhile or
        BlockType.RandomChoice or BlockType.ForEachNearby
                                     => new Color(0.65f, 0.95f, 0.30f),  // 黃綠
        BlockType.Wait               => new Color(0.38f, 0.88f, 0.48f),  // 綠
        BlockType.RisingEdge or BlockType.FallingEdge or BlockType.SinglePulse
                                     => new Color(0.38f, 0.88f, 0.88f),  // 青
        BlockType.SetVar or BlockType.GetVar or BlockType.SetVarBool or
        BlockType.GetVarBool or BlockType.Compare
                                     => new Color(1.0f,  0.88f, 0.28f),  // 黃
        BlockType.QueryNear or BlockType.GetEntityProp or BlockType.SetEntityProp
                                     => new Color(0.55f, 0.80f, 1.0f),   // 藍
        BlockType.Broadcast or BlockType.BroadcastAndWait or BlockType.OnReceive
                                     => new Color(0.80f, 0.38f, 1.0f),   // 紫
        BlockType.DetectHpThreshold or BlockType.DetectMpThreshold or
        BlockType.DetectEntityEnter  => new Color(1.0f,  0.42f, 0.42f),  // 紅
        BlockType.TaskCounterSet or BlockType.TaskCounterAdd or
        BlockType.TaskCounterOnReach or BlockType.TaskCounterReset
                                     => new Color(0.95f, 0.65f, 0.95f),  // 淡紫
        _                            => new Color(0.75f, 0.75f, 0.75f),
    };

    private void SaveSpell()
    {
        if (string.IsNullOrWhiteSpace(_spell.Name))
        {
            _status.Text = "⚠ 請先填寫法陣名稱！";
            return;
        }
        if (AbilityPointCalculator.ExceedsLevelCap(_spell, PlayerLevel))
        {
            _status.Text = "⚠ 能力點超過上限，無法儲存！";
            return;
        }
        Loadout.SetSlot(_activeEditorSlot, _spell);

        // 寫入磁碟
        var allSpells = Enumerable.Range(0, SpellLoadout.MaxSlots)
                                  .Select(i => Loadout.GetSlot(i))
                                  .ToArray();
        SaveSystem.Save(allSpells, _activeEditorSlot);

        GD.Print($"[儲存] 槽位 {_activeEditorSlot + 1} ← 法陣「{_spell.Name}」  " +
                 $"AP：{AbilityPointCalculator.CalculateTotalCost(_spell)}  " +
                 $"MP：{AbilityPointCalculator.CalculateMpCost(_spell):F0}  " +
                 $"容器：{_spell.Container}");
        _status.Text = $"✓ 槽位 {_activeEditorSlot + 1}「{_spell.Name}」已存　按 E 切回世界，1-5 切換槽位，空白鍵施放";
    }

    private void SelectEditorSlot(int i)
    {
        _activeEditorSlot = i;
        _mode       = Mode.Idle;
        _activeSlot = -1;
        RefreshAll();
    }

    // ════════════════════════════════════════════════════════════
    //  Helper 工廠
    // ════════════════════════════════════════════════════════════

    // 有色背景的 Panel（子節點自行用 FullRect anchor 填滿）
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
        TotemType.Trigger      => new Color(0.55f, 0.80f, 1.0f),  // 藍
        TotemType.Technique    => new Color(1.0f,  0.72f, 0.35f), // 橙
        TotemType.Morph        => new Color(0.40f, 0.90f, 0.80f), // 青
        TotemType.Displacement => new Color(0.65f, 0.95f, 0.30f), // 黃綠
        TotemType.Summon       => new Color(1.0f,  0.75f, 0.20f), // 金
        TotemType.Domain       => new Color(0.85f, 0.40f, 1.0f),  // 紫
        _                      => new Color(0.8f,  0.8f,  0.8f),
    };

    private static string TotemTypeName(TotemType t) => t switch
    {
        TotemType.Trigger      => "── 觸發圖騰 ──",
        TotemType.Technique    => "── 武技圖騰 ──",
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
