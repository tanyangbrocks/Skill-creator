namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem.VM;

// 自由畫布 — 以絕對座標管理多條 BlockScript，替換 VBox 式的 ScratchCanvas。
//
// 公開介面與 ScratchCanvas 相同：
//   SyncFrom(blocks, getSlotOptions)
//   event Action? Changed
//
// 互動邏輯（3-B 版）：
//   • Header 拖把 → 拖整條腳本
//   • 積木色條拖把 → 斷開，生成新浮動腳本
//   • 放開時靠近另一腳本底端 → 自動連結（磁吸 40px）
//   • 磁吸提示：靠近時底端顯示綠色橫條
//   • 調色盤拖放：從 AbilityEditorUI 拖出積木，放到畫布任意位置
public partial class ScriptCanvas : Control
{
    public event Action? Changed;
    public Action<BlockNode>?          BlockDoubleClicked  { get; set; }
    public Action<BlockNode, Vector2>? PaletteBlockDropped { get; set; }

    public void SpawnPaletteScript(List<BlockNode> blocks, Vector2 localPos)
        => SpawnScript(blocks, localPos, isMain: false);

    private List<BlockNode>                       _blocks     = new();
    private Func<List<(string, string)>>?         _getSlotOpts;
    private readonly List<BlockScript>            _scripts    = new();
    private Control                               _freeCanvas = null!;

    // Script drag state
    private BlockScript? _dragging;
    private Vector2      _dragOffset;

    // Snap highlight
    private Panel _snapHL = null!;

    // Palette drop preview (shown when hovering canvas during palette drag)
    private Label _palPreview = null!;

    // Trash zone — drop target for deleting blocks
    private Panel        _trashZone  = null!;
    private StyleBoxFlat _trashStyle = null!;
    private Vector2      _lastMouseGlobal;

