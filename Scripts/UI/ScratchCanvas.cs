namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem.VM;

// ─────────────────────────────────────────────────────────────────────────────
//  ScratchCanvas — Scratch 式積木序列編輯器
//
//  職責：
//    • 視覺化顯示 List<BlockNode>（色塊卡片、巢狀容器）
//    • 支援在序列內拖拉重排（頂層）
//    • 提供 Changed 事件通知呼叫方資料已變動
//
//  與外層的唯一介面：
//    SyncFrom(blocks, getSlotOptions)  ← 呼叫方驅動刷新
//    event Action? Changed             ← canvas 驅動通知
//
//  刻意不知道 SpellArray / SpellSlot 等概念，保持可替換。
// ─────────────────────────────────────────────────────────────────────────────
public partial class ScratchCanvas : Control
{
    public event Action? Changed;

    private List<BlockNode>                         _blocks      = new();
    private Func<List<(string display, string key)>>? _getSlotOpts;

    private VBoxContainer _list = null!;

    // 拖拉狀態由 BlockDrag 靜態類別統一管理（見檔案末尾）

    // ══════════════════════════════════════════════════════════════
    //  公開 API
    // ══════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.VerticalScrollMode   = ScrollContainer.ScrollMode.Auto;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        AddChild(scroll);

