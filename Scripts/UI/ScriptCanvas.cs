namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem.VM;

// 自由畫布 — 以絕對座標管理多條 BlockScript，替換 VBox 式的 ScratchCanvas。
//
// 公開介面與 ScratchCanvas 相同：
//   SyncFrom(blocks, getSlotOptions)
//   event Action? Changed
//
// 互動邏輯：
//   • Header 拖把（BlockScript 頂端細條）→ 拖整條腳本
//   • 積木色條拖把 → 在該索引斷開，生成新浮動腳本
//   • 放開時靠近另一條腳本底端 → 自動連結（磁吸距離 40px）
public partial class ScriptCanvas : Control
{
    public event Action? Changed;

    private List<BlockNode>                       _blocks     = new();
    private Func<List<(string, string)>>?         _getSlotOpts;
    private readonly List<BlockScript>            _scripts    = new();
    private Control                               _freeCanvas = null!;

    // Drag state
    private BlockScript? _dragging;
    private Vector2      _dragOffset;

    // Index 0 = main script (maps to _blocks / SpellArray.Blocks)
    private BlockScript? MainScript => _scripts.Count > 0 ? _scripts[0] : null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ClipContents = true;

        var bg = new ColorRect { Color = new Color(0.08f, 0.09f, 0.13f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

        _freeCanvas = new Control();
        _freeCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _freeCanvas.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_freeCanvas);

        var hint = new Label { Text = "← 點擊積木庫以加入積木" };
        hint.AddThemeColorOverride("font_color", new Color(0.35f, 0.35f, 0.45f));
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        hint.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(hint);
    }

    // ── 公開 API ─────────────────────────────────────────────────────

    public void SyncFrom(List<BlockNode> blocks, Func<List<(string, string)>>? getSlotOpts)
    {
        _blocks      = blocks;
        _getSlotOpts = getSlotOpts;
        _dragging    = null;

        foreach (var s in _scripts)
        {
            _freeCanvas.RemoveChild(s);
            s.QueueFree();
        }
        _scripts.Clear();

        if (_blocks.Count > 0)
            SpawnScript(_blocks, new Vector2(20f, 20f), isMain: true);
    }

    // ── 腳本生成 ──────────────────────────────────────────────────────

    private BlockScript SpawnScript(List<BlockNode> blocks, Vector2 localPos, bool isMain)
    {
        bool captMain = isMain;
        var script = new BlockScript(
            blocks,
            _getSlotOpts,
            onChanged: s => { if (captMain) Changed?.Invoke(); },
            onHeaderDrag: StartScriptDrag,
            onBlockSplitDrag: StartSplitDrag);
        script.Position = localPos;
        _freeCanvas.AddChild(script);
        _scripts.Add(script);
        return script;
    }

    // ── 拖曳啟動 ──────────────────────────────────────────────────────

    private void StartScriptDrag(BlockScript script, Vector2 mouseGlobal)
    {
        _dragging   = script;
        _dragOffset = script.GlobalPosition - mouseGlobal;
        script.ZIndex = 10;
    }

    private void StartSplitDrag(BlockScript script, int blockIdx, Vector2 mouseGlobal)
    {
        if (blockIdx == 0)
        {
            StartScriptDrag(script, mouseGlobal);
            return;
        }

        var taken = script.SplitAt(blockIdx);
        if (taken.Count == 0) return;

        var newPos = script.Position + new Vector2(28f, blockIdx * BlockScript.BlockH);
        var newScript = SpawnScript(taken, newPos, isMain: false);
        StartScriptDrag(newScript, mouseGlobal);
    }

    // ── 輸入處理 ──────────────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        if (_dragging == null) return;

        if (@event is InputEventMouseMotion mm)
        {
            _dragging.GlobalPosition = mm.GlobalPosition + _dragOffset;
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton mb
                 && mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
        {
            FinishDrag();
            GetViewport().SetInputAsHandled();
        }
    }

    private void FinishDrag()
    {
        var dropped = _dragging;
        _dragging = null;
        if (dropped == null) return;
        dropped.ZIndex = 0;

        // Snap: find nearest script whose bottom is close to dropped's top
        const float Threshold = 40f;
        BlockScript? target = null;
        float bestDist = Threshold;
        foreach (var s in _scripts)
        {
            if (s == dropped) continue;
            float d = s.GetBottomSnapGlobal().DistanceTo(dropped.GetTopSnapGlobal());
            if (d < bestDist) { bestDist = d; target = s; }
        }

        if (target == null) return;

        bool targetIsMain = (target == MainScript);
        target.AppendBlocks(dropped.Blocks);
        _scripts.Remove(dropped);
        _freeCanvas.RemoveChild(dropped);
        dropped.QueueFree();

        if (targetIsMain) Changed?.Invoke();
    }
}