    // Pan / zoom state
    private Vector2 _canvasPan  = Vector2.Zero;
    private float   _canvasZoom = 1.0f;
    private bool    _panning;
    private bool    _panWithLeft;  // true = Ctrl+Left 平移，false = 中鍵平移
    private Vector2 _panStart;
    private Vector2 _panOrigin;
    private const float ZoomMin  = 0.20f;
    private const float ZoomMax  = 3.00f;
    private const float ZoomStep = 0.12f;

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
        _freeCanvas.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_freeCanvas);

        var hint = new Label { Text = "← 點擊積木庫以加入積木，或拖入此區域" };
        hint.AddThemeColorOverride("font_color", new Color(0.32f, 0.32f, 0.42f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        hint.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(hint);

        // Snap highlight bar (hidden until near a snap target)
        _snapHL = new Panel();
        _snapHL.CustomMinimumSize = new Vector2(BlockScript.BlockMinW, 4);
        var hlStyle = new StyleBoxFlat { BgColor = new Color(0.20f, 0.90f, 0.25f, 0.85f) };
        hlStyle.CornerRadiusTopLeft = hlStyle.CornerRadiusTopRight =
        hlStyle.CornerRadiusBottomLeft = hlStyle.CornerRadiusBottomRight = 2;
        _snapHL.AddThemeStyleboxOverride("panel", hlStyle);
        _snapHL.Visible = false;
        _snapHL.ZIndex  = 20;
        _snapHL.MouseFilter = MouseFilterEnum.Ignore;
        _freeCanvas.AddChild(_snapHL);

        // Palette drop preview label
        _palPreview = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _palPreview.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        _palPreview.AddThemeFontSizeOverride("font_size", 11);
        _palPreview.Visible = false;
        _palPreview.ZIndex  = 30;
        AddChild(_palPreview);

        // Trash zone — bottom-right corner, deletes dragged block chains on drop
        _trashZone = new Panel();
        _trashZone.MouseFilter = MouseFilterEnum.Ignore;
        _trashZone.ZIndex = 25;
        _trashStyle = new StyleBoxFlat
        {
            BgColor     = new Color(0.22f, 0.06f, 0.06f, 0.65f),
            BorderColor = new Color(0.60f, 0.15f, 0.15f, 0.55f),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        };
        _trashStyle.CornerRadiusTopLeft = _trashStyle.CornerRadiusTopRight =
        _trashStyle.CornerRadiusBottomLeft = _trashStyle.CornerRadiusBottomRight = 8;
        _trashZone.AddThemeStyleboxOverride("panel", _trashStyle);
        var trashIcon = new Label
        {
            Text                = "🗑",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        trashIcon.AddThemeFontSizeOverride("font_size", 20);
        trashIcon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        trashIcon.MouseFilter = MouseFilterEnum.Ignore;
        _trashZone.AddChild(trashIcon);
        _trashZone.AnchorLeft   = 1f;
        _trashZone.AnchorTop    = 1f;
        _trashZone.AnchorRight  = 1f;
        _trashZone.AnchorBottom = 1f;
        _trashZone.OffsetLeft   = -56f;
        _trashZone.OffsetTop    = -56f;
        _trashZone.OffsetRight  = -8f;
        _trashZone.OffsetBottom = -8f;
        AddChild(_trashZone);
    }

    // ── 公開 API ─────────────────────────────────────────────────────

    public void SyncFrom(List<BlockNode> blocks, Func<List<(string, string)>>? getSlotOpts, Vector2? initialPos = null)
    {
        _blocks      = blocks;
        _getSlotOpts = getSlotOpts;
        _dragging    = null;
        _panning     = false;
        _snapHL.Visible = false;

        foreach (var s in _scripts)
        {
            _freeCanvas.RemoveChild(s);
            s.QueueFree();
        }
        _scripts.Clear();

        if (_blocks.Count > 0)
            SpawnScript(_blocks, initialPos ?? new Vector2(20f, 20f), isMain: true);
    }

    // 重置視角到原點 1:1（載入新技能時可呼叫）
    public void ResetView()
    {
        _canvasPan  = Vector2.Zero;
        _canvasZoom = 1.0f;
        ApplyCanvasTransform();
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
            onBlockSplitDrag: StartSplitDrag,
            onDoubleClick: BlockDoubleClicked);
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
        if (@event is InputEventMouseMotion mm)
        {
            _lastMouseGlobal = mm.GlobalPosition;
            UpdateTrashHighlight(mm.GlobalPosition);

            if (_panning)
            {
                // Ctrl+Left 模式：Ctrl 放開時結束平移
                if (_panWithLeft && !Input.IsKeyPressed(Key.Ctrl))
                {
                    _panning = _panWithLeft = false;
                    return;
                }
                _canvasPan = _panOrigin + (mm.GlobalPosition - _panStart);
                ApplyCanvasTransform();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_dragging != null)
            {
                _dragging.GlobalPosition = mm.GlobalPosition + _dragOffset;
                UpdateSnapHighlight(mm.GlobalPosition);
                GetViewport().SetInputAsHandled();
            }
            else if (BlockDrag.Active && BlockDrag.SourceList == null)
            {
                UpdatePalettePreview(mm.GlobalPosition);
            }
            // 圖騰拖曳中：高亮所有 TotemDropZone
            if (IsTotemDragging())
                UpdateTotemZoneHighlight(mm.GlobalPosition);
            else
                ClearTotemZoneHighlights();
            return;
        }

        // 滾輪縮放 + 中鍵平移開始
        if (@event is InputEventMouseButton mbe && mbe.Pressed)
        {
            if ((mbe.ButtonIndex == MouseButton.WheelUp || mbe.ButtonIndex == MouseButton.WheelDown)
                && GetGlobalRect().HasPoint(mbe.GlobalPosition))
            {
                ZoomAt(mbe.GlobalPosition, mbe.ButtonIndex == MouseButton.WheelUp ? ZoomStep : -ZoomStep);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (mbe.ButtonIndex == MouseButton.Middle && GetGlobalRect().HasPoint(mbe.GlobalPosition))
            {
                _panning     = true;
                _panWithLeft = false;
                _panStart    = mbe.GlobalPosition;
                _panOrigin   = _canvasPan;
                GetViewport().SetInputAsHandled();
                return;
            }
            if (mbe.ButtonIndex == MouseButton.Left && mbe.CtrlPressed && GetGlobalRect().HasPoint(mbe.GlobalPosition))
            {
                _panning     = true;
                _panWithLeft = true;
                _panStart    = mbe.GlobalPosition;
                _panOrigin   = _canvasPan;
                GetViewport().SetInputAsHandled();
                return;
            }
            return;
        }

        // 中鍵釋放 → 結束平移
        if (@event is InputEventMouseButton mbm && !mbm.Pressed && mbm.ButtonIndex == MouseButton.Middle)
        {
            _panning = false;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // Ctrl+Left 平移中：左鍵放開即結束
            if (_panning && _panWithLeft)
            {
                _panning = _panWithLeft = false;
                GetViewport().SetInputAsHandled();
                return;
            }

            // 圖騰 script 拖到 InvokeTotem 插槽 → 綁定（保留圖騰積木不移除）
            if (_dragging != null && _dragging.Blocks.Count > 0 &&
                _dragging.Blocks[0].Type == BlockType.Totem)
            {
                foreach (var zone in TotemDropZone.ActiveZones)
                {
                    if (!zone.GetGlobalRect().HasPoint(mb.GlobalPosition)) continue;
                    zone.Bind(_dragging.Blocks[0]);
                    ClearTotemZoneHighlights();
                    _snapHL.Visible = false;
                    _dragging.ZIndex = 0;
                    _dragging = null;
                    Changed?.Invoke();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            // 調色盤 Totem 積木拖到 InvokeTotem 插槽 → 綁定（不在畫布新增積木）
            if (BlockDrag.Active && BlockDrag.Block?.Type == BlockType.Totem)
            {
                foreach (var zone in TotemDropZone.ActiveZones)
                {
                    if (!zone.GetGlobalRect().HasPoint(mb.GlobalPosition)) continue;
                    zone.Bind(BlockDrag.Block);
                    ClearTotemZoneHighlights();
                    HidePalettePreview();
                    BlockDrag.Clear();
                    Changed?.Invoke();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            // Palette drop on canvas — 路由給上層處理，保持業務邏輯在 AbilityEditorUI
            if (BlockDrag.Active && BlockDrag.SourceList == null && BlockDrag.Block != null)
            {
                HidePalettePreview();
                if (GetGlobalRect().HasPoint(mb.GlobalPosition)
                    && !_trashZone.GetGlobalRect().HasPoint(mb.GlobalPosition))
                {
                    var localPos = ToCanvasLocal(mb.GlobalPosition);
                    if (PaletteBlockDropped != null)
                        PaletteBlockDropped(BlockDrag.Block, localPos);
                    else
                        SpawnScript(new List<BlockNode> { BlockDrag.Block }, localPos, isMain: false);
                }
                BlockDrag.Clear();
                UpdateTrashHighlight(Vector2.Zero);
                GetViewport().SetInputAsHandled();
                return;
            }

            // Script drag release
            if (_dragging != null)
            {
                _snapHL.Visible = false;
                FinishDrag();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private bool IsTotemDragging() =>
        (_dragging != null && _dragging.Blocks.Count > 0 && _dragging.Blocks[0].Type == BlockType.Totem) ||
        (BlockDrag.Active && BlockDrag.Block?.Type == BlockType.Totem);

    private void UpdateTotemZoneHighlight(Vector2 mouseGlobal)
    {
        foreach (var zone in TotemDropZone.ActiveZones)
            zone.SetHighlight(zone.GetGlobalRect().HasPoint(mouseGlobal));
    }

    private void ClearTotemZoneHighlights()
    {
        foreach (var zone in TotemDropZone.ActiveZones)
            zone.SetHighlight(false);
    }

    private void UpdateSnapHighlight(Vector2 mouseGlobal)
    {
        if (_dragging == null) { _snapHL.Visible = false; return; }

        const float ShowDist = 60f;
        BlockScript? best = null;
        float bestD = ShowDist;
        var droppedTop = _dragging.GetTopSnapGlobal();

        foreach (var s in _scripts)
        {
            if (s == _dragging) continue;
            float d = s.GetBottomSnapGlobal().DistanceTo(droppedTop);
            if (d < bestD) { bestD = d; best = s; }
        }

        if (best != null)
        {
            var bottomGlobal = best.GetBottomSnapGlobal();
            _snapHL.GlobalPosition = bottomGlobal - new Vector2(BlockScript.BlockMinW * 0.5f, 2f);
            _snapHL.Visible = true;
        }
        else
        {
            _snapHL.Visible = false;
        }
    }

    private void UpdatePalettePreview(Vector2 mouseGlobal)
    {
        if (!BlockDrag.Active || BlockDrag.Block == null)
        {
            HidePalettePreview();
            return;
        }

        bool overCanvas = GetGlobalRect().HasPoint(mouseGlobal);
        if (overCanvas)
        {
            _palPreview.Text = $"[ {ScratchCanvas.BlockName(BlockDrag.Block)} ]";
            _palPreview.GlobalPosition = mouseGlobal + new Vector2(10f, -16f);
            _palPreview.Visible = true;
        }
        else
        {
            HidePalettePreview();
        }
    }

    private void HidePalettePreview()
    {
        _palPreview.Visible = false;
    }

    // ── 視角變換輔助 ──────────────────────────────────────────────────

    // 套用目前 pan / zoom 到 _freeCanvas
    private void ApplyCanvasTransform()
    {
        _freeCanvas.Position = _canvasPan;
        _freeCanvas.Scale    = new Vector2(_canvasZoom, _canvasZoom);
    }

    // 以滑鼠位置為圓心縮放
    private void ZoomAt(Vector2 mouseGlobal, float delta)
    {
        float newZoom = Mathf.Clamp(_canvasZoom + delta, ZoomMin, ZoomMax);
        if (Mathf.IsEqualApprox(newZoom, _canvasZoom)) return;
        var pivot  = mouseGlobal - GlobalPosition;
        _canvasPan = pivot - (pivot - _canvasPan) * (newZoom / _canvasZoom);
        _canvasZoom = newZoom;
        ApplyCanvasTransform();
    }

    // 全域座標 → _freeCanvas 本機座標
    private Vector2 ToCanvasLocal(Vector2 globalPos)
        => (globalPos - GlobalPosition - _canvasPan) / _canvasZoom;

    private void UpdateTrashHighlight(Vector2 mouseGlobal)
    {
        bool isDragging = _dragging != null || BlockDrag.Active;
        bool overTrash  = isDragging && _trashZone.GetGlobalRect().HasPoint(mouseGlobal);
        _trashStyle.BgColor = overTrash
            ? new Color(0.80f, 0.12f, 0.12f, 0.95f)
            : isDragging
                ? new Color(0.38f, 0.09f, 0.09f, 0.80f)
                : new Color(0.22f, 0.06f, 0.06f, 0.65f);
        _trashZone.AddThemeStyleboxOverride("panel", _trashStyle);
    }

    private void FinishDrag()
    {
        var dropped = _dragging;
        _dragging = null;
        if (dropped == null) return;
        dropped.ZIndex = 0;

        UpdateTrashHighlight(Vector2.Zero);

        // 拖到垃圾桶 → 刪除整串積木
        if (_trashZone.GetGlobalRect().HasPoint(_lastMouseGlobal))
        {
            if (dropped == MainScript) _blocks.Clear();
            _scripts.Remove(dropped);
            _freeCanvas.RemoveChild(dropped);
            dropped.QueueFree();
            Changed?.Invoke();
            return;
        }

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
