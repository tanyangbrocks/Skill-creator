namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem;
using SkillCreator.AbilitySystem.Data;

public partial class AbilityEditorUI : Control
{
    private enum Mode { Idle, TotemSelected, SlotSelected }

    // 最後一次儲存的法陣（供 Main.cs 施放用）
    public SpellArray? SavedSpell { get; private set; }

    // ── 狀態 ──────────────────────────────────────────────────────
    private readonly SpellArray _spell = new();
    private Mode _mode = Mode.Idle;
    private TotemData? _pendingTotem;
    private int _activeSlot = -1;
    private const int MaxSlots = 8;
    private const int PlayerLv = 1;

    // ── UI 節點引用 ───────────────────────────────────────────────
    private LineEdit _nameInput = null!;
    private HBoxContainer _slotsRow = null!;
    private Label _apValue = null!;
    private ProgressBar _apBar = null!;
    private Label _mpValue = null!;
    private Label _status = null!;

    // ── 初始化 ────────────────────────────────────────────────────
    public override void _Ready()
    {
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

        HSpacer(row, 20);
        row.AddChild(Lbl("發動類型：", vcenter: true));

        var grp = new ButtonGroup();
        foreach (var (type, label) in new (AbilityActivationType, string)[]
        {
            (AbilityActivationType.Instant,   "即時 ×0.8"),
            (AbilityActivationType.Declare,   "宣告 ×1.0"),
            (AbilityActivationType.Sustained, "持續 ×1.5"),
        })
        {
            var btn = Btn(label, new Color(0.22f, 0.22f, 0.30f));
            btn.ToggleMode = true;
            btn.ButtonGroup = grp;
            btn.ButtonPressed = _spell.ActivationType == type;
            btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            var t2 = type;
            btn.Toggled += on => { if (on) { _spell.ActivationType = t2; RefreshCost(); } };
            row.AddChild(btn);
        }

        // 彈性空白
        var flex = new Control();
        flex.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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

        foreach (var totem in TotemLibrary.AllTotems)
        {
            var t = totem;
            var btn = Btn(
                $"{totem.DisplayName}  [{(totem.Type == TotemType.Trigger ? "觸發" : "武技")}]  {totem.BaseAbilityPointCost}pt",
                new Color(0.18f, 0.22f, 0.30f));
            btn.Alignment = HorizontalAlignment.Left;
            btn.CustomMinimumSize = new Vector2(0, 38);
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
            var btn = Btn($"  {eng.DisplayName}  {eng.BaseCost}pt", new Color(0.18f, 0.20f, 0.20f));
            btn.Alignment = HorizontalAlignment.Left;
            btn.CustomMinimumSize = new Vector2(0, 28);
            btn.AddThemeColorOverride("font_color", EngraveClr(eng.Color));
            btn.Pressed += () => AttachEngrave(e);
            vbox.AddChild(btn);
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
        hint.Text = "① 點左側圖騰  ② 點插槽放入  ③ 選中插槽後點刻印附加";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(hint);

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
        var capLbl = Lbl($"上限（LV{PlayerLv}）：{LvCap(PlayerLv)} 點");
        capLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.55f));
        capRow.AddChild(capLbl);
        vbox.AddChild(capRow);

        // AP 進度條
        _apBar = new ProgressBar();
        _apBar.MinValue = 0;
        _apBar.MaxValue = LvCap(PlayerLv);
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
        RefreshSlots();
        RefreshCost();
        RefreshStatus();
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
        bool over = AbilityPointCalculator.ExceedsLevelCap(_spell, PlayerLv);
        int cap = LvCap(PlayerLv);

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
            IsGlobal = template.IsGlobal, PointsInvested = 0,
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

    private void SaveSpell()
    {
        if (string.IsNullOrWhiteSpace(_spell.Name))
        {
            _status.Text = "⚠ 請先填寫法陣名稱！";
            return;
        }
        if (AbilityPointCalculator.ExceedsLevelCap(_spell, PlayerLv))
        {
            _status.Text = "⚠ 能力點超過上限，無法儲存！";
            return;
        }
        SavedSpell = _spell;
        GD.Print($"[儲存] 法陣「{_spell.Name}」" +
                 $"AP：{AbilityPointCalculator.CalculateTotalCost(_spell)}  " +
                 $"MP：{AbilityPointCalculator.CalculateMpCost(_spell):F0}  " +
                 $"類型：{_spell.ActivationType}");
        _status.Text = $"✓ 「{_spell.Name}」已儲存　按 E 切回世界，空白鍵施放";
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

    private static Color TotemClr(TotemType t) => t == TotemType.Trigger
        ? new Color(0.55f, 0.80f, 1.0f)
        : new Color(1.0f, 0.72f, 0.35f);

    private static Color EngraveClr(EngraveColor c) => c switch
    {
        EngraveColor.White  => new Color(0.93f, 0.93f, 0.93f),
        EngraveColor.Red    => new Color(1.0f,  0.38f, 0.38f),
        EngraveColor.Green  => new Color(0.38f, 0.88f, 0.48f),
        EngraveColor.Blue   => new Color(0.38f, 0.60f, 1.0f),
        EngraveColor.Yellow => new Color(1.0f,  0.88f, 0.28f),
        EngraveColor.Orange => new Color(1.0f,  0.58f, 0.18f),
        EngraveColor.Purple => new Color(0.80f, 0.38f, 1.0f),
        EngraveColor.Black  => new Color(0.68f, 0.48f, 0.88f),
        _                   => new Color(1, 1, 1),
    };

    private static string ColorGroupName(EngraveColor c) => c switch
    {
        EngraveColor.White  => "  ── 白（傷害）",
        EngraveColor.Green  => "  ── 綠（輔助）",
        EngraveColor.Red    => "  ── 紅（侵略）",
        EngraveColor.Blue   => "  ── 藍（改造）",
        EngraveColor.Yellow => "  ── 黃（限制）",
        _                   => "  ──",
    };

    private static int LvCap(int lv) => lv switch { < 20 => 50, < 30 => 120, < 50 => 250, < 70 => 500, _ => 900 };
}
