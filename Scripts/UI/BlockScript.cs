namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem.VM;

// 自由畫布上的一條可拖曳積木鏈。
// Header 拖把整條腳本拖走；每個積木左側色條拖動時在該索引斷開並生成新腳本。
public partial class BlockScript : Control
{
    public List<BlockNode> Blocks { get; private set; }

    private readonly Func<List<(string display, string key)>>? _getSlotOpts;
    private readonly Action<BlockScript>                        _onChanged;
    private readonly Action<BlockScript, Vector2>               _onHeaderDrag;
    private readonly Action<BlockScript, int, Vector2>          _onBlockSplitDrag;

    private VBoxContainer _vbox = null!;

    internal const float BlockH    = 34f;
    internal const float BlockMinW = 200f;

    public BlockScript(
        List<BlockNode> blocks,
        Func<List<(string, string)>>? getSlotOpts,
        Action<BlockScript> onChanged,
        Action<BlockScript, Vector2> onHeaderDrag,
        Action<BlockScript, int, Vector2> onBlockSplitDrag)
    {
        Blocks            = blocks;
        _getSlotOpts      = getSlotOpts;
        _onChanged        = onChanged;
        _onHeaderDrag     = onHeaderDrag;
        _onBlockSplitDrag = onBlockSplitDrag;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        _vbox = new VBoxContainer();
        _vbox.AddThemeConstantOverride("separation", 0);
        _vbox.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_vbox);

