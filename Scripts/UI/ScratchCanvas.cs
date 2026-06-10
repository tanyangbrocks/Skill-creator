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

        // ── 巢狀容器（If / Evaluate / RepeatN / RepeatWhile）────────
        if (block.Type == BlockType.If)
        {
            outer.AddChild(BuildBranch(block.ThenBranch, "成立時執行", indent + 1));
            outer.AddChild(BuildBranch(block.ElseBranch, "不成立時執行", indent + 1));
        }

        if (block.Type == BlockType.Evaluate)
            outer.AddChild(BuildBranch(block.ThenBranch, "條件成立時執行", indent + 1));

        if (block.Type == BlockType.RepeatN   ||
            block.Type == BlockType.RepeatWhile ||
            block.Type == BlockType.ForEachNearby)
            outer.AddChild(BuildBranch(block.LoopBody, "每輪執行", indent + 1));

        if (block.Type == BlockType.RandomChoice)
        {
            outer.AddChild(BuildBranch(block.ThenBranch, "選項 A（50%）", indent + 1));
            outer.AddChild(BuildBranch(block.ElseBranch, "選項 B（50%）", indent + 1));
        }

        if (block.Type == BlockType.AlternateTrigger)
        {
            outer.AddChild(BuildBranch(block.ThenBranch, "偶數次執行（0,2,4...）", indent + 1));
            outer.AddChild(BuildBranch(block.ElseBranch, "奇數次執行（1,3,5...）", indent + 1));
        }

        if (block.Type == BlockType.TaskCounterOnReach)
            outer.AddChild(BuildBranch(block.ThenBranch, "到達時執行（僅一次）", indent + 1));

        if (block.Type == BlockType.SinglePulse)
            outer.AddChild(BuildBranch(block.ThenBranch, "條件首次成立時執行", indent + 1));

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
        => new DragHandle(idx, parent, parent[idx], clr);

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
        => new DropZone(insertAt, parent, OnChanged);

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
    private static readonly Color CVec   = new(0.30f, 0.88f, 0.80f); // 青綠 Vector
    private static readonly Color CGray  = new(0.75f, 0.75f, 0.75f); // 灰   fallback

    // 積木描述器（顏色、UI 名稱、預設積木工廠、參數 UI 建構器）
    // BuildUI = null 表示無參數列
    internal record BlockDescriptor(
        Color  Color,
        string Name,
        Func<BlockNode> Make,
        Action<HBoxContainer, BlockNode, ScratchCanvas>? BuildUI = null
    );

    // 條件型參數 UI（如果 / 條件成立重複 共用）
    private static Action<HBoxContainer, BlockNode, ScratchCanvas> ConditionUI => (row, block, canvas) =>
    {
        string[] types  = { "totemDone", "totemHit", "totemFizzle", "compare", "varBool" };
        string[] labels = { "技能完成",  "技能命中",  "技能失效",    "數值比較", "布林變數" };
        row.AddChild(SmallDrop(block, "conditionType", types, labels, 72));
        string cType = block.Params.TryGetValue("conditionType", out var cv) ? cv?.ToString() ?? "" : "";
        if (cType == "compare")
        {
            row.AddChild(SmallEdit(block, "left", "左值", 44));
            string[] ops = { ">", "<", "=", "≠", ">=", "<=" };
            row.AddChild(SmallDrop(block, "op", ops, ops, 40));
            row.AddChild(SmallEdit(block, "right", "右值", 44));
        }
        else if (cType == "varBool")
            row.AddChild(SmallEdit(block, "varName", "變數名", 72));
        else
            row.AddChild(canvas.SlotPicker(block, "totemName"));
    };

    // 集中管理所有積木型別的 descriptor
    private static readonly Dictionary<BlockType, BlockDescriptor> _descs = new()
    {
        // ── 技能呼叫 ──────────────────────────────────────────────────
        { BlockType.InvokeTotem,  new(COrng, "使用技能", () => B(BlockType.InvokeTotem,  ("totemName", "")),
            (r, b, c) => r.AddChild(c.SlotPicker(b, "totemName"))) },
        { BlockType.InvokeSpell,  new(COrng, "施放其他法陣", () => B(BlockType.InvokeSpell,  ("spellName", "")),
            (r, b, _) => r.AddChild(SmallEdit(b, "spellName", "法陣名", 90))) },

        // ── 控制流 ────────────────────────────────────────────────────
        { BlockType.If,           new(CFlow, "如果",        () => B(BlockType.If, ("conditionType", "totemDone"), ("totemName", "")),
            ConditionUI) },
        { BlockType.RepeatN,      new(CFlow, "重複 N 次",   () => B(BlockType.RepeatN,   ("count", 2f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "count", 1f, 20f, 1f, 36)); r.AddChild(TinyLbl("次")); }) },
        { BlockType.RepeatWhile,  new(CFlow, "條件成立，重複", () => B(BlockType.RepeatWhile, ("conditionType", "compare"), ("left", "x"), ("op", ">"), ("right", "0")),
            ConditionUI) },
        { BlockType.RandomChoice, new(CFlow, "隨機選擇",    () => B(BlockType.RandomChoice, ("count", 2f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "count", 2f, 8f, 1f, 36)); r.AddChild(TinyLbl("支")); }) },
        { BlockType.ForEachNearby,new(CFlow, "對每個附近敵人", () => B(BlockType.ForEachNearby, ("radius", 5f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "radius", 1f, 30f, 1f, 40)); r.AddChild(TinyLbl("格內")); }) },
        { BlockType.Evaluate,     new(CFlow, "條件成立執行（無 ELSE）", () => B(BlockType.Evaluate, ("conditionType", "compare"), ("left", "x"), ("op", ">"), ("right", "0")),
            ConditionUI) },
        { BlockType.Die,          new(CRed,  "終止法陣",     () => B(BlockType.Die),
            null) },

        // ── 時序 ──────────────────────────────────────────────────────
        { BlockType.Wait,         new(CGrn,  "等待",        () => B(BlockType.Wait, ("duration", 1f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "duration", 0.1f, 30f, 0.1f, 42)); r.AddChild(TinyLbl("秒")); }) },
        { BlockType.Sleep,        new(CGrn,  "等待 N 幀",   () => B(BlockType.Sleep, ("frames", 1f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "frames", 1f, 300f, 1f, 44)); r.AddChild(TinyLbl("幀")); }) },

        // ── 觸發時機 ──────────────────────────────────────────────────
        { BlockType.RisingEdge,   new(CCyan, "條件剛成立時",
            () => B(BlockType.RisingEdge,  ("conditionType","compare"),("left","x"),("op",">"),("right","0")),
            ConditionUI) },
        { BlockType.FallingEdge,  new(CCyan, "條件剛結束時",
            () => B(BlockType.FallingEdge, ("conditionType","compare"),("left","x"),("op",">"),("right","0")),
            ConditionUI) },
        { BlockType.SinglePulse,  new(CCyan, "條件首次成立時",
            () => B(BlockType.SinglePulse, ("conditionType","compare"),("left","x"),("op",">"),("right","0")),
            ConditionUI) },

        // ── 變數 ──────────────────────────────────────────────────────
        { BlockType.SetVar,       new(CYlw,  "設定變數",    () => B(BlockType.SetVar, ("name", "x"), ("value", "0"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "變數名", 52)); r.AddChild(TinyLbl("="));
                           r.AddChild(SmallEdit(b, "value", "值", 52)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.GetVar,       new(CYlw,  "讀取變數",    () => B(BlockType.GetVar, ("name", "x")),
            (r, b, _) => r.AddChild(SmallEdit(b, "name", "變數名", 72))) },
        { BlockType.SetVarBool,   new(CYlw,  "設定布林值",  () => B(BlockType.SetVarBool, ("name", "b"), ("value", "true"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "變數名", 52)); r.AddChild(TinyLbl("="));
                           r.AddChild(SmallDrop(b, "value", new[]{"true","false"}, new[]{"真","假"}, 44));
                           r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.GetVarBool,   new(CYlw,  "讀取布林值",  () => B(BlockType.GetVarBool, ("name", "b"), ("resultVar", "_bool"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "源變數", 52)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b, "resultVar", "存入變數", 56));
                           r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.Compare,      new(CYlw,  "比較數值",    () => B(BlockType.Compare, ("left", "x"), ("op", "="), ("right", "0"), ("resultVar", "result"), ("global", false)),
            (r, b, _) =>
            {
                r.AddChild(SmallEdit(b, "left", "左值", 44));
                string[] ops = { ">", "<", "=", "≠", ">=", "<=" };
                r.AddChild(SmallDrop(b, "op", ops, ops, 40));
                r.AddChild(SmallEdit(b, "right", "右值", 44));
                r.AddChild(TinyLbl("存入"));
                r.AddChild(SmallEdit(b, "resultVar", "變數名", 56));
                r.AddChild(CheckBox(b, "global", "全域"));
            }) },

        // ── 執行追蹤 ──────────────────────────────────────────────────
        { BlockType.LoopcastIndex,new(CYlw,  "本陣觸發次數", () => B(BlockType.LoopcastIndex, ("resultVar", "loop_idx"), ("global", false)),
            (r, b, _) => { r.AddChild(TinyLbl("→")); r.AddChild(SmallEdit(b, "resultVar", "存入變數", 64)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.SuccessCount, new(CYlw,  "命中圖騰數量", () => B(BlockType.SuccessCount,  ("resultVar", "hit_count"), ("global", false)),
            (r, b, _) => { r.AddChild(TinyLbl("→")); r.AddChild(SmallEdit(b, "resultVar", "存入變數", 64)); r.AddChild(CheckBox(b, "global", "全域")); }) },

        // ── 列表 ──────────────────────────────────────────────────────
        { BlockType.ListCreate,   new(COrnD, "建立列表",        () => B(BlockType.ListCreate, ("name", "list"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 72)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListAppend,   new(COrnD, "列表加入末尾",    () => B(BlockType.ListAppend, ("name", "list"), ("value", "0"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("+"));
                           r.AddChild(SmallEdit(b, "value", "值/變數", 60)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListPop,      new(COrnD, "取出列表末尾",    () => B(BlockType.ListPop,    ("name", "list"), ("resultVar", "v"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b, "resultVar", "存入變數", 56)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListGet,      new(COrnD, "讀取列表第 N 項", () => B(BlockType.ListGet,    ("name", "list"), ("index", "1"), ("resultVar", "v"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("第"));
                           r.AddChild(SmallEdit(b, "index", "N", 40)); r.AddChild(TinyLbl("項→"));
                           r.AddChild(SmallEdit(b, "resultVar", "存入變數", 52)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListDequeue,  new(COrnD, "取出列表第一項",  () => B(BlockType.ListDequeue, ("name", "list"), ("resultVar", "v"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b, "resultVar", "存入變數", 56)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListSet,      new(COrnD, "修改列表第 N 項", () => B(BlockType.ListSet,     ("name", "list"), ("index", "1"), ("value", "0"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("第"));
                           r.AddChild(SmallEdit(b, "index", "N", 36)); r.AddChild(TinyLbl("項="));
                           r.AddChild(SmallEdit(b, "value", "值/變數", 56)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListLength,   new(COrnD, "取得列表長度",    () => B(BlockType.ListLength,  ("name", "list"), ("resultVar", "len"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("長度→"));
                           r.AddChild(SmallEdit(b, "resultVar", "存入變數", 56)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListContains, new(COrnD, "列表是否包含",    () => B(BlockType.ListContains,("name", "list"), ("value", "0"), ("resultVar", "found"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("含"));
                           r.AddChild(SmallEdit(b, "value", "值/變數", 56)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b, "resultVar", "結果變數", 52)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListRemoveAt, new(COrnD, "刪除列表第 N 項", () => B(BlockType.ListRemoveAt,("name", "list"), ("index", "1"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 64)); r.AddChild(TinyLbl("刪第"));
                           r.AddChild(SmallEdit(b, "index", "N", 40)); r.AddChild(TinyLbl("項")); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.ListClear,    new(COrnD, "清空列表",        () => B(BlockType.ListClear,   ("name", "list"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "列表名", 72)); r.AddChild(CheckBox(b, "global", "全域")); }) },

        // ── 敵人查詢 ──────────────────────────────────────────────────
        { BlockType.QueryNear,    new(CBlue, "附近敵人數量",    () => B(BlockType.QueryNear, ("radius", 5f), ("resultVar", "nearby")),
            (r, b, _) => { r.AddChild(SmallSpin(b, "radius", 1f, 30f, 1f, 40)); r.AddChild(TinyLbl("格內→"));
                           r.AddChild(SmallEdit(b, "resultVar", "存入變數", 60)); }) },
        { BlockType.QueryNearest, new(CBlue, "最近的敵人",      () => B(BlockType.QueryNearest, ("radius", 5f), ("resultVar", "nearest")),
            (r, b, _) => { r.AddChild(SmallSpin(b, "radius", 1f, 30f, 1f, 40)); r.AddChild(TinyLbl("格內 →"));
                           r.AddChild(SmallEdit(b, "resultVar", "變數前綴", 60)); }) },
        { BlockType.GetEntityProp,new(CBlue, "讀取敵人屬性",    () => B(BlockType.GetEntityProp, ("property", "hp"), ("resultVar", "e_hp")),
            (r, b, _) =>
            {
                string[] props = { "hp", "maxhp", "x", "y" };
                string[] plbls = { "生命值", "最大生命", "X 座標", "Y 座標" };
                r.AddChild(SmallDrop(b, "property", props, plbls, 60));
                r.AddChild(TinyLbl("→"));
                r.AddChild(SmallEdit(b, "resultVar", "存入變數", 64));
            }) },
        { BlockType.SetEntityProp,new(CBlue, "設定敵人屬性",    () => B(BlockType.SetEntityProp, ("property", "hp"), ("damage", 10f)),
            (r, b, _) =>
            {
                string[] props = { "hp", "x", "y" };
                string[] plbls = { "生命值", "X 座標", "Y 座標" };
                r.AddChild(SmallDrop(b, "property", props, plbls, 52));
                string prop = b.Params.TryGetValue("property", out var pv) ? pv?.ToString() ?? "hp" : "hp";
                if (prop == "hp")
                {
                    r.AddChild(TinyLbl("扣除"));
                    r.AddChild(SmallSpin(b, "damage", 1f, 999f, 1f, 52));
                }
                else
                {
                    r.AddChild(TinyLbl("設為"));
                    r.AddChild(SmallEdit(b, "value", "值/變數", 60));
                }
            }) },

        // ── 廣播訊號 ──────────────────────────────────────────────────
        { BlockType.Broadcast,       new(CPurp, "廣播訊號",          () => B(BlockType.Broadcast,       ("signal", "")),
            (r, b, _) => r.AddChild(SmallEdit(b, "signal", "訊號名", 80))) },
        { BlockType.BroadcastAndWait,new(CPurp, "廣播訊號（等同廣播）", () => B(BlockType.BroadcastAndWait,("signal", "")),
            (r, b, _) => r.AddChild(SmallEdit(b, "signal", "訊號名", 80))) },
        { BlockType.OnReceive,       new(CPurp, "收到訊號時",          () => B(BlockType.OnReceive,       ("signal", "")),
            (r, b, _) => r.AddChild(SmallEdit(b, "signal", "訊號名", 80))) },

        // ── 向量運算 ──────────────────────────────────────────────────
        { BlockType.VecMake,      new(CVec, "建立向量",     () => B(BlockType.VecMake,      ("name","v"),("x","0"),("y","0"),("global",false)),
            (r, b, _) => { r.AddChild(SmallEdit(b,"name","向量名",52)); r.AddChild(TinyLbl("=("));
                           r.AddChild(SmallEdit(b,"x","x",40)); r.AddChild(TinyLbl(","));
                           r.AddChild(SmallEdit(b,"y","y",40)); r.AddChild(TinyLbl(")"));
                           r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecGetComp,   new(CVec, "取向量分量",   () => B(BlockType.VecGetComp,   ("name","v"),("comp","x"),("resultVar","val"),("global",false)),
            (r, b, _) => { r.AddChild(SmallEdit(b,"name","向量名",52)); r.AddChild(TinyLbl("."));
                           r.AddChild(SmallDrop(b,"comp",new[]{"x","y"},new[]{"x","y"},36));
                           r.AddChild(TinyLbl("→")); r.AddChild(SmallEdit(b,"resultVar","存入",48));
                           r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecAdd,       new(CVec, "向量加法",     () => B(BlockType.VecAdd,       ("vecA","a"),("vecB","b"),("result","r"),("global",false)),
            (r, b, _) => { r.AddChild(SmallEdit(b,"vecA","A",44)); r.AddChild(TinyLbl("+"));
                           r.AddChild(SmallEdit(b,"vecB","B",44)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b,"result","結果",48)); r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecSub,       new(CVec, "向量減法",     () => B(BlockType.VecSub,       ("vecA","a"),("vecB","b"),("result","r"),("global",false)),
            (r, b, _) => { r.AddChild(SmallEdit(b,"vecA","A",44)); r.AddChild(TinyLbl("−"));
                           r.AddChild(SmallEdit(b,"vecB","B",44)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b,"result","結果",48)); r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecScale,     new(CVec, "向量縮放",     () => B(BlockType.VecScale,     ("vec","v"),("scalar","1"),("result","r"),("global",false)),
            (r, b, _) => { r.AddChild(SmallEdit(b,"vec","向量",48)); r.AddChild(TinyLbl("×"));
                           r.AddChild(SmallEdit(b,"scalar","純量",40)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b,"result","結果",48)); r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecNegate,    new(CVec, "向量反向",     () => B(BlockType.VecNegate,    ("vec","v"),("result","r"),("global",false)),
            (r, b, _) => { r.AddChild(TinyLbl("−")); r.AddChild(SmallEdit(b,"vec","向量",48));
                           r.AddChild(TinyLbl("→")); r.AddChild(SmallEdit(b,"result","結果",48));
                           r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecNorm,      new(CVec, "向量正規化（轉為單位向量）", () => B(BlockType.VecNorm, ("vec","v"),("result","r"),("global",false)),
            (r, b, _) => { r.AddChild(TinyLbl("norm(")); r.AddChild(SmallEdit(b,"vec","向量",48));
                           r.AddChild(TinyLbl(")→")); r.AddChild(SmallEdit(b,"result","結果",48));
                           r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecLength,    new(CVec, "向量長度",     () => B(BlockType.VecLength,    ("vec","v"),("resultVar","len"),("global",false)),
            (r, b, _) => { r.AddChild(TinyLbl("|")); r.AddChild(SmallEdit(b,"vec","向量",48));
                           r.AddChild(TinyLbl("|→")); r.AddChild(SmallEdit(b,"resultVar","存入",48));
                           r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecDot,       new(CVec, "向量點積（內積）", () => B(BlockType.VecDot,    ("vecA","a"),("vecB","b"),("resultVar","dot"),("global",false)),
            (r, b, _) => { r.AddChild(SmallEdit(b,"vecA","A",44)); r.AddChild(TinyLbl("·"));
                           r.AddChild(SmallEdit(b,"vecB","B",44)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b,"resultVar","存入",48)); r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecCross,     new(CVec, "向量叉積（外積）", () => B(BlockType.VecCross,  ("vecA","a"),("vecB","b"),("resultVar","cross"),("global",false)),
            (r, b, _) => { r.AddChild(SmallEdit(b,"vecA","A",44)); r.AddChild(TinyLbl("×"));
                           r.AddChild(SmallEdit(b,"vecB","B",44)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b,"resultVar","存入",48)); r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.VecFromEntity,new(CVec, "目標位置→向量",  () => B(BlockType.VecFromEntity, ("result","e_pos"),("global",false)),
            (r, b, _) => { r.AddChild(TinyLbl("→")); r.AddChild(SmallEdit(b,"result","結果向量",64));
                           r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.FocalPoint,   new(CVec, "游標所在位置",    () => B(BlockType.FocalPoint, ("result","focal"),("global",false)),
            (r, b, _) => { r.AddChild(TinyLbl("→")); r.AddChild(SmallEdit(b,"result","結果向量",64));
                           r.AddChild(CheckBox(b,"global","全域")); }) },
        { BlockType.Raycast,      new(CVec, "射線投射",     () => B(BlockType.Raycast, ("startVec","pos"),("dirVec","dir"),("maxDist","20"),("resultVec","ray"),("global",false)),
            (r, b, _) => { r.AddChild(TinyLbl("從")); r.AddChild(SmallEdit(b,"startVec","起點向量",52));
                           r.AddChild(TinyLbl("向")); r.AddChild(SmallEdit(b,"dirVec","方向向量",52));
                           r.AddChild(TinyLbl("射")); r.AddChild(SmallEdit(b,"maxDist","最遠格數",44));
                           r.AddChild(TinyLbl("格→")); r.AddChild(SmallEdit(b,"resultVec","結果前綴",48));
                           r.AddChild(CheckBox(b,"global","全域")); }) },

        // ── 偵測條件（被動觸發，等待條件後繼續執行）──────────────────
        { BlockType.DetectHpThreshold,new(CRed, "生命值低於 N%",  () => B(BlockType.DetectHpThreshold, ("percent", 30f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "percent", 1f, 99f, 1f, 44)); r.AddChild(TinyLbl("%")); }) },
        { BlockType.DetectMpThreshold,new(CRed, "魔力值低於 N%",  () => B(BlockType.DetectMpThreshold, ("percent", 30f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "percent", 1f, 99f, 1f, 44)); r.AddChild(TinyLbl("%")); }) },
        { BlockType.DetectHitReceived, new(CRed, "受到攻擊時",       () => B(BlockType.DetectHitReceived)) },
        { BlockType.DetectEntityEnter, new(CRed, "敵人進入範圍時",   () => B(BlockType.DetectEntityEnter, ("faction", "敵方"), ("radius", 5f)),
            (r, b, _) => { r.AddChild(SmallSpin(b, "radius", 1f, 30f, 1f, 40)); r.AddChild(TinyLbl("格內有敵人")); }) },

        // ── 戰鬥統計查詢 ───────────────────────────────────────────────
        { BlockType.GetBattleStat, new(new Color(0.15f,0.55f,0.55f), "本場戰鬥統計",
            () => B(BlockType.GetBattleStat, ("stat", "castCount"), ("resultVar", "_stat")),
            (r, b, _) => {
                var opts = new[] { ("castCount","施放次數"), ("damageDealt","造成傷害"), ("killCount","擊殺數") };
                var dd = new OptionButton(); dd.CustomMinimumSize = new Vector2(80, 0);
                foreach (var (v, lbl) in opts) dd.AddItem(lbl);
                string curStat = b.Params.TryGetValue("stat", out var s) && s is string sv ? sv : "castCount";
                int cur = Array.FindIndex(opts, x => x.Item1 == curStat);
                dd.Selected = Math.Max(0, cur);
                dd.ItemSelected += idx => { b.Params["stat"] = opts[idx].Item1; };
                r.AddChild(dd);
                r.AddChild(TinyLbl("→")); r.AddChild(SmallEdit(b, "resultVar", "變數名", 64));
            }) },

        // ── 任務計數器 ────────────────────────────────────────────────
        { BlockType.TaskCounterSet,   new(CLvnd, "計數器設定值",  () => B(BlockType.TaskCounterSet,    ("name", "c"), ("count", 0f)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "計數器名", 64)); r.AddChild(SmallSpin(b, "count", 0f, 999f, 1f, 44)); }) },
        { BlockType.TaskCounterAdd,   new(CLvnd, "計數器增加",    () => B(BlockType.TaskCounterAdd,    ("name", "c"), ("count", 1f)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "計數器名", 64)); r.AddChild(SmallSpin(b, "count", 0f, 999f, 1f, 44)); }) },
        { BlockType.TaskCounterGet,   new(CLvnd, "計數器讀值",    () => B(BlockType.TaskCounterGet,    ("name", "c"), ("resultVar", "c_val"), ("global", false)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "計數器名", 64)); r.AddChild(TinyLbl("→"));
                           r.AddChild(SmallEdit(b, "resultVar", "存入變數", 64)); r.AddChild(CheckBox(b, "global", "全域")); }) },
        { BlockType.TaskCounterOnReach,new(CLvnd,"計數器到達時",  () => B(BlockType.TaskCounterOnReach,("name", "c"), ("count", 5f)),
            (r, b, _) => { r.AddChild(SmallEdit(b, "name", "計數器名", 64)); r.AddChild(SmallSpin(b, "count", 0f, 999f, 1f, 44)); }) },
        { BlockType.TaskCounterReset, new(CLvnd, "計數器歸零",    () => B(BlockType.TaskCounterReset,  ("name", "c")),
            (r, b, _) => r.AddChild(SmallEdit(b, "name", "計數器名", 64))) },

        // ── Phase 4：行動攔截積木 ────────────────────────────────────
        { BlockType.DamageShield, new(new Color(0.35f, 0.78f, 0.95f), "攔截下一次受傷",
            () => B(BlockType.DamageShield, ("mode", "cancel"), ("threshold", 0f), ("oneShot", true)),
            (r, b, _) => {
                string[] modes  = { "cancel", "halve", "cap" };
                string[] labels = { "完全免傷", "減半", "傷害封頂" };
                r.AddChild(SmallDrop(b, "mode", modes, labels, 72));
                string m = b.Params.TryGetValue("mode", out var mv) ? mv?.ToString() ?? "cancel" : "cancel";
                if (m == "cap")
                {
                    r.AddChild(TinyLbl("封頂值"));
                    r.AddChild(SmallSpin(b, "capValue", 1f, 9999f, 1f, 52));
                }
                r.AddChild(TinyLbl("門檻≥"));
                r.AddChild(SmallSpin(b, "threshold", 0f, 9999f, 1f, 52));
                r.AddChild(CheckBox(b, "oneShot", "一次性"));
            }) },
        { BlockType.DeathGuard, new(new Color(0.35f, 0.78f, 0.95f), "攔截下一次死亡（存活 1HP）",
            () => B(BlockType.DeathGuard, ("oneShot", true)),
            (r, b, _) => r.AddChild(CheckBox(b, "oneShot", "一次性"))) },

        // ── Phase 4：狀態快照積木（S-11，法則刻印色系，群星 LV50+）────
        { BlockType.Anchor, new(new Color(0.72f, 0.28f, 0.95f), "錨點刻印（群星 LV50+）",
            () => B(BlockType.Anchor, ("radius", 10f)),
            (r, b, _) => {
                r.AddChild(TinyLbl("半徑"));
                r.AddChild(SmallSpin(b, "radius", 1f, 60f, 1f, 44));
                r.AddChild(TinyLbl("格"));
            }) },
        { BlockType.Rollback, new(new Color(0.72f, 0.28f, 0.95f), "回溯刻印（群星 LV50+）",
            () => B(BlockType.Rollback),
            null) },

        // ── 規劃中積木（UI 已接入，VM 實作待補；執行時顯示警告）─────
        { BlockType.Discard, new(CGray, "捨棄輸出",
            () => B(BlockType.Discard), null) },

        { BlockType.SequentialGate, new(CFlow, "按順序輪流執行",
            () => B(BlockType.SequentialGate, ("stages", 3f)),
            (r, b, _) => {
                r.AddChild(TinyLbl("階段"));
                r.AddChild(SmallSpin(b, "stages", 2f, 10f, 1f, 40));
            }) },

        { BlockType.AlternateTrigger, new(CFlow, "奇/偶次輪流執行",
            () => B(BlockType.AlternateTrigger), null) },

        { BlockType.EndOfChain, new(CGrn, "連擊結束時",
            () => B(BlockType.EndOfChain), null) },

        { BlockType.GetComboCount, new(CYlw, "讀取連擊數",
            () => B(BlockType.GetComboCount, ("resultVar", "combo"), ("global", false)),
            (r, b, _) => {
                r.AddChild(TinyLbl("→"));
                r.AddChild(SmallEdit(b, "resultVar", "變數", 60));
                r.AddChild(CheckBox(b, "global", "全域"));
            }) },

        { BlockType.DetectProjectile, new(CRed, "投射物進入範圍時",
            () => B(BlockType.DetectProjectile, ("radius", 5f)),
            (r, b, _) => {
                r.AddChild(TinyLbl("半徑"));
                r.AddChild(SmallSpin(b, "radius", 1f, 30f, 1f, 40));
                r.AddChild(TinyLbl("格"));
            }) },

        { BlockType.DetectAttack, new(CRed, "敵方蓄力攻擊時",
            () => B(BlockType.DetectAttack), null) },

        { BlockType.DetectStatusChange, new(CRed, "狀態變化時",
            () => B(BlockType.DetectStatusChange, ("status", "")),
            (r, b, _) => {
                r.AddChild(TinyLbl("狀態"));
                r.AddChild(SmallEdit(b, "status", "狀態名", 70));
            }) },

        { BlockType.SetActivationInstant,    new(COrng, "設為即時施放", () => B(BlockType.SetActivationInstant),    null) },
        { BlockType.SetActivationDeclare,    new(COrng, "設為宣告施放", () => B(BlockType.SetActivationDeclare),    null) },
        { BlockType.SetActivationSustained,  new(COrng, "設為持續施放", () => B(BlockType.SetActivationSustained),  null) },

        { BlockType.EffectLabel, new(new Color(0.40f, 0.65f, 0.45f), "效果標籤",
            () => B(BlockType.EffectLabel, ("label", "")),
            (r, b, _) => {
                r.AddChild(TinyLbl("標籤"));
                r.AddChild(SmallEdit(b, "label", "標籤名", 80));
            }) },

        { BlockType.OnEffectStart, new(new Color(0.40f, 0.65f, 0.45f), "效果開始時",
            () => B(BlockType.OnEffectStart, ("label", "")),
            (r, b, _) => {
                r.AddChild(TinyLbl("標籤"));
                r.AddChild(SmallEdit(b, "label", "標籤名", 80));
            }) },

        { BlockType.OnEffectEnd, new(new Color(0.40f, 0.65f, 0.45f), "效果結束時",
            () => B(BlockType.OnEffectEnd, ("label", "")),
            (r, b, _) => {
                r.AddChild(TinyLbl("標籤"));
                r.AddChild(SmallEdit(b, "label", "標籤名", 80));
            }) },
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

    // ══════════════════════════════════════════════════════════════
    //  DragHandle — 使用 Godot 原生 _GetDragData 繞過 ScrollContainer
    // ══════════════════════════════════════════════════════════════

    private sealed partial class DragHandle : Panel
    {
        private readonly int             _idx;
        private readonly List<BlockNode> _parent;
        private readonly BlockNode       _block;

        public DragHandle(int idx, List<BlockNode> parent, BlockNode block, Color clr)
        {
            _idx = idx; _parent = parent; _block = block;
            CustomMinimumSize = new Vector2(8, 0);
            SizeFlagsVertical = SizeFlags.ExpandFill;
            MouseDefaultCursorShape = CursorShape.Drag;
            var s = new StyleBoxFlat { BgColor = clr };
            s.CornerRadiusTopLeft = s.CornerRadiusBottomLeft = 4;
            AddThemeStyleboxOverride("panel", s);
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            SetDragPreview(BuildDragPreview(_block));
            BlockDrag.BeginMove(_idx, _parent, _block);
            return new Godot.Collections.Dictionary { ["scratch"] = _idx };
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  DropZone — 使用 _CanDropData / _DropData 接受原生拖放
    // ══════════════════════════════════════════════════════════════

    private sealed partial class DropZone : Control
    {
        private readonly int             _insertAt;
        private readonly List<BlockNode> _parent;
        private readonly Action          _onChanged;
        private readonly StyleBoxFlat    _lineStyle;

        public DropZone(int insertAt, List<BlockNode> parent, Action onChanged)
        {
            _insertAt = insertAt; _parent = parent; _onChanged = onChanged;
            CustomMinimumSize = new Vector2(0, 6);
            MouseFilter = MouseFilterEnum.Stop;

            var line = new Panel();
            line.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            line.OffsetTop = 2; line.OffsetBottom = -2;
            _lineStyle = new StyleBoxFlat { BgColor = new Color(0.4f, 0.8f, 0.4f, 0f) };
            _lineStyle.CornerRadiusTopLeft = _lineStyle.CornerRadiusTopRight =
            _lineStyle.CornerRadiusBottomLeft = _lineStyle.CornerRadiusBottomRight = 2;
            line.AddThemeStyleboxOverride("panel", _lineStyle);
            AddChild(line);

            MouseEntered += () => { if (BlockDrag.Active) _lineStyle.BgColor = new Color(0.4f, 0.8f, 0.4f, 1f); };
            MouseExited  += () => _lineStyle.BgColor = new Color(0.4f, 0.8f, 0.4f, 0f);
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
            => BlockDrag.Active && data.AsGodotDictionary().ContainsKey("scratch");

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            _lineStyle.BgColor = new Color(0.4f, 0.8f, 0.4f, 0f);
            if (!BlockDrag.Active) return;

            var block    = BlockDrag.Block!;
            int insertAt = _insertAt;

            if (BlockDrag.SourceList != null)
            {
                var srcList = BlockDrag.SourceList;
                int srcIdx  = BlockDrag.SourceIdx;
                if (ReferenceEquals(srcList, _parent) && srcIdx < insertAt) insertAt--;
                if (ReferenceEquals(srcList, _parent) && insertAt == srcIdx)
                { BlockDrag.Clear(); return; }
                srcList.RemoveAt(srcIdx);
            }

            _parent.Insert(Math.Clamp(insertAt, 0, _parent.Count), block);
            BlockDrag.Clear();
            _onChanged();
        }
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