        _list = new VBoxContainer();
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _list.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(_list);
    }

    public void SyncFrom(List<BlockNode> blocks,
        Func<List<(string display, string key)>> getSlotOpts)
    {
        _blocks      = blocks;
        _getSlotOpts = getSlotOpts;
        Rebuild();
    }

    // ══════════════════════════════════════════════════════════════
    //  重建 UI
    // ══════════════════════════════════════════════════════════════

    private void Rebuild()
    {
        if (_list is null) return;
        foreach (Node c in _list.GetChildren().ToArray()) c.QueueFree();

        if (_blocks.Count == 0)
        {
            _list.AddChild(BuildEmpty());
            return;
        }

        // 頂部 drop zone
        _list.AddChild(BuildDropZone(0, _blocks));

        for (int i = 0; i < _blocks.Count; i++)
        {
            _list.AddChild(BuildBlockCard(i, _blocks[i], _blocks, indent: 0));
            _list.AddChild(BuildDropZone(i + 1, _blocks));
        }
    }

    private static Label BuildEmpty()
    {
        var l = new Label
        {
            Text = "（空白）——從左側積木庫點擊加入，或拖入此處",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        l.AddThemeColorOverride("font_color", new Color(0.35f, 0.35f, 0.40f));
        l.AddThemeFontSizeOverride("font_size", 11);
        l.CustomMinimumSize = new Vector2(0, 36);
        return l;
    }

    // ══════════════════════════════════════════════════════════════
    //  卡片構建
    // ══════════════════════════════════════════════════════════════

    private Control BuildBlockCard(int idx, BlockNode block,
        List<BlockNode> parent, int indent)
    {
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 0);

        // ── 主卡片列 ──────────────────────────────────────────────
        var card = new Panel();
        card.CustomMinimumSize = new Vector2(0, 32);

        var cardStyle = new StyleBoxFlat
        {
            BgColor             = new Color(0.17f, 0.17f, 0.21f),
            CornerRadiusTopLeft = 5, CornerRadiusTopRight    = 5,
            CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        };
        card.AddThemeStyleboxOverride("panel", cardStyle);

        var row = new HBoxContainer();
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.AddThemeConstantOverride("separation", 4);
        card.AddChild(row);

        // 縮排空白
        if (indent > 0)
            row.AddChild(Spacer(indent * 14, 0));

        // ── 拖拉把手（帶顏色的左側條）──────────────────────────
        var handle = MakeDragHandle(idx, parent, BlockColor(block.Type));
        row.AddChild(handle);

        Spacer(row, 4, 0);

        // ── 積木名稱 ─────────────────────────────────────────────
        var nameLbl = new Label { Text = BlockName(block.Type) };
        nameLbl.AddThemeColorOverride("font_color", BlockColor(block.Type));
        nameLbl.AddThemeFontSizeOverride("font_size", 12);
        nameLbl.VerticalAlignment = VerticalAlignment.Center;
        nameLbl.CustomMinimumSize = new Vector2(0, 32);
        row.AddChild(nameLbl);

        Spacer(row, 4, 0);

        // ── 參數區 ───────────────────────────────────────────────
        AddParams(row, block, parent, indent);

        var flex = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(flex);

        // ── 刪除按鈕 ─────────────────────────────────────────────
        var del = MiniBtn("✕", new Color(0.5f, 0.15f, 0.15f));
        var captIdx = idx; var captParent = parent;
        del.Pressed += () => { captParent.RemoveAt(captIdx); OnChanged(); };
        row.AddChild(del);
        Spacer(row, 4, 0);

        outer.AddChild(card);

        // ── 巢狀容器（If / RepeatN / RepeatWhile）────────────────
        if (block.Type == BlockType.If)
        {
            outer.AddChild(BuildBranch(block.ThenBranch, "THEN", indent + 1));
            outer.AddChild(BuildBranch(block.ElseBranch, "ELSE", indent + 1));
        }

        if (block.Type == BlockType.RepeatN   ||
            block.Type == BlockType.RepeatWhile ||
            block.Type == BlockType.ForEachNearby)
            outer.AddChild(BuildBranch(block.LoopBody, "LOOP", indent + 1));

        // 間距
        outer.AddChild(Spacer(0, 3));
        return outer;
    }

    // ── 巢狀分支容器 ──────────────────────────────────────────────

    private Control BuildBranch(List<BlockNode> children, string label, int indent)
    {
        var wrap = new Panel();
        wrap.CustomMinimumSize = new Vector2(0, 0);
        var wStyle = new StyleBoxFlat
        {
            BgColor      = new Color(0.13f, 0.15f, 0.13f),
            BorderColor  = new Color(0.25f, 0.35f, 0.25f),
            BorderWidthLeft  = 2,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        wrap.AddThemeStyleboxOverride("panel", wStyle);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 0);
        vbox.OffsetLeft = indent * 14;
        wrap.AddChild(vbox);

        // 標題列
        var hdrRow = new HBoxContainer();
        Spacer(hdrRow, indent * 14 + 10, 0);
        var hdrLbl = new Label { Text = label };
        hdrLbl.AddThemeColorOverride("font_color", new Color(0.40f, 0.70f, 0.40f));
        hdrLbl.AddThemeFontSizeOverride("font_size", 10);
        hdrRow.AddChild(hdrLbl);
        vbox.AddChild(hdrRow);

        // 子積木
        for (int ci = 0; ci < children.Count; ci++)
            vbox.AddChild(BuildBlockCard(ci, children[ci], children, indent));

        // 加入子積木按鈕
        var addRow = new HBoxContainer();
        Spacer(addRow, indent * 14 + 8, 0);
        var addBtn = new Button
        {
            Text      = "＋  加入積木",
            Flat      = true,
            Alignment = HorizontalAlignment.Left,
        };
        addBtn.CustomMinimumSize = new Vector2(0, 22);
        addBtn.AddThemeColorOverride("font_color", new Color(0.40f, 0.65f, 0.40f));
        addBtn.AddThemeFontSizeOverride("font_size", 10);
        var captList = children;
        addBtn.Pressed += () =>
        {
            captList.Add(new BlockNode
            {
                Type   = BlockType.InvokeTotem,
                Params = new Dictionary<string, object?> { ["totemName"] = "" }
            });
            OnChanged();
        };
        addRow.AddChild(addBtn);
        vbox.AddChild(addRow);

        return wrap;
    }

    // ══════════════════════════════════════════════════════════════
    //  拖拉機制
    // ══════════════════════════════════════════════════════════════

    private Panel MakeDragHandle(int idx, List<BlockNode> parent, Color clr)
    {
        var handle = new Panel();
        handle.CustomMinimumSize = new Vector2(8, 0);
        handle.SizeFlagsVertical = SizeFlags.ExpandFill;
        handle.MouseDefaultCursorShape = CursorShape.Drag;
        var s = new StyleBoxFlat { BgColor = clr };
        s.CornerRadiusTopLeft = s.CornerRadiusBottomLeft = 4;
        handle.AddThemeStyleboxOverride("panel", s);

        var captIdx    = idx;
        var captParent = parent;

        handle.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                BlockDrag.BeginMove(captIdx, captParent, captParent[captIdx]);
                var preview = BuildDragPreview(captParent[captIdx]);
                handle.SetDragPreview(preview);
                handle.ForceDrag(new Godot.Collections.Dictionary { ["scratch"] = captIdx }, preview);
            }
        };
        return handle;
    }

    internal static Control BuildDragPreview(BlockNode block)
    {
        var p = new Panel();
        p.CustomMinimumSize = new Vector2(140, 28);
        var s = new StyleBoxFlat
        {
            BgColor = BlockColor(block.Type).Darkened(0.3f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        p.AddThemeStyleboxOverride("panel", s);
        var l = new Label { Text = "  " + BlockName(block.Type) };
        l.AddThemeColorOverride("font_color", BlockColor(block.Type));
        l.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        p.AddChild(l);
        p.Modulate = new Color(1, 1, 1, 0.75f);
        return p;
    }

    private Control BuildDropZone(int insertAt, List<BlockNode> parent)
    {
        // 細線，hover 時變亮
        var zone = new Control();
        zone.CustomMinimumSize = new Vector2(0, 6);
        zone.MouseFilter       = MouseFilterEnum.Stop;

        // 視覺指示線（預設透明）
        var line = new Panel();
        line.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        line.OffsetTop    =  2;
        line.OffsetBottom = -2;
        var lineStyle = new StyleBoxFlat { BgColor = new Color(0.4f, 0.8f, 0.4f, 0f) };
        lineStyle.CornerRadiusTopLeft = lineStyle.CornerRadiusTopRight =
        lineStyle.CornerRadiusBottomLeft = lineStyle.CornerRadiusBottomRight = 2;
        line.AddThemeStyleboxOverride("panel", lineStyle);
        zone.AddChild(line);

        var captInsert = insertAt;
        var captParent = parent;

        zone.MouseEntered += () =>
        {
            if (BlockDrag.Active)
                lineStyle.BgColor = new Color(0.4f, 0.8f, 0.4f, 1f);
        };
        zone.MouseExited += () => lineStyle.BgColor = new Color(0.4f, 0.8f, 0.4f, 0f);

        zone.GuiInput += (InputEvent e) =>
        {
            if (e is not InputEventMouseButton mb ||
                mb.Pressed ||
                mb.ButtonIndex != MouseButton.Left ||
                !BlockDrag.Active) return;

            var block    = BlockDrag.Block!;
            int insertAt = captInsert;

            if (BlockDrag.SourceList != null)
            {
                var srcList = BlockDrag.SourceList;
                int srcIdx  = BlockDrag.SourceIdx;
                // 同一個列表移動：插入點要補償移除造成的偏移
                if (ReferenceEquals(srcList, captParent) && srcIdx < insertAt)
                    insertAt--;
                // 移到原位，忽略
                if (ReferenceEquals(srcList, captParent) && insertAt == srcIdx)
                {
                    BlockDrag.Clear();
                    return;
                }
                srcList.RemoveAt(srcIdx);
            }

            captParent.Insert(Math.Clamp(insertAt, 0, captParent.Count), block);
            BlockDrag.Clear();
            OnChanged();
        };

        return zone;
    }

    // ══════════════════════════════════════════════════════════════
    //  積木參數區（統一查 BlockDescriptor）
    // ══════════════════════════════════════════════════════════════

    private void AddParams(HBoxContainer row, BlockNode block,
        List<BlockNode> parent, int indent)
    {
        if (_descs.TryGetValue(block.Type, out var desc))
            desc.BuildUI?.Invoke(row, block, this);
    }

    // ══════════════════════════════════════════════════════════════
    //  小型參數控件工廠
    // ══════════════════════════════════════════════════════════════

    private OptionButton SlotPicker(BlockNode block, string key)
    {
        var opts = _getSlotOpts?.Invoke() ?? new List<(string, string)>();
        var dd = new OptionButton();
        dd.CustomMinimumSize = new Vector2(100, 24);
        dd.AddThemeFontSizeOverride("font_size", 10);
        dd.AddItem("（選插槽）");
        foreach (var (display, _) in opts) dd.AddItem(display);

        string cur = block.Params.TryGetValue(key, out var pv) ? pv?.ToString() ?? "" : "";
        for (int i = 0; i < opts.Count; i++)
            if (opts[i].key == cur) { dd.Selected = i + 1; break; }

        dd.ItemSelected += optIdx =>
        {
            block.Params[key] = optIdx > 0 && optIdx - 1 < opts.Count
                ? opts[(int)optIdx - 1].key : "";
            Changed?.Invoke();
        };
        return dd;
    }

    private static LineEdit SmallEdit(BlockNode block, string key,
        string placeholder, int width)
    {
        var le = new LineEdit { PlaceholderText = placeholder };
        le.CustomMinimumSize = new Vector2(width, 24);
        le.AddThemeFontSizeOverride("font_size", 11);
        le.Text = block.Params.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
        le.TextChanged += t => block.Params[key] = t;
        return le;
    }

    private static SpinBox SmallSpin(BlockNode block, string key,
        float min, float max, float step, int width)
    {
        var sb = new SpinBox { MinValue = min, MaxValue = max, Step = step };
        sb.CustomMinimumSize = new Vector2(width, 24);
        sb.GetLineEdit().AddThemeFontSizeOverride("font_size", 11);
        float cur = block.Params.TryGetValue(key, out var v) && v is float f ? f : min;
        sb.Value = cur;
        sb.ValueChanged += d => block.Params[key] = (float)d;
        return sb;
    }

    private static OptionButton SmallDrop(BlockNode block, string key,
        string[] values, string[] labels, int width)
    {
        var dd = new OptionButton();
        dd.CustomMinimumSize = new Vector2(width, 24);
        dd.AddThemeFontSizeOverride("font_size", 10);
        foreach (var l in labels) dd.AddItem(l);

        string cur = block.Params.TryGetValue(key, out var pv) ? pv?.ToString() ?? "" : "";
        for (int i = 0; i < values.Length; i++)
            if (values[i] == cur) { dd.Selected = i; break; }

        dd.ItemSelected += i => block.Params[key] = values[(int)i];
        return dd;
    }

    private static CheckButton CheckBox(BlockNode block, string key, string label)
    {
        var cb = new CheckButton { Text = label };
        cb.AddThemeFontSizeOverride("font_size", 10);
        cb.ButtonPressed = block.Params.TryGetValue(key, out var v) && v is bool b && b;
        cb.Toggled += on => block.Params[key] = on;
        return cb;
    }

    private static Label TinyLbl(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.60f));
        l.AddThemeFontSizeOverride("font_size", 11);
        l.VerticalAlignment = VerticalAlignment.Center;
        return l;
    }

    private static Button MiniBtn(string text, Color bg)
    {
        var b = new Button { Text = text };
        b.CustomMinimumSize = new Vector2(22, 22);
        b.AddThemeFontSizeOverride("font_size", 10);
        b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var s = new StyleBoxFlat { BgColor = bg };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
        b.AddThemeStyleboxOverride("normal",  s);
        b.AddThemeStyleboxOverride("hover",   new StyleBoxFlat { BgColor = bg.Lightened(0.2f) });
        b.AddThemeStyleboxOverride("pressed", s);
        b.AddThemeStyleboxOverride("focus",   s);
        return b;
    }

    private static Control Spacer(int w, int h)
    {
        return new Control { CustomMinimumSize = new Vector2(w, h) };
    }

    private static void Spacer(HBoxContainer row, int w, int h)
    {
        row.AddChild(new Control { CustomMinimumSize = new Vector2(w, h) });
    }

    // ── Changed 廣播 ──────────────────────────────────────────────

    private void OnChanged()
    {
        Rebuild();
        Changed?.Invoke();
    }

    // ══════════════════════════════════════════════════════════════
    //  積木型別描述器（顏色 / 名稱 / 預設積木 / 參數 UI）集中管理
    // ══════════════════════════════════════════════════════════════

    // 色系常數（避免各 descriptor 重複 new Color(...)）
    private static readonly Color COrng  = new(1.00f, 0.72f, 0.35f); // 橙   InvokeTotem/Spell
    private static readonly Color CFlow  = new(0.65f, 0.95f, 0.30f); // 黃綠 控制流
    private static readonly Color CGrn   = new(0.38f, 0.88f, 0.48f); // 綠   Wait
    private static readonly Color CCyan  = new(0.38f, 0.88f, 0.88f); // 青   Edge/Pulse
    private static readonly Color CYlw   = new(1.00f, 0.88f, 0.28f); // 黃   Var/Compare
    private static readonly Color COrnD  = new(1.00f, 0.65f, 0.20f); // 橘深 List
    private static readonly Color CBlue  = new(0.55f, 0.80f, 1.00f); // 藍   Entity query
    private static readonly Color CPurp  = new(0.80f, 0.38f, 1.00f); // 紫   Broadcast
    private static readonly Color CRed   = new(1.00f, 0.42f, 0.42f); // 紅   Detect
    private static readonly Color CLvnd  = new(0.95f, 0.65f, 0.95f); // 淡紫 TaskCounter
    private static readonly Color CGray  = new(0.75f, 0.75f, 0.75f); // 灰   fallback

    // 積木描述器（顏色、UI 名稱、預設積木工廠、參數 UI 建構器）
    // BuildUI = null 表示無參數列
    internal record BlockDescriptor(
        Color  Color,
        string Name,
        Func<BlockNode> Make,
        Action<HBoxContainer, BlockNode, ScratchCanvas>? BuildUI = null
    );

    // 條件型參數 UI（If / RepeatWhile 共用）
    private static Action<HBoxContainer, BlockNode, ScratchCanvas> ConditionUI => (row, block, canvas) =>
    {
        string[] types  = { "totemDone", "totemHit", "totemFizzle", "compare", "varBool" };
        string[] labels = { "已執行",    "命中",      "Fizzle",      "比較",    "布林變數" };
        row.AddChild(SmallDrop(block, "conditionType", types, labels, 60));
        string cType = block.Params.TryGetValue("conditionType", out var cv) ? cv?.ToString() ?? "" : "";
        if (cType == "compare")
        {
            row.AddChild(SmallEdit(block, "left", "L", 44));
            string[] ops = { ">", "<", "=", "≠", ">=", "<=" };
            row.AddChild(SmallDrop(block, "op", ops, ops, 40));
            row.AddChild(SmallEdit(block, "right", "R", 44));
        }
        else if (cType == "varBool")
            row.AddChild(SmallEdit(block, "varName", "變數名", 72));
        else
            row.AddChild(canvas.SlotPicker(block, "totemName"));
    };

    // 集中管理所有積木型別的 descriptor
    private static readonly Dictionary<BlockType, BlockDescriptor> _descs = new()
    {
        // ── 圖騰 / 連段 ──────────────────────────────────────────────
        { BlockType.InvokeTotem,  new(COrng, "觸發圖騰", () => B(BlockType.InvokeTotem,  ("totemName", "")),
            (r, b, c) => r.AddChild(c.SlotPicker(b, "totemName"))) },
        { BlockType.InvokeSpell,  new(COrng, "發動法陣", () => B(BlockType.InvokeSpell,  ("spellName", "")),
            (r, b, _) => r.AddChild(SmallEdit(b, "spellName", "法陣名", 90))) },

        // ── 控制流 ────────────────────────────────────────────────────
        { BlockType.If,           new(CFlow, "IF",      () => B(BlockType.If, ("conditionType", "totemDone"), ("totemName", "")),
            ConditionUI) },
        { BlockType.RepeatN,      new(CFlow, "REPEAT",  () => B(BlockType.RepeatN,   ("count", 2f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "count", 1f, 20f, 1f, 36)); r.AddChild(TinyLbl("次")); }) },
        { BlockType.RepeatWhile,  new(CFlow, "WHILE",   () => B(BlockType.RepeatWhile, ("conditionType", "compare"), ("left", "x"), ("op", ">"), ("right", "0")),
            ConditionUI) },
        { BlockType.RandomChoice, new(CFlow, "RANDOM",  () => B(BlockType.RandomChoice, ("count", 2f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "count", 2f, 8f, 1f, 36)); r.AddChild(TinyLbl("支")); }) },
        { BlockType.ForEachNearby,new(CFlow, "FOREACH", () => B(BlockType.ForEachNearby, ("radius", 5f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "radius", 1f, 30f, 1f, 40)); r.AddChild(TinyLbl("格")); }) },

        // ── 時序 ──────────────────────────────────────────────────────
        { BlockType.Wait,         new(CGrn,  "WAIT",    () => B(BlockType.Wait, ("duration", 1f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "duration", 0.1f, 30f, 0.1f, 42)); r.AddChild(TinyLbl("秒")); }) },

        // ── 邊沿偵測 ──────────────────────────────────────────────────
        { BlockType.RisingEdge,   new(CCyan, "上升沿",  () => B(BlockType.RisingEdge,  ("totemName", "")),
            (r, b, c) => r.AddChild(c.SlotPicker(b, "totemName"))) },
        { BlockType.FallingEdge,  new(CCyan, "下降沿",  () => B(BlockType.FallingEdge, ("totemName", "")),
            (r, b, c) => r.AddChild(c.SlotPicker(b, "totemName"))) },
        { BlockType.SinglePulse,  new(CCyan, "單次脈衝",() => B(BlockType.SinglePulse, ("totemName", "")),
            (r, b, c) => r.AddChild(c.SlotPicker(b, "totemName"))) },

        // ── 變數 ──────────────────────────────────────────────────────
        { BlockType.SetVar,       new(CYlw,  "SET VAR", () => B(BlockType.SetVar, ("name", "x"), ("value", "0"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "變數名", 52)); r.AddChild(TinyLbl("="));
                           r.AddChild(SmallEdit(b, "value", "值", 52)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.GetVar,       new(CYlw,  "GET VAR", () => B(BlockType.GetVar, ("name", "x")),
            (r, b, _) => r.AddChild(SmallEdit(b, "name", "變數名", 72))) },
        { BlockType.SetVarBool,   new(CYlw,  "SET BOOL",() => B(BlockType.SetVarBool, ("name", "b"), ("value", "true"), ("global", false))) },
        { BlockType.GetVarBool,   new(CYlw,  "GET BOOL",() => B(BlockType.GetVarBool, ("name", "b"))) },
        { BlockType.Compare,      new(CYlw,  "COMPARE", () => B(BlockType.Compare, ("left", "x"), ("op", "="), ("right", "0"), ("resultVar", "result"), ("global", false)),
            (r, b, _) =>
            {
                r.AddChild(SmallEdit(b, "left", "L", 44));
                string[] ops = { ">", "<", "=", "≠", ">=", "<=" };
                r.AddChild(SmallDrop(b, "op", ops, ops, 40));
                r.AddChild(SmallEdit(b, "right", "R", 44));
                r.AddChild(TinyLbl("→"));
                r.AddChild(SmallEdit(b, "resultVar", "結果", 56));
                r.AddChild(CheckBox(b, "global", "全域"));
            }) },

        // ── List ──────────────────────────────────────────────────────
        { BlockType.ListCreate,   new(COrnD, "LIST NEW",    () => B(BlockType.ListCreate, ("name", "list"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 72)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListAppend,   new(COrnD, "LIST ADD",    () => B(BlockType.ListAppend, ("name", "list"), ("value", "0"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("+"));
                           r.AddChild(SmallEdit(b, "value", "值/變數", 60)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListPop,      new(COrnD, "LIST POP",    () => B(BlockType.ListPop,    ("name", "list"), ("resultVar", "v"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b, "resultVar", "結果", 56)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListGet,      new(COrnD, "LIST GET",    () => B(BlockType.ListGet,    ("name", "list"), ("index", "1"), ("resultVar", "v"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("["));
                           r.AddChild(SmallEdit(b, "index", "索引", 40)); r.AddChild(TinyLbl("]→"));
                           r.AddChild(SmallEdit(b, "resultVar", "結果", 52)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListDequeue,  new(COrnD, "LIST DEQUEUE",() => B(BlockType.ListDequeue)) },
        { BlockType.ListSet,      new(COrnD, "LIST SET",    () => B(BlockType.ListSet)) },
        { BlockType.ListLength,   new(COrnD, "LIST LEN",    () => B(BlockType.ListLength)) },
        { BlockType.ListContains, new(COrnD, "LIST HAS",    () => B(BlockType.ListContains)) },
        { BlockType.ListRemoveAt, new(COrnD, "LIST DEL",    () => B(BlockType.ListRemoveAt)) },
        { BlockType.ListClear,    new(COrnD, "LIST CLEAR",  () => B(BlockType.ListClear)) },

        // ── 實體查詢 ──────────────────────────────────────────────────
        { BlockType.QueryNear,    new(CBlue, "QUERY",    () => B(BlockType.QueryNear, ("radius", 5f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "radius", 1f, 30f, 1f, 40)); r.AddChild(TinyLbl("格")); }) },
        { BlockType.QueryNearest, new(CBlue, "QUERY 1",  () => B(BlockType.QueryNearest, ("radius", 5f), ("resultVar", "nearest")),
            (r, b, _) => { r.AddChild(SmallSpin(b, "radius", 1f, 30f, 1f, 40)); r.AddChild(TinyLbl("格 →"));
                           r.AddChild(SmallEdit(b, "resultVar", "前綴", 60)); }) },
        { BlockType.GetEntityProp,new(CBlue, "GET PROP", () => B(BlockType.GetEntityProp, ("property", "hp"), ("resultVar", "e_hp")),
            (r, b, _) =>
            {
                string[] props = { "hp", "maxhp", "x", "y" };
                string[] plbls = { "HP", "MaxHP", "X", "Y" };
                r.AddChild(SmallDrop(b, "property", props, plbls, 52));
                r.AddChild(TinyLbl("→"));
                r.AddChild(SmallEdit(b, "resultVar", "變數名", 64));
            }) },
        { BlockType.SetEntityProp,new(CBlue, "SET PROP", () => B(BlockType.SetEntityProp, ("property", "hp"), ("damage", 10f)),
            (r, b, _) =>
            {
                string[] props = { "hp" };
                string[] plbls = { "HP" };
                r.AddChild(SmallDrop(b, "property", props, plbls, 44));
                r.AddChild(TinyLbl("減"));
                r.AddChild(SmallSpin(b, "damage", 1f, 999f, 1f, 52));
            }) },

        // ── 廣播 ──────────────────────────────────────────────────────
        { BlockType.Broadcast,       new(CPurp, "BROADCAST",        () => B(BlockType.Broadcast,       ("signal", "")),
            (r, b, _) => r.AddChild(SmallEdit(b, "signal", "訊號名", 80))) },
        { BlockType.BroadcastAndWait,new(CPurp, "BCAST（等同廣播）", () => B(BlockType.BroadcastAndWait,("signal", "")),
            (r, b, _) => r.AddChild(SmallEdit(b, "signal", "訊號名", 80))) },
        { BlockType.OnReceive,       new(CPurp, "ON RECEIVE",        () => B(BlockType.OnReceive,       ("signal", "")),
            (r, b, _) => r.AddChild(SmallEdit(b, "signal", "訊號名", 80))) },

        // ── 偵測 ──────────────────────────────────────────────────────
        { BlockType.DetectHpThreshold,new(CRed, "DETECT HP",  () => B(BlockType.DetectHpThreshold, ("percent", 30f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "percent", 1f, 99f, 1f, 44)); r.AddChild(TinyLbl("%")); }) },
        { BlockType.DetectMpThreshold,new(CRed, "DETECT MP",  () => B(BlockType.DetectMpThreshold, ("percent", 30f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "percent", 1f, 99f, 1f, 44)); r.AddChild(TinyLbl("%")); }) },
        { BlockType.DetectEntityEnter,new(CRed, "DETECT ENT", () => B(BlockType.DetectEntityEnter, ("faction", "敵方"), ("radius", 5f))) },

        // ── 任務計數器 ────────────────────────────────────────────────
        { BlockType.TaskCounterSet,   new(CLvnd, "CTR SET",   () => B(BlockType.TaskCounterSet,    ("name", "c"), ("count", 0f)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "計數器名", 64)); r.AddChild(SmallSpin(b, "count", 0f, 999f, 1f, 44)); }) },
        { BlockType.TaskCounterAdd,   new(CLvnd, "CTR ADD",   () => B(BlockType.TaskCounterAdd,    ("name", "c"), ("count", 1f)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "計數器名", 64)); r.AddChild(SmallSpin(b, "count", 0f, 999f, 1f, 44)); }) },
        { BlockType.TaskCounterOnReach,new(CLvnd,"CTR REACH", () => B(BlockType.TaskCounterOnReach,("name", "c"), ("count", 5f)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "計數器名", 64)); r.AddChild(SmallSpin(b, "count", 0f, 999f, 1f, 44)); }) },
        { BlockType.TaskCounterReset, new(CLvnd, "CTR RESET", () => B(BlockType.TaskCounterReset,  ("name", "c")),
            (r, b, _) => r.AddChild(SmallEdit(b, "name", "計數器名", 64))) },
    };

    // ── 統一查表的三個舊介面（保持對外 API 不變）─────────────────
    internal static Color     BlockColor(BlockType t) =>
        _descs.TryGetValue(t, out var d) ? d.Color : CGray;
    internal static string    BlockName(BlockType t)  =>
        _descs.TryGetValue(t, out var d) ? d.Name  : t.ToString();
    internal static BlockNode MakeDefaultBlock(BlockType t) =>
        _descs.TryGetValue(t, out var d) ? d.Make() : new BlockNode { Type = t };

    private static BlockNode B(BlockType t, params (string k, object? v)[] ps)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in ps) d[k] = v;
        return new BlockNode { Type = t, Params = d };
    }
}

// ── 積木拖拉共享狀態（ScratchCanvas drop 與 AbilityEditorUI palette 共用）──────
internal static class BlockDrag
{
    public static BlockNode?       Block      { get; private set; }
    public static List<BlockNode>? SourceList { get; private set; }
    public static int              SourceIdx  { get; private set; } = -1;

    // 從現有積木序列拖出（移動）
    public static void BeginMove(int idx, List<BlockNode> src, BlockNode block)
    { Block = block; SourceList = src; SourceIdx = idx; }

    // 從 palette 拖出（新增）
    public static void BeginNew(BlockNode block)
    { Block = block; SourceList = null; SourceIdx = -1; }

    public static void Clear() { Block = null; SourceList = null; SourceIdx = -1; }
    public static bool Active  => Block != null;
}