        Rebuild();
    }

    // Re-render from Blocks
    public void Rebuild()
    {
        if (_vbox is null) return;

        while (_vbox.GetChildCount() > 0)
        {
            var c = _vbox.GetChild(0);
            _vbox.RemoveChild(c);
            c.QueueFree();
        }

        if (Blocks.Count == 0) return;

        _vbox.AddChild(MakeHeaderBar());
        for (int i = 0; i < Blocks.Count; i++)
            _vbox.AddChild(MakeBlockEntry(i));
    }

    // Narrow grip strip — drags the whole script
    private Control MakeHeaderBar()
    {
        var bar = new Panel();
        bar.CustomMinimumSize = new Vector2(BlockMinW, 6);
        var s = new StyleBoxFlat { BgColor = new Color(0.28f, 0.28f, 0.40f) };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight = 4;
        bar.AddThemeStyleboxOverride("panel", s);
        bar.MouseFilter = MouseFilterEnum.Stop;
        bar.MouseDefaultCursorShape = CursorShape.Drag;
        bar.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                _onHeaderDrag(this, mb.GlobalPosition);
                GetViewport().SetInputAsHandled();
            }
        };
        return bar;
    }

    // One block card + optional branch containers below it
    private Control MakeBlockEntry(int idx)
    {
        var block = Blocks[idx];
        var wrap  = new VBoxContainer();
        wrap.AddThemeConstantOverride("separation", 0);
        wrap.MouseFilter = MouseFilterEnum.Ignore;

        wrap.AddChild(MakeCard(idx, block, Blocks, topLevel: true));

        switch (block.Type)
        {
            case BlockType.If:
                wrap.AddChild(MakeBranch("成立時執行", block.ThenBranch));
                wrap.AddChild(MakeBranch("不成立時執行", block.ElseBranch));
                break;
            case BlockType.Evaluate:
                wrap.AddChild(MakeBranch("條件成立時執行", block.ThenBranch));
                break;
            case BlockType.RisingEdge:
                wrap.AddChild(MakeBranch("條件剛成立時執行", block.ThenBranch));
                break;
            case BlockType.FallingEdge:
                wrap.AddChild(MakeBranch("條件剛結束時執行", block.ThenBranch));
                break;
            case BlockType.SinglePulse:
                wrap.AddChild(MakeBranch("首次成立時執行", block.ThenBranch));
                break;
            case BlockType.AlternateTrigger:
                wrap.AddChild(MakeBranch("偶數次執行（0,2,4...）", block.ThenBranch));
                wrap.AddChild(MakeBranch("奇數次執行（1,3,5...）", block.ElseBranch));
                break;
            case BlockType.RandomChoice:
                wrap.AddChild(MakeBranch("選項 A（50%）", block.ThenBranch));
                wrap.AddChild(MakeBranch("選項 B（50%）", block.ElseBranch));
                break;
            case BlockType.RepeatN:
            case BlockType.RepeatWhile:
            case BlockType.ForEachNearby:
                wrap.AddChild(MakeBranch("每輪執行", block.LoopBody));
                break;
            case BlockType.TaskCounterOnReach:
                wrap.AddChild(MakeBranch("到達時執行（僅一次）", block.ThenBranch));
                break;
        }

        return wrap;
    }

    // Single block row card
    private Control MakeCard(int idx, BlockNode block, List<BlockNode> parent, bool topLevel)
    {
        var card = new Panel();
        card.CustomMinimumSize = new Vector2(BlockMinW, BlockH);
        var clr = ScratchCanvas.BlockColor(block.Type);
        var cardStyle = new StyleBoxFlat { BgColor = new Color(0.14f, 0.15f, 0.20f) };
        card.AddThemeStyleboxOverride("panel", cardStyle);
        card.MouseFilter = MouseFilterEnum.Ignore;

        var row = new HBoxContainer();
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.AddThemeConstantOverride("separation", 4);
        row.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(row);

        // Left colour strip — drag handle for split (top-level only)
        var strip = new Panel();
        strip.CustomMinimumSize = new Vector2(8, 0);
        strip.SizeFlagsVertical = SizeFlags.ExpandFill;
        var sStyle = new StyleBoxFlat { BgColor = clr };
        sStyle.CornerRadiusTopLeft = sStyle.CornerRadiusBottomLeft = 4;
        strip.AddThemeStyleboxOverride("panel", sStyle);
        if (topLevel)
        {
            strip.MouseFilter = MouseFilterEnum.Stop;
            strip.MouseDefaultCursorShape = CursorShape.Drag;
            int captIdx = idx;
            strip.GuiInput += @event =>
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                {
                    _onBlockSplitDrag(this, captIdx, mb.GlobalPosition);
                    GetViewport().SetInputAsHandled();
                }
            };
        }
        else
        {
            strip.MouseFilter = MouseFilterEnum.Ignore;
        }
        row.AddChild(strip);
        row.AddChild(Spacer(4));

        // Block name
        var lbl = new Label
        {
            Text               = ScratchCanvas.BlockName(block.Type),
            VerticalAlignment  = VerticalAlignment.Center,
        };
        lbl.AddThemeColorOverride("font_color", clr);
        lbl.AddThemeFontSizeOverride("font_size", 11);
        lbl.CustomMinimumSize = new Vector2(0, BlockH);
        lbl.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(lbl);
        row.AddChild(Spacer(4));

        // Parameters via descriptor
        if (ScratchCanvas.TryGetDescriptor(block.Type, out var desc) && desc is not null)
            desc.BuildUI?.Invoke(row, block, _getSlotOpts);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        // Delete button
        var del = new Button { Text = "✕", Flat = true };
        del.AddThemeColorOverride("font_color", new Color(0.60f, 0.20f, 0.20f));
        del.CustomMinimumSize = new Vector2(22, 0);
        del.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        del.AddThemeFontSizeOverride("font_size", 10);
        del.MouseFilter = MouseFilterEnum.Stop;
        var captParent = parent; var captIdx2 = idx;
        del.Pressed += () =>
        {
            captParent.RemoveAt(captIdx2);
            _onChanged(this);
            Rebuild();
        };
        row.AddChild(del);
        row.AddChild(Spacer(4));

        return card;
    }

    // C-shape container for a branch's child block list
    private Control MakeBranch(string label, List<BlockNode> children)
    {
        const float ArmW   = 14f;
        const float ArmClr = 0.22f;
        var arm   = new Color(ArmClr, 0.38f, ArmClr);
        var darkBg = new Color(0.09f, 0.11f, 0.09f);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 0);
        outer.MouseFilter = MouseFilterEnum.Ignore;

        // ── Content area ─────────────────────────────────────────────
        var body = new Panel();
        var bStyle = new StyleBoxFlat { BgColor = darkBg };
        bStyle.BorderWidthLeft = 2;
        bStyle.BorderColor = arm;
        body.AddThemeStyleboxOverride("panel", bStyle);
        body.MouseFilter = MouseFilterEnum.Ignore;

        var inner = new VBoxContainer();
        inner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        inner.AddThemeConstantOverride("separation", 0);
        inner.OffsetLeft  = ArmW;
        inner.OffsetTop   = 2f;
        inner.OffsetBottom = -2f;
        inner.MouseFilter = MouseFilterEnum.Ignore;
        body.AddChild(inner);

        var hdr = new Label { Text = label };
        hdr.AddThemeColorOverride("font_color", new Color(0.42f, 0.70f, 0.42f));
        hdr.AddThemeFontSizeOverride("font_size", 9);
        hdr.CustomMinimumSize = new Vector2(0, 14);
        hdr.VerticalAlignment = VerticalAlignment.Center;
        hdr.MouseFilter = MouseFilterEnum.Ignore;
        inner.AddChild(hdr);

        for (int ci = 0; ci < children.Count; ci++)
            inner.AddChild(MakeCard(ci, children[ci], children, topLevel: false));

        var addBtn = new Button { Text = "＋ 加入積木", Flat = true, Alignment = HorizontalAlignment.Left };
        addBtn.CustomMinimumSize = new Vector2(0, 20);
        addBtn.AddThemeColorOverride("font_color", new Color(0.40f, 0.65f, 0.40f));
        addBtn.AddThemeFontSizeOverride("font_size", 9);
        addBtn.MouseFilter = MouseFilterEnum.Stop;
        var captList = children;
        addBtn.Pressed += () =>
        {
            captList.Add(ScratchCanvas.MakeDefaultBlock(BlockType.InvokeTotem));
            _onChanged(this);
            Rebuild();
        };
        inner.AddChild(addBtn);

        outer.AddChild(body);

        // ── Closing strip (bottom of C) ───────────────────────────────
        var closeRow = new HBoxContainer();
        closeRow.MouseFilter = MouseFilterEnum.Ignore;
        closeRow.AddThemeConstantOverride("separation", 0);

        var armEnd = new Panel();
        armEnd.CustomMinimumSize = new Vector2(ArmW, 8);
        armEnd.SizeFlagsVertical = SizeFlags.Fill;
        var aStyle = new StyleBoxFlat { BgColor = arm };
        aStyle.CornerRadiusBottomLeft = 3;
        armEnd.AddThemeStyleboxOverride("panel", aStyle);
        armEnd.MouseFilter = MouseFilterEnum.Ignore;
        closeRow.AddChild(armEnd);

        var shelf = new Panel();
        shelf.CustomMinimumSize = new Vector2(0, 8);
        shelf.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        shelf.SizeFlagsVertical = SizeFlags.Fill;
        var shStyle = new StyleBoxFlat { BgColor = darkBg };
        shelf.AddThemeStyleboxOverride("panel", shStyle);
        shelf.MouseFilter = MouseFilterEnum.Ignore;
        closeRow.AddChild(shelf);

        outer.AddChild(closeRow);

        return outer;
    }

    // Split off blocks starting at idx; returns the detached blocks
    public List<BlockNode> SplitAt(int idx)
    {
        if (idx <= 0 || idx >= Blocks.Count) return new List<BlockNode>();
        var taken = Blocks.GetRange(idx, Blocks.Count - idx);
        Blocks.RemoveRange(idx, Blocks.Count - idx);
        Rebuild();
        return taken;
    }

    // Append blocks from a floating script that was dropped on this one
    public void AppendBlocks(List<BlockNode> incoming)
    {
        Blocks.AddRange(incoming);
        _onChanged(this);
        Rebuild();
    }

    // Global-space snap points for connection detection
    public Vector2 GetTopSnapGlobal()
    {
        if (_vbox == null) return GlobalPosition;
        return _vbox.GlobalPosition + new Vector2(BlockMinW * 0.5f, 0f);
    }

    public Vector2 GetBottomSnapGlobal()
    {
        if (_vbox == null) return GlobalPosition;
        return _vbox.GlobalPosition + new Vector2(BlockMinW * 0.5f, _vbox.Size.Y);
    }

    private static Control Spacer(int w) =>
        new Control { CustomMinimumSize = new Vector2(w, 0), MouseFilter = MouseFilterEnum.Ignore };
}
