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

    // ── 靜態：拖拉狀態 ─────────────────────────────────────────────
    private static int            _dragSrcIdx  = -1;
    private static ScratchCanvas? _dragSrcCanvas;

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

        // ── 巢狀容器（If / RepeatN）──────────────────────────────
        if (block.Type == BlockType.If)
            outer.AddChild(BuildBranch(block.ThenBranch, "THEN", indent + 1));

        if (block.Type == BlockType.RepeatN)
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

        // 拖拉資料
        var captIdx    = idx;
        var captParent = parent;
        var captCanvas = this;

        // 用 GrabFocus 觸發拖拉
        handle.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                _dragSrcIdx    = captIdx;
                _dragSrcCanvas = captCanvas;

                // 建立拖拉資料（Godot native drag）
                var preview = BuildDragPreview(captParent[captIdx]);
                handle.SetDragPreview(preview);
                handle.ForceDrag(
                    new Godot.Collections.Dictionary { ["scratch"] = captIdx },
                    preview);
            }
        };
        return handle;
    }

    private static Control BuildDragPreview(BlockNode block)
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
            if (_dragSrcIdx >= 0)
                lineStyle.BgColor = new Color(0.4f, 0.8f, 0.4f, 1f);
        };
        zone.MouseExited += () => lineStyle.BgColor = new Color(0.4f, 0.8f, 0.4f, 0f);

        // 接受 drop
        // 使用 Godot native drag-drop callbacks 需要 C# override，
        // 此處改用 GuiInput 偵測 mouse_release 在 zone 上方
        zone.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb && !mb.Pressed &&
                mb.ButtonIndex == MouseButton.Left &&
                _dragSrcIdx >= 0 && _dragSrcCanvas == this)
            {
                int from = _dragSrcIdx;
                int to   = captInsert;
                _dragSrcIdx = -1;
                if (from != to && from != to - 1 && captParent == _blocks)
                    ReorderBlock(from, to);
            }
        };

        return zone;
    }

    private void ReorderBlock(int fromIdx, int insertBefore)
    {
        if (fromIdx < 0 || fromIdx >= _blocks.Count) return;
        var block = _blocks[fromIdx];
        _blocks.RemoveAt(fromIdx);
        int target = insertBefore > fromIdx ? insertBefore - 1 : insertBefore;
        target = Math.Clamp(target, 0, _blocks.Count);
        _blocks.Insert(target, block);
        OnChanged();
    }

    // ══════════════════════════════════════════════════════════════
    //  積木參數區（型別分派）
    // ══════════════════════════════════════════════════════════════

    private void AddParams(HBoxContainer row, BlockNode block,
        List<BlockNode> parent, int indent)
    {
        switch (block.Type)
        {
            case BlockType.InvokeTotem:
                row.AddChild(SlotPicker(block, "totemName"));
                break;

            case BlockType.InvokeSpell:
                row.AddChild(SmallEdit(block, "spellName", "法陣名", 90));
                break;

            case BlockType.If:
            {
                string[] types  = { "totemDone", "totemHit", "totemFizzle", "compare" };
                string[] labels = { "已執行",    "命中",      "Fizzle",      "比較" };
                row.AddChild(SmallDrop(block, "conditionType", types, labels, 58));
                row.AddChild(SlotPicker(block, "totemName"));
                break;
            }

            case BlockType.Wait:
                row.AddChild(SmallSpin(block, "duration", 0.1f, 30f, 0.1f, 42));
                row.AddChild(TinyLbl("秒"));
                break;

            case BlockType.RepeatN:
                row.AddChild(SmallSpin(block, "count", 1f, 20f, 1f, 36));
                row.AddChild(TinyLbl("次"));
                break;

            case BlockType.SetVar:
            {
                row.AddChild(SmallEdit(block, "name", "變數名", 52));
                row.AddChild(TinyLbl("="));
                row.AddChild(SmallEdit(block, "value", "值", 52));
                var gb = CheckBox(block, "global", "全域");
                row.AddChild(gb);
                break;
            }

            case BlockType.GetVar:
                row.AddChild(SmallEdit(block, "name", "變數名", 72));
                break;

            case BlockType.Compare:
            {
                row.AddChild(SmallEdit(block, "left",  "L", 44));
                string[] ops = { ">", "<", "=", "≠", ">=", "<=" };
                row.AddChild(SmallDrop(block, "op", ops, ops, 40));
                row.AddChild(SmallEdit(block, "right", "R", 44));
                break;
            }

            case BlockType.RepeatWhile:
                row.AddChild(SmallEdit(block, "condition", "條件名", 80));
                break;

            case BlockType.DetectHpThreshold:
            case BlockType.DetectMpThreshold:
                row.AddChild(SmallSpin(block, "percent", 1f, 99f, 1f, 44));
                row.AddChild(TinyLbl("%"));
                break;

            case BlockType.RisingEdge:
            case BlockType.FallingEdge:
            case BlockType.SinglePulse:
                row.AddChild(SlotPicker(block, "totemName"));
                break;

            case BlockType.Broadcast:
            case BlockType.BroadcastAndWait:
            case BlockType.OnReceive:
                row.AddChild(SmallEdit(block, "signal", "訊號名", 80));
                break;

            case BlockType.TaskCounterSet:
            case BlockType.TaskCounterAdd:
            case BlockType.TaskCounterOnReach:
            {
                row.AddChild(SmallEdit(block, "name", "計數器名", 64));
                row.AddChild(SmallSpin(block, "count", 0f, 999f, 1f, 44));
                break;
            }

            case BlockType.TaskCounterReset:
                row.AddChild(SmallEdit(block, "name", "計數器名", 64));
                break;

            case BlockType.QueryNear:
            case BlockType.ForEachNearby:
                row.AddChild(SmallSpin(block, "radius", 1f, 30f, 1f, 40));
                row.AddChild(TinyLbl("格"));
                break;

            case BlockType.RandomChoice:
                row.AddChild(SmallSpin(block, "count", 2f, 8f, 1f, 36));
                row.AddChild(TinyLbl("支"));
                break;
        }
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
    //  靜態：積木型別映射（顏色 / 名稱 / 預設 Params）
    // ══════════════════════════════════════════════════════════════

    internal static Color BlockColor(BlockType t) => t switch
    {
        BlockType.InvokeTotem or BlockType.InvokeSpell
            => new Color(1.0f,  0.72f, 0.35f),  // 橙
        BlockType.If or BlockType.RepeatN or BlockType.RepeatWhile or
        BlockType.RandomChoice or BlockType.ForEachNearby
            => new Color(0.65f, 0.95f, 0.30f),  // 黃綠
        BlockType.Wait
            => new Color(0.38f, 0.88f, 0.48f),  // 綠
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
        BlockType.DetectEntityEnter
            => new Color(1.0f,  0.42f, 0.42f),  // 紅
        BlockType.TaskCounterSet or BlockType.TaskCounterAdd or
        BlockType.TaskCounterOnReach or BlockType.TaskCounterReset
            => new Color(0.95f, 0.65f, 0.95f),  // 淡紫
        _   => new Color(0.75f, 0.75f, 0.75f),
    };

    internal static string BlockName(BlockType t) => t switch
    {
        BlockType.InvokeTotem       => "觸發圖騰",
        BlockType.InvokeSpell       => "發動法陣",
        BlockType.If                => "IF",
        BlockType.RepeatN           => "REPEAT",
        BlockType.RepeatWhile       => "WHILE",
        BlockType.RandomChoice      => "RANDOM",
        BlockType.ForEachNearby     => "FOREACH",
        BlockType.Wait              => "WAIT",
        BlockType.RisingEdge        => "上升沿",
        BlockType.FallingEdge       => "下降沿",
        BlockType.SinglePulse       => "單次脈衝",
        BlockType.SetVar            => "SET VAR",
        BlockType.GetVar            => "GET VAR",
        BlockType.SetVarBool        => "SET BOOL",
        BlockType.GetVarBool        => "GET BOOL",
        BlockType.Compare           => "COMPARE",
        BlockType.QueryNear         => "QUERY",
        BlockType.GetEntityProp     => "GET PROP",
        BlockType.SetEntityProp     => "SET PROP",
        BlockType.Broadcast         => "BROADCAST",
        BlockType.BroadcastAndWait  => "BCAST+WAIT",
        BlockType.OnReceive         => "ON RECEIVE",
        BlockType.DetectHpThreshold => "DETECT HP",
        BlockType.DetectMpThreshold => "DETECT MP",
        BlockType.DetectEntityEnter => "DETECT ENT",
        BlockType.TaskCounterSet    => "CTR SET",
        BlockType.TaskCounterAdd    => "CTR ADD",
        BlockType.TaskCounterOnReach=> "CTR REACH",
        BlockType.TaskCounterReset  => "CTR RESET",
        _                           => t.ToString(),
    };

    internal static BlockNode MakeDefaultBlock(BlockType type) => type switch
    {
        BlockType.InvokeTotem    => B(type, ("totemName", "")),
        BlockType.InvokeSpell    => B(type, ("spellName", "")),
        BlockType.If             => B(type, ("conditionType", "totemDone"), ("totemName", "")),
        BlockType.RepeatN        => B(type, ("count", 2f)),
        BlockType.RepeatWhile    => B(type, ("condition", "")),
        BlockType.RandomChoice   => B(type, ("count", 2f)),
        BlockType.ForEachNearby  => B(type, ("radius", 5f)),
        BlockType.Wait           => B(type, ("duration", 1f)),
        BlockType.SetVar         => B(type, ("name", "x"), ("value", "0"), ("global", false)),
        BlockType.GetVar         => B(type, ("name", "x")),
        BlockType.SetVarBool     => B(type, ("name", "b"), ("value", "true"), ("global", false)),
        BlockType.GetVarBool     => B(type, ("name", "b")),
        BlockType.Compare        => B(type, ("left", "x"), ("op", "="), ("right", "0")),
        BlockType.RisingEdge or BlockType.FallingEdge or BlockType.SinglePulse
                                 => B(type, ("totemName", "")),
        BlockType.DetectHpThreshold or BlockType.DetectMpThreshold
                                 => B(type, ("percent", 30f)),
        BlockType.DetectEntityEnter => B(type, ("faction", "敵方"), ("radius", 5f)),
        BlockType.QueryNear      => B(type, ("radius", 5f)),
        BlockType.Broadcast or BlockType.BroadcastAndWait or BlockType.OnReceive
                                 => B(type, ("signal", "")),
        BlockType.TaskCounterSet    => B(type, ("name", "c"), ("count", 0f)),
        BlockType.TaskCounterAdd    => B(type, ("name", "c"), ("count", 1f)),
        BlockType.TaskCounterOnReach=> B(type, ("name", "c"), ("count", 5f)),
        BlockType.TaskCounterReset  => B(type, ("name", "c")),
        _                        => new BlockNode { Type = type },
    };

    private static BlockNode B(BlockType t, params (string k, object? v)[] ps)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in ps) d[k] = v;
        return new BlockNode { Type = t, Params = d };
    }
}
