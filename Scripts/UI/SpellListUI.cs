namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem.Data;

// 技能創建空間：圓球列表首頁（Stage 2）
// 主動技能圓球邊框紅色，被動技能圓球邊框橘色。
// 點擊主動技能圓球 → 發出 ActiveSpellClicked 讓 Main.cs 切換到編輯頁。
// 點擊被動技能圓球 → 列表內顯示提示訊息（Stage 3 再支援完整編輯）。
// 點擊「+」→ 發出 AddSpellRequested，由 Main.cs 決定新增主動或被動。
public partial class SpellListUI : Control
{
    [Signal] public delegate void ActiveSpellClickedEventHandler(int slotIndex);
    [Signal] public delegate void AddSpellRequestedEventHandler();

    public SpellLoadout? Loadout { get; set; }

    private HBoxContainer  _circleRow   = null!;
    private Button         _addFloatBtn = null!;
    private Label          _msgLabel    = null!;
    private double         _msgTimer    = 0;
    private PanelContainer _tooltip     = null!;
    private Label          _tooltipLbl  = null!;

    private const float CircleSize    = 260f;
    private const float CircleSpacing = 22f;
    private const float FloatBtnSize  = CircleSize / 2f; // 130

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUI();
    }

    // 外部呼叫：重新渲染圓球列表（切換到此頁前呼叫）
    public void Refresh()
    {
        RebuildCircles();
        UpdateFloatBtn();
    }

    // ──────────────────────────────────────────────────────────────
    //  UI 建構
    // ──────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.12f, 0.97f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 0);
        AddChild(vbox);

        // ── 標題列 ─────────────────────────────────────────────
        {
            var header = new PanelContainer();
            var hs = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.18f) };
            header.AddThemeStyleboxOverride("panel", hs);
            header.CustomMinimumSize = new Vector2(0, 58);
            vbox.AddChild(header);

            var row = new HBoxContainer();
            row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            row.AddThemeConstantOverride("separation", 0);
            header.AddChild(row);

            Spacer(row, 24);

            var title = new Label { Text = "技能創建空間" };
            title.AddThemeColorOverride("font_color", Colors.White);
            title.AddThemeFontSizeOverride("font_size", 22);
            title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(title);

            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

            var hint = new Label { Text = "點擊圓球編輯技能　E 關閉" };
            hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.65f));
            hint.AddThemeFontSizeOverride("font_size", 13);
            hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(hint);

            Spacer(row, 24);
        }

        // ── 圓球捲動區 ──────────────────────────────────────────
        {
            var area = new PanelContainer();
            var ars = new StyleBoxFlat { BgColor = new Color(0.10f, 0.10f, 0.14f) };
            area.AddThemeStyleboxOverride("panel", ars);
            area.SizeFlagsVertical = SizeFlags.ExpandFill;
            vbox.AddChild(area);

            // 垂直置中
            var inner = new VBoxContainer();
            inner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            inner.AddThemeConstantOverride("separation", 0);
            area.AddChild(inner);
            inner.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

            var scroll = new ScrollContainer();
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
            scroll.VerticalScrollMode   = ScrollContainer.ScrollMode.Disabled;
            scroll.CustomMinimumSize    = new Vector2(0, CircleSize + 44f);
            scroll.SizeFlagsHorizontal  = SizeFlags.ExpandFill;
            inner.AddChild(scroll);

            // 隱藏 scrollbar 視覺（保留滾輪互動）
            var hbar = scroll.GetHScrollBar();
            hbar.CustomMinimumSize = Vector2.Zero;
            hbar.Modulate = new Color(0, 0, 0, 0);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left",   28);
            margin.AddThemeConstantOverride("margin_right",  28);
            margin.AddThemeConstantOverride("margin_top",    0);
            margin.AddThemeConstantOverride("margin_bottom", 0);
            scroll.AddChild(margin);

            _circleRow = new HBoxContainer();
            _circleRow.AddThemeConstantOverride("separation", (int)CircleSpacing);
            _circleRow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            margin.AddChild(_circleRow);

            inner.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        }

        // ── 底部欄（容量提示 + 訊息）────────────────────────────
        {
            var footer = new PanelContainer();
            var fs = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.18f) };
            footer.AddThemeStyleboxOverride("panel", fs);
            footer.CustomMinimumSize = new Vector2(0, 38);
            vbox.AddChild(footer);

            var row = new HBoxContainer();
            row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            row.AddThemeConstantOverride("separation", 0);
            footer.AddChild(row);

            Spacer(row, 24);

            var capLbl = new Label
            {
                Text = $"主動上限 {SpellLoadout.MaxSlots}　被動上限 {SpellLoadout.MaxPassiveSlots}"
            };
            capLbl.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.55f));
            capLbl.AddThemeFontSizeOverride("font_size", 13);
            capLbl.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(capLbl);

            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

            _msgLabel = new Label();
            _msgLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));
            _msgLabel.AddThemeFontSizeOverride("font_size", 13);
            _msgLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            _msgLabel.Visible = false;
            row.AddChild(_msgLabel);

            Spacer(row, 24);
        }

        // ── 浮動「+」按鈕（右下角，半尺寸）────────────────────────
        _addFloatBtn = MakeAddCircle(FloatBtnSize);
        _addFloatBtn.AnchorLeft   = 1.0f;
        _addFloatBtn.AnchorRight  = 1.0f;
        _addFloatBtn.AnchorTop    = 1.0f;
        _addFloatBtn.AnchorBottom = 1.0f;
        _addFloatBtn.OffsetLeft   = -(FloatBtnSize + 28f);
        _addFloatBtn.OffsetTop    = -(FloatBtnSize + 28f);
        _addFloatBtn.OffsetRight  = -28f;
        _addFloatBtn.OffsetBottom = -28f;
        _addFloatBtn.Pressed += () => EmitSignal(SignalName.AddSpellRequested);
        AddChild(_addFloatBtn);

        // ── Tooltip ────────────────────────────────────────────
        _tooltip = new PanelContainer();
        _tooltip.Visible = false;
        _tooltip.ZIndex  = 20;
        var tts = new StyleBoxFlat();
        tts.BgColor = new Color(0.10f, 0.10f, 0.15f, 0.96f);
        tts.BorderColor = new Color(0.35f, 0.35f, 0.52f);
        tts.BorderWidthLeft = tts.BorderWidthRight =
        tts.BorderWidthTop  = tts.BorderWidthBottom = 1;
        tts.CornerRadiusTopLeft = tts.CornerRadiusTopRight =
        tts.CornerRadiusBottomLeft = tts.CornerRadiusBottomRight = 6;
        _tooltip.AddThemeStyleboxOverride("panel", tts);

        var ttMargin = new MarginContainer();
        ttMargin.AddThemeConstantOverride("margin_left",   12);
        ttMargin.AddThemeConstantOverride("margin_right",  12);
        ttMargin.AddThemeConstantOverride("margin_top",     8);
        ttMargin.AddThemeConstantOverride("margin_bottom",  8);
        _tooltip.AddChild(ttMargin);

        _tooltipLbl = new Label();
        _tooltipLbl.AutowrapMode = TextServer.AutowrapMode.Word;
        _tooltipLbl.CustomMinimumSize = new Vector2(220, 0);
        _tooltipLbl.AddThemeColorOverride("font_color", Colors.White);
        _tooltipLbl.AddThemeFontSizeOverride("font_size", 13);
        ttMargin.AddChild(_tooltipLbl);

        AddChild(_tooltip);
    }

    // ──────────────────────────────────────────────────────────────
    //  圓球重建
    // ──────────────────────────────────────────────────────────────

    private void RebuildCircles()
    {
        // 清空現有圓球
        while (_circleRow.GetChildCount() > 0)
        {
            var c = _circleRow.GetChild(0);
            _circleRow.RemoveChild(c);
            c.QueueFree();
        }

        if (Loadout is null) return;

        // ── 主動技能 ─────────────────────────────────────────────
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            var spell = Loadout.GetSlot(i);
            if (spell is null || string.IsNullOrWhiteSpace(spell.Name)) continue;

            int idx = i;
            var circle = MakeSpellCircle(spell, isPassive: false, isOverLimit: false);
            circle.Pressed += () => EmitSignal(SignalName.ActiveSpellClicked, idx);
            circle.MouseEntered += () => ShowTooltip(spell, isPassive: false);
            circle.MouseExited  += HideTooltip;
            _circleRow.AddChild(circle);
        }

        // ── 被動技能 ─────────────────────────────────────────────
        int passiveCount = Loadout.PassiveCount;
        for (int i = 0; i < passiveCount; i++)
        {
            var spell = Loadout.GetPassive(i)!;
            bool over = i >= SpellLoadout.MaxPassiveSlots;

            var circle = MakeSpellCircle(spell, isPassive: true, isOverLimit: over);
            circle.Pressed += () => ShowMsg("被動技能編輯將於 Stage 3 支援");
            circle.MouseEntered += () => ShowTooltip(spell, isPassive: true);
            circle.MouseExited  += HideTooltip;
            _circleRow.AddChild(circle);
        }

        // ── 列表尾「+」 ──────────────────────────────────────────
        if (!IsAtLimit())
        {
            var addInline = MakeAddCircle();
            addInline.Pressed += () => EmitSignal(SignalName.AddSpellRequested);
            _circleRow.AddChild(addInline);
        }
    }

    private bool IsAtLimit()
    {
        if (Loadout is null) return true;
        bool activeFull  = CountActiveNamed() >= SpellLoadout.MaxSlots;
        bool passiveFull = Loadout.PassiveCount >= SpellLoadout.MaxPassiveSlots;
        return activeFull && passiveFull;
    }

    private int CountActiveNamed()
    {
        if (Loadout is null) return 0;
        int c = 0;
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            var s = Loadout.GetSlot(i);
            if (s is not null && !string.IsNullOrWhiteSpace(s.Name)) c++;
        }
        return c;
    }

    private void UpdateFloatBtn()
    {
        bool maxed = IsAtLimit();
        _addFloatBtn.Disabled = maxed;
        if (_addFloatBtn.GetChildCount() > 0 && _addFloatBtn.GetChild(0) is Label lbl)
        {
            lbl.AddThemeColorOverride("font_color",
                maxed ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.5f, 1.0f, 0.5f));
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  訊息顯示（短暫提示）
    // ──────────────────────────────────────────────────────────────

    private void ShowMsg(string msg)
    {
        _msgLabel.Text    = msg;
        _msgLabel.Visible = true;
        _msgTimer         = 3.0;
    }

    public override void _Process(double delta)
    {
        if (_msgTimer > 0)
        {
            _msgTimer -= delta;
            if (_msgTimer <= 0)
                _msgLabel.Visible = false;
        }

        if (_tooltip.Visible)
        {
            var mouse = GetViewport().GetMousePosition();
            var vp    = GetViewportRect().Size;
            float x = Mathf.Min(mouse.X + 14f, vp.X - _tooltip.Size.X - 4f);
            float y = Mathf.Min(mouse.Y + 14f, vp.Y - _tooltip.Size.Y - 4f);
            _tooltip.GlobalPosition = new Vector2(x, y);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Tooltip
    // ──────────────────────────────────────────────────────────────

    private void ShowTooltip(SpellArray spell, bool isPassive)
    {
        string name  = string.IsNullOrWhiteSpace(spell.Name) ? "（未命名）" : spell.Name;
        string prose = SpellDescriptionGenerator.GenerateProse(spell);
        _tooltipLbl.Text = $"{name}\n\n{prose}";
        _tooltip.Visible = true;
    }

    private void HideTooltip() => _tooltip.Visible = false;

    // ──────────────────────────────────────────────────────────────
    //  圓球工廠
    // ──────────────────────────────────────────────────────────────

    private Button MakeSpellCircle(SpellArray spell, bool isPassive, bool isOverLimit)
    {
        int r   = (int)(CircleSize / 2);
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(CircleSize, CircleSize);
        btn.ClipContents      = true;

        Color bgNorm   = isOverLimit ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.05f, 0.08f, 0.18f);
        Color bgHover  = isOverLimit ? new Color(0.10f, 0.10f, 0.10f) : new Color(0.10f, 0.16f, 0.34f);
        Color brdNorm  = isOverLimit ? new Color(0.25f, 0.25f, 0.25f)
                                     : isPassive ? new Color(0.15f, 0.88f, 0.75f)
                                                 : new Color(0.30f, 0.58f, 1.00f);
        Color brdHover = isOverLimit ? new Color(0.35f, 0.35f, 0.35f)
                                     : isPassive ? new Color(0.28f, 1.00f, 0.88f)
                                                 : new Color(0.55f, 0.78f, 1.00f);

        btn.AddThemeStyleboxOverride("normal",  CircleStyle(bgNorm,  brdNorm,  r, 5));
        btn.AddThemeStyleboxOverride("focus",   CircleStyle(bgNorm,  brdNorm,  r, 5));
        btn.AddThemeStyleboxOverride("hover",   CircleStyle(bgHover, brdHover, r, 7));
        btn.AddThemeStyleboxOverride("pressed", CircleStyle(bgHover, brdHover, r, 7));

        string nameText = string.IsNullOrWhiteSpace(spell.Name) ? "（未命名）" : spell.Name;
        if (isOverLimit) nameText += "\n超過上限";

        var lbl = new Label { Text = nameText };
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.VerticalAlignment   = VerticalAlignment.Center;
        lbl.AutowrapMode        = TextServer.AutowrapMode.Word;
        lbl.AddThemeColorOverride("font_color",
            isOverLimit ? new Color(0.42f, 0.42f, 0.42f) : Colors.White);
        lbl.AddThemeFontSizeOverride("font_size", 16);
        lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        btn.AddChild(lbl);

        return btn;
    }

    private static Button MakeAddCircle(float size = CircleSize)
    {
        int r        = (int)(size / 2);
        int fontSize = size >= 200f ? 28 : 20;
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(size, size);

        btn.AddThemeStyleboxOverride("normal",  CircleStyle(new Color(0.10f, 0.18f, 0.10f), new Color(0.30f, 0.70f, 0.30f), r, 2));
        btn.AddThemeStyleboxOverride("focus",   CircleStyle(new Color(0.10f, 0.18f, 0.10f), new Color(0.30f, 0.70f, 0.30f), r, 2));
        btn.AddThemeStyleboxOverride("hover",   CircleStyle(new Color(0.16f, 0.26f, 0.16f), new Color(0.45f, 0.90f, 0.45f), r, 3));
        btn.AddThemeStyleboxOverride("pressed", CircleStyle(new Color(0.08f, 0.14f, 0.08f), new Color(0.28f, 0.65f, 0.28f), r, 2));

        var lbl = new Label { Text = "+" };
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.VerticalAlignment   = VerticalAlignment.Center;
        lbl.AddThemeColorOverride("font_color", new Color(0.5f, 1.0f, 0.5f));
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        btn.AddChild(lbl);

        return btn;
    }

    private static StyleBoxFlat CircleStyle(Color bg, Color border, int radius, int bw)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = radius;
        s.BorderWidthLeft = s.BorderWidthRight =
        s.BorderWidthTop  = s.BorderWidthBottom = bw;
        return s;
    }

    // ──────────────────────────────────────────────────────────────
    //  Helper
    // ──────────────────────────────────────────────────────────────

    private static void Spacer(Control parent, float w) =>
        parent.AddChild(new Control { CustomMinimumSize = new Vector2(w, 0) });
}
