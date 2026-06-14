namespace SkillCreator.UI;

using Godot;
using SkillCreator.AbilitySystem;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.VM;
using SkillCreator.World;

public partial class AbilityEditorUI : Control
{
    // 從圓球列表返回時發出
    [Signal] public delegate void BackPressedEventHandler();
    // W-6F：技能資料已儲存（攜帶 JSON，Main.cs 負責寫入角色存檔）
    [Signal] public delegate void SpellDataSavedEventHandler(string spellGroupJson);

    // W-6D：最多 5 個技能組，每組各自持有一個 SpellLoadout
    public SpellGroup   SpellGroup { get; } = new();
    // 向後相容：Main.cs / SpellListUI 等直接取用當前組的 Loadout
    public SpellLoadout Loadout    => SpellGroup.ActiveLoadout;

    // ── 狀態 ──────────────────────────────────────────────────────
    private readonly SpellArray[] _spells =
        new SpellArray[SpellLoadout.MaxSlots];
    private int _activeEditorSlot = 0;
    // 被動技能編輯模式
    private bool       _editingPassive = false;
    private int        _passiveEditIdx = -1;   // -1 = 新建被動
    private SpellArray _passiveBuffer  = new();
    // 容器導覽棧：每層記錄（該層的 SpellArray, 顯示標籤）
    private readonly List<(SpellArray arr, string label)> _navStack = new();
    // 當前編輯目標：主體 或 容器效果深處
    private SpellArray _spell => _navStack.Count > 0
        ? _navStack[^1].arr
        : (_editingPassive ? _passiveBuffer : _spells[_activeEditorSlot]);

    // 由 Main.cs 每幀更新，用於刻印庫境界門檻顯示
    public  int       PlayerLevel   { get; set; } = 1;

    // 調色盤拖放狀態（_palDragBlock 優先於 _palDragType）
    private BlockType? _palDragType;
    private BlockNode? _palDragBlock;
    private Vector2    _palDragStart;

    // ── UI 節點引用 ───────────────────────────────────────────────
    private LineEdit      _nameInput      = null!;
    private Label         _apValue        = null!;
    private ProgressBar   _apBar          = null!;
    private VBoxContainer _mpBreakdown    = null!;
    private Label         _descLabel      = null!;
    private SpinBox       _baseMpCostSpin = null!;
    private Label         _status         = null!;
    private ScriptCanvas  _canvas         = null!;
    // Header 導覽元素
    private Button   _backBtn           = null!;
    private Label    _breadcrumbLabel   = null!;
    // 左側面板：0=技能因子 1=積木 2=刻印
    private int           _activeLeftTab  = 1;
    private int           _activeSubTab   = 0;
    private VBoxContainer _leftContent    = null!;
    private VBoxContainer _subLabelCol    = null!;
    private readonly Button[] _leftTabBtns  = new Button[3];
    private Button[]          _groupDots    = null!;
    private bool              _isDirty      = false;

    // ── 初始化 ────────────────────────────────────────────────────
    public override void _Ready()
    {
        for (int i = 0; i < _spells.Length; i++) _spells[i] = new SpellArray();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUI();
        RefreshAll();
        // 技能資料由 Main.cs 在場景建立後呼叫 InitSpells(json) 注入
    }

    /// <summary>
    /// W-6F：由 Main.cs 在場景建立後呼叫，將角色技能資料注入編輯器。
    /// json 為空字串表示新角色（全空白）。
    /// </summary>
    public void InitSpells(string json)
    {
        _editingPassive = false;
        _passiveEditIdx = -1;
        var totemMap   = TotemLibrary.AllTotems.ToDictionary(t => t.Id);
        var engraveMap = TotemLibrary.AllEngravings.ToDictionary(e => e.Id);

        // 清空所有 5 組
        for (int g = 0; g < SpellGroup.MaxGroups; g++)
            SpellGroup.GetGroup(g).ClearAll();

        if (!string.IsNullOrWhiteSpace(json))
        {
            int activeGroupIdx = SaveSystem.LoadGroupFromString(json, SpellGroup, totemMap, engraveMap);
            SpellGroup.SetActiveGroup(activeGroupIdx);
        }

        // 同步當前組到 _spells 緩衝
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
            _spells[i] = Loadout.GetSlot(i) ?? new SpellArray();
        _activeEditorSlot = Loadout.ActiveIndex;
        _navStack.Clear();

        RefreshAll();
        _isDirty = false;
    }

    /// <summary>W-6F：序列化目前所有技能組為 JSON，供 Main.cs 存入角色存檔。</summary>
    public string GetSpellGroupJson()
    {
        // 被動編輯中：不把 _spells 緩衝寫回主動槽（避免覆蓋原有主動技能）
        if (!_editingPassive)
        {
            for (int i = 0; i < SpellLoadout.MaxSlots; i++)
                Loadout.SetSlot(i, _spells[i]);
            Loadout.ActiveIndex = _activeEditorSlot;
        }
        return SaveSystem.SaveGroupToString(SpellGroup);
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
        row.AddThemeConstantOverride("separation", 8);
        bar.AddChild(row);

        HSpacer(row, 4);

        // ── 返回 / 上一層 ──
        _backBtn = Btn("← 返回", new Color(0.20f, 0.20f, 0.28f));
        _backBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _backBtn.CustomMinimumSize = new Vector2(84, 30);
        _backBtn.Pressed += () =>
        {
            if (_navStack.Count > 0) { _navStack.RemoveAt(_navStack.Count - 1); RefreshAll(); }
            else                     { TryExitEditor(); }
        };
        row.AddChild(_backBtn);

        // ── 技能整構名稱 ──
        _nameInput = new LineEdit();
        _nameInput.PlaceholderText = "輸入技能整構名稱（必填）";
        _nameInput.CustomMinimumSize = new Vector2(180, 34);
        _nameInput.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _nameInput.TextChanged += t => { _spell.Name = t; _isDirty = true; RefreshDescription(); };
        row.AddChild(_nameInput);

        // ── 麵包屑（depth>0）──
        _breadcrumbLabel = new Label();
        _breadcrumbLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 1.0f));
        _breadcrumbLabel.AddThemeFontSizeOverride("font_size", 12);
        _breadcrumbLabel.VerticalAlignment = VerticalAlignment.Center;
        _breadcrumbLabel.Visible = false;
        row.AddChild(_breadcrumbLabel);

        var flex = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(flex);

        // ── W-6D：技能組切換圓點（1–5）──
        var groupRow = new HBoxContainer();
        groupRow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        groupRow.AddThemeConstantOverride("separation", 3);
        _groupDots = new Button[SpellGroup.MaxGroups];
        for (int gi = 0; gi < SpellGroup.MaxGroups; gi++)
        {
            int captured = gi;
            var dot = Btn($"{gi + 1}", new Color(0.20f, 0.20f, 0.30f));
            dot.CustomMinimumSize = new Vector2(26, 26);
            dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            dot.TooltipText       = $"技能組 {gi + 1}（點擊切換）";
            dot.Pressed           += () => SwitchEditorGroup(captured);
            groupRow.AddChild(dot);
            _groupDots[gi] = dot;
        }
        row.AddChild(groupRow);
        HSpacer(row, 4);

        var gearBtn = Btn("⚙", new Color(0.18f, 0.18f, 0.24f));
        gearBtn.CustomMinimumSize = new Vector2(30, 30);
        gearBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        gearBtn.TooltipText = "編輯器設定";
        gearBtn.Pressed += ShowSettingsPopup;
        row.AddChild(gearBtn);
        HSpacer(row, 6);

        _status = new Label();
        _status.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.4f));
        _status.AddThemeFontSizeOverride("font_size", 12);
        _status.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_status);

        HSpacer(row, 12);
    }

    // ── W-6D：技能組切換 ──────────────────────────────────────────────

    /// <summary>切換到指定技能組（編輯器內點圓點或外部呼叫）。</summary>
    public void SwitchEditorGroup(int index)
    {
        if (index == SpellGroup.ActiveGroupIndex) return;
        _editingPassive = false;
        _passiveEditIdx = -1;

        // 把當前 _spells[] 緩衝寫回當前 Loadout
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
            SpellGroup.ActiveLoadout.SetSlot(i, _spells[i]);

        SpellGroup.SetActiveGroup(index);

        // 從新 Loadout 還原 _spells[] 緩衝
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
            _spells[i] = SpellGroup.ActiveLoadout.GetSlot(i) ?? new SpellArray();
        _activeEditorSlot = SpellGroup.ActiveLoadout.ActiveIndex;
        _navStack.Clear();

        RefreshAll();
    }

    private void RefreshGroupDots()
    {
        if (_groupDots == null) return;
        for (int i = 0; i < SpellGroup.MaxGroups; i++)
        {
            bool active = (i == SpellGroup.ActiveGroupIndex);
            _groupDots[i].Modulate = active
                ? new Color(0.40f, 0.85f, 1.00f)   // 亮藍 = 當前組
                : new Color(0.50f, 0.50f, 0.60f);   // 灰 = 其他組
        }
    }

    /// <summary>嘗試關閉編輯器；若有未儲存變更先詢問，再檢查 MP 種類超限。</summary>
    private void TryExitEditor()
    {
        if (_isDirty)
        {
            var dlg = new ConfirmationDialog
            {
                Title      = "未儲存的變更",
                DialogText = "技能整構尚未儲存，是否儲存後離開？",
            };
            dlg.OkButtonText     = "儲存並離開";
            dlg.CancelButtonText = "捨棄變更";
            AddChild(dlg);
            dlg.PopupCentered(new Vector2I(320, 0));
            dlg.Confirmed += () =>
            {
                bool ok = SaveSpell();
                dlg.QueueFree();
                if (ok) DoExitEditor();
            };
            dlg.Canceled += () =>
            {
                _isDirty = false;
                dlg.QueueFree();
                DoExitEditor();
            };
            dlg.CloseRequested += () => dlg.QueueFree();
            return;
        }
        DoExitEditor();
    }

    private void DoExitEditor()
    {
        var violating = new List<int>();
        for (int g = 0; g < SpellGroup.MaxGroups; g++)
        {
            var ld = SpellGroup.GetGroup(g);
            for (int i = 0; i < SpellLoadout.MaxSlots; i++)
            {
                var spell = ld.GetSlot(i);
                if (spell != null && !spell.IsValidManaTypeCount())
                {
                    violating.Add(g + 1);
                    break;
                }
            }
        }
        if (violating.Count > 0)
        {
            var nums = string.Join("、", violating);
            var dlg = new AcceptDialog
            {
                Title      = "⚠ MP 種類超限",
                DialogText = $"技能組 {nums} 中有技能整構使用超過 {SpellArray.MaxManaTypes} 種 MP，\n超限技能整構在遊戲中將以「禁用」狀態執行。",
            };
            AddChild(dlg);
            dlg.PopupCentered(new Vector2I(360, 0));
            dlg.Confirmed      += () => dlg.QueueFree();
            dlg.Canceled       += () => dlg.QueueFree();
            dlg.CloseRequested += () => dlg.QueueFree();
        }
        EmitSignal(SignalName.BackPressed);
    }

    // ── 設定 Popup ────────────────────────────────────────────────────

    private void ShowSettingsPopup()
    {
        var popup = new PopupPanel();
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left",   14);
        margin.AddThemeConstantOverride("margin_right",  14);
        margin.AddThemeConstantOverride("margin_top",    10);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        popup.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        var title = new Label { Text = "編輯器設定" };
        title.AddThemeFontSizeOverride("font_size", 13);
        title.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.45f));
        vbox.AddChild(title);
        vbox.AddChild(new HSeparator());

        var chk = new CheckButton { Text = "技能因子加入時自動插入基礎 Action 刻印" };
        chk.ButtonPressed = EditorSettings.AutoInsertBaseEngraving;
        chk.AddThemeFontSizeOverride("font_size", 12);
        chk.Toggled += on => EditorSettings.AutoInsertBaseEngraving = on;
        vbox.AddChild(chk);

        AddChild(popup);
        popup.PopupCentered(new Vector2I(330, 0));
    }

    // ── 容器導覽 ──────────────────────────────────────────────────────

    private void EnterContainerEffect(string totemDisplayName)
    {
        if (_navStack.Count >= SafetyGuard.MaxContainerDepth) return;

        var dlg = new ConfirmationDialog
        {
            Title      = "進入容器效果",
            DialogText = $"編輯「{totemDisplayName}」的內部效果？",
        };
        AddChild(dlg);
        dlg.PopupCentered(new Vector2I(340, 0));
        dlg.Confirmed += () =>
        {
            _spell.ContainerEffect ??= new SpellArray();
            _navStack.Add((_spell.ContainerEffect, totemDisplayName));
            dlg.QueueFree();
            RefreshAll();
        };
        dlg.Canceled      += () => dlg.QueueFree();
        dlg.CloseRequested += () => dlg.QueueFree();
    }

    private void RefreshHeaderState()
    {
        bool inContainer = _navStack.Count > 0;
        _backBtn.Text = inContainer ? "← 上一層" : "← 返回";
        _breadcrumbLabel.Visible = inContainer;
        if (inContainer)
            _breadcrumbLabel.Text = BuildBreadcrumb();
    }

    private string BuildBreadcrumb()
    {
        var parts = new List<string>();
        string rootName = _editingPassive
            ? (_passiveBuffer.Name is { Length: > 0 } pn ? pn : "被動技能")
            : (_spells[_activeEditorSlot].Name is { Length: > 0 } n ? n : $"槽位 {_activeEditorSlot + 1}");
        parts.Add(rootName);
        foreach (var (_, lbl) in _navStack)
            parts.Add(lbl);
        return string.Join(" › ", parts);
    }

    private static string ContainerTypeName(ContainerType ct) => ct switch
    {
        ContainerType.DirectCast     => "直接施放",
        ContainerType.Projectile     => "投射物",
        ContainerType.SummonMinion   => "精靈",
        ContainerType.SummonTurret   => "砲台",
        ContainerType.SummonGuardian => "護衛",
        _                            => "容器",
    };

    // ── 左側面板（母分類 tabs：技能因子 / 積木 / 刻印） ──────────────

    private void BuildLeftPanel(HBoxContainer body)
    {
        var panel = Tinted(new Color(0.13f, 0.13f, 0.17f));
        panel.CustomMinimumSize = new Vector2(220, 0);
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
        var tabNames = new[] { "技能因子", "積木", "刻印" };
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

        // ── 內容區（左）+ 子標籤欄（右）──
        var bodyRow = new HBoxContainer();
        bodyRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        bodyRow.AddThemeConstantOverride("separation", 0);
        outer.AddChild(bodyRow);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical   = SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        bodyRow.AddChild(scroll);

        _leftContent = new VBoxContainer();
        _leftContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _leftContent.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_leftContent);

        // 子標籤欄（右側，固定 36px）
        var subWrap = new PanelContainer();
        subWrap.CustomMinimumSize = new Vector2(36, 0);
        subWrap.SizeFlagsVertical = SizeFlags.ExpandFill;
        var subBg = new StyleBoxFlat { BgColor = new Color(0.09f, 0.09f, 0.14f) };
        subWrap.AddThemeStyleboxOverride("panel", subBg);
        bodyRow.AddChild(subWrap);

        var subScroll = new ScrollContainer();
        subScroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        subScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        subScroll.VerticalScrollMode   = ScrollContainer.ScrollMode.Auto;
        subWrap.AddChild(subScroll);

        _subLabelCol = new VBoxContainer();
        _subLabelCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _subLabelCol.AddThemeConstantOverride("separation", 1);
        subScroll.AddChild(_subLabelCol);

        RebuildLeftContent();
    }

    // 根據 _activeLeftTab 填充左側面板（切主 tab 時重置子 tab）
    private void RebuildLeftContent()
    {
        if (_leftContent is null) return;
        _activeSubTab = 0;
        RebuildSubLabels();
        RebuildFilteredContent();
    }

    // 依目前 _activeSubTab 過濾內容
    private void RebuildFilteredContent()
    {
        if (_leftContent is null) return;
        foreach (var c in _leftContent.GetChildren().ToArray()) c.QueueFree();
        VSpacer(_leftContent, 4);
        switch (_activeLeftTab)
        {
            case 0: BuildTotemContent(_activeSubTab);   break;
            case 1: BuildBlockContent(_activeSubTab);   break;
            case 2: BuildEngraveContent(_activeSubTab); break;
        }
        VSpacer(_leftContent, 8);
    }

    // 重建右側子標籤欄
    private void RebuildSubLabels()
    {
        if (_subLabelCol is null) return;
        foreach (var c in _subLabelCol.GetChildren().ToArray()) c.QueueFree();

        var grp = new ButtonGroup();

        switch (_activeLeftTab)
        {
            case 0: // 技能因子
            {
                var subs = new (string lbl, TotemType t)[]
                {
                    ("範圍", TotemType.Area),   ("武技", TotemType.Technique),
                    ("投射", TotemType.Projectile), ("被動", TotemType.Passive),
                    ("變幻", TotemType.Morph),  ("位移", TotemType.Displacement),
                    ("召喚", TotemType.Summon), ("領域", TotemType.Domain),
                };
                for (int i = 0; i < subs.Length; i++)
                {
                    int ci = i;
                    var btn = MakeSubLabelBtn(subs[i].lbl, i == _activeSubTab, grp, TotemClr(subs[i].t));
                    btn.Toggled += on => { if (on) { _activeSubTab = ci; RebuildFilteredContent(); } };
                    _subLabelCol.AddChild(btn);
                }
                break;
            }
            case 1: // 積木
            {
                var subs = new[] { "控制", "觸發", "效果", "變數", "列表", "實體", "廣播", "統計", "向量", "攔截", "快照" };
                var clr  = new Color(0.55f, 0.70f, 1.0f);
                for (int i = 0; i < subs.Length; i++)
                {
                    int ci = i;
                    var btn = MakeSubLabelBtn(subs[i], i == _activeSubTab, grp, clr);
                    btn.Toggled += on => { if (on) { _activeSubTab = ci; RebuildFilteredContent(); } };
                    _subLabelCol.AddChild(btn);
                }
                break;
            }
            case 2: // 刻印
            {
                var subs = new (string lbl, EngraveColor clr)[]
                {
                    ("動作", EngraveColor.Action),
                    ("白", EngraveColor.White),  ("橙", EngraveColor.Orange),
                    ("藍", EngraveColor.Blue),   ("紅", EngraveColor.Red),
                    ("綠", EngraveColor.Green),  ("紫", EngraveColor.Purple),
                    ("黃", EngraveColor.Yellow), ("黑", EngraveColor.Black),
                    ("屬性", EngraveColor.Elemental), ("法則", EngraveColor.Law),
                };
                for (int i = 0; i < subs.Length; i++)
                {
                    int ci = i;
                    var btn = MakeSubLabelBtn(subs[i].lbl, i == _activeSubTab, grp, EngraveClr(subs[i].clr));
                    btn.Toggled += on => { if (on) { _activeSubTab = ci; RebuildFilteredContent(); } };
                    _subLabelCol.AddChild(btn);
                }
                break;
            }
        }
    }

    private static Button MakeSubLabelBtn(string text, bool active, ButtonGroup grp, Color accent)
    {
        var btn = new Button
        {
            Text          = text,
            ToggleMode    = true,
            ButtonPressed = active,
            ButtonGroup   = grp,
            AutowrapMode  = TextServer.AutowrapMode.Word,
            FocusMode     = FocusModeEnum.None,
        };
        btn.CustomMinimumSize = new Vector2(36, 0);
        btn.AddThemeFontSizeOverride("font_size", 10);
        btn.AddThemeColorOverride("font_color",         new Color(0.62f, 0.62f, 0.72f));
        btn.AddThemeColorOverride("font_pressed_color", accent);
        btn.AddThemeColorOverride("font_hover_color",   accent.Lightened(0.15f));

        var normSt = new StyleBoxFlat { BgColor = new Color(0.09f, 0.09f, 0.14f) };
        var hovSt  = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.18f) };
        var actSt  = new StyleBoxFlat { BgColor = new Color(0.13f, 0.13f, 0.20f) };
        actSt.BorderWidthLeft = 2;
        actSt.BorderColor     = accent;
        btn.AddThemeStyleboxOverride("normal",  normSt);
        btn.AddThemeStyleboxOverride("hover",   hovSt);
        btn.AddThemeStyleboxOverride("pressed", actSt);
        btn.AddThemeStyleboxOverride("focus",   actSt);
        return btn;
    }

    // 技能因子庫（依子標籤過濾，點擊加入 Totem 積木）
    private void BuildTotemContent(int subIdx)
    {
        var typeOrder = new[] { TotemType.Area, TotemType.Technique, TotemType.Projectile, TotemType.Passive,
                                 TotemType.Morph, TotemType.Displacement, TotemType.Summon, TotemType.Domain };
        var filterType = subIdx < typeOrder.Length ? typeOrder[subIdx] : typeOrder[0];

        foreach (var totem in TotemLibrary.AllTotems.Where(t => t.Type == filterType))
        {
            var t = totem;
            string lvTag = totem.RequiredPlayerLevel > 1 ? $" LV{totem.RequiredPlayerLevel}+" : "";
            var btn = Btn($"  {totem.DisplayName}{lvTag}  {totem.BaseAbilityPointCost}pt",
                          new Color(0.18f, 0.22f, 0.30f));
            btn.Alignment = HorizontalAlignment.Left;
            btn.CustomMinimumSize = new Vector2(0, 30);
            btn.AddThemeColorOverride("font_color", TotemClr(totem.Type));
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.Pressed += () => AddTotemBlock(t);
            btn.GuiInput += @event =>
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed && !mb.DoubleClick)
                {
                    _palDragBlock = new BlockNode { Type = BlockType.Totem,
                        Params = new Dictionary<string, object?> { ["totemId"] = (object?)t.Id } };
                    _palDragStart = mb.GlobalPosition;
                }
            };
            _leftContent.AddChild(btn);
        }

        // 自定義技能因子按鈕（每個子分頁底部）
        VSpacer(_leftContent, 6);
        var captType = filterType;
        var customBtn = Btn("  ＋ 自定義技能因子", new Color(0.14f, 0.22f, 0.20f));
        customBtn.Alignment = HorizontalAlignment.Left;
        customBtn.CustomMinimumSize = new Vector2(0, 28);
        customBtn.AddThemeFontSizeOverride("font_size", 11);
        customBtn.AddThemeColorOverride("font_color", new Color(0.50f, 0.85f, 0.72f));
        customBtn.Pressed += () => AddCustomTotemBlock();
        customBtn.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed && !mb.DoubleClick)
            {
                _palDragBlock = new BlockNode { Type = BlockType.Totem,
                    Params = new Dictionary<string, object?> { ["totemId"] = "custom", ["customName"] = "" } };
                _palDragStart = mb.GlobalPosition;
            }
        };
        _leftContent.AddChild(customBtn);
    }

    private void AddCustomTotemBlock()
    {
        _spell.Blocks.Insert(0, new BlockNode
        {
            Type   = BlockType.Totem,
            Params = new Dictionary<string, object?> { ["totemId"] = "custom", ["customName"] = "" },
        });
        // custom 不在 DefaultActionEngraveId，AutoInsert 只標記 _actInserted 不插入 Action 刻印
        AutoInsertBaseEngravings();
        SyncSlotsFromBlocks();
        RefreshHeaderState();
        SyncCanvas();
        RefreshCost();
        RefreshDescription();
        _isDirty = true;
    }

    private void AddTotemBlock(TotemData totem)
    {
        _spell.Blocks.Insert(0, new BlockNode
        {
            Type   = BlockType.Totem,
            Params = new Dictionary<string, object?> { ["totemId"] = (object?)totem.Id },
        });
        AutoInsertBaseEngravings();
        SyncSlotsFromBlocks();
        RefreshHeaderState();
        SyncCanvas();
        RefreshCost();
        RefreshDescription();
        _isDirty = true;
    }

    // 積木庫（依子標籤分組過濾，可拖放至畫布）
    private void BuildBlockContent(int subIdx)
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

        // subIdx → cat 索引集合：0控制, 1觸發, 2效果, 3變數, 4列表, 5實體, 6廣播, 7統計, 8向量, 9攔截, 10快照
        var groups = new int[][]
        {
            new[] {0, 1},      // 控制
            new[] {2, 3},      // 觸發
            new[] {4, 5, 6},   // 效果
            new[] {7},         // 變數
            new[] {8},         // 列表
            new[] {9},         // 實體
            new[] {10},        // 廣播
            new[] {11, 12},    // 統計
            new[] {13},        // 向量
            new[] {14},        // 攔截
            new[] {15},        // 快照
        };
        var catIndices = subIdx < groups.Length ? groups[subIdx] : groups[0];

        foreach (var ci in catIndices)
        {
            var (cat, bts) = cats[ci];
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

    // 刻印庫（依子標籤過濾，點擊加入 Engraving 積木）
    private void BuildEngraveContent(int subIdx)
    {
        var colorOrder = new[] { EngraveColor.Action,
                                  EngraveColor.White, EngraveColor.Orange, EngraveColor.Blue, EngraveColor.Red,
                                  EngraveColor.Green, EngraveColor.Purple, EngraveColor.Yellow, EngraveColor.Black,
                                  EngraveColor.Elemental, EngraveColor.Law };
        var filterColor = subIdx < colorOrder.Length ? colorOrder[subIdx] : colorOrder[0];

        foreach (var eng in TotemLibrary.AllEngravings.Where(e => e.Color == filterColor))
        {
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
            btn.Pressed += () => AddEngraveBlock(e);
            btn.GuiInput += @event =>
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed && !mb.DoubleClick)
                {
                    _palDragBlock = new BlockNode { Type = BlockType.Engraving,
                        Params = new Dictionary<string, object?> { ["engraveId"] = (object?)e.Id, ["pts"] = (object?)0f } };
                    _palDragStart = mb.GlobalPosition;
                }
            };
            _leftContent.AddChild(btn);
        }
    }

    private void AddEngraveBlock(EngraveData template)
    {
        _spell.Blocks.Add(new BlockNode
        {
            Type   = BlockType.Engraving,
            Params = new Dictionary<string, object?> { ["engraveId"] = (object?)template.Id, ["pts"] = (object?)0f },
        });
        SyncSlotsFromBlocks();
        SyncCanvas();
        RefreshCost();
        RefreshDescription();
        _isDirty = true;
    }

    // ── 中央積木序列區 ─────────────────────────────────────────────

    private void BuildCenter(HBoxContainer body)
    {
        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 0);
        body.AddChild(vbox);

        _canvas = new ScriptCanvas();
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
        _canvas.Changed += () =>
        {
            bool inserted = AutoInsertBaseEngravings();
            SyncSlotsFromBlocks();
            RefreshHeaderState();
            RefreshCost();
            RefreshDescription();
            _isDirty = true;
            if (inserted) SyncCanvas();
        };
        _canvas.ParamChanged += () =>
        {
            SyncSlotsFromBlocks();
            RefreshCost();
            _isDirty = true;
        };
        // 調色盤拖放落點 → 技能因子：浮動積木停在落點並預插動作刻印；其餘積木：浮動積木
        _canvas.PaletteBlockDropped = (node, localPos) =>
        {
            if (node.Type == BlockType.Totem)
            {
                string tid = node.Params.TryGetValue("totemId", out var tv) ? tv?.ToString() ?? "" : "";
                node.Params["_actInserted"] = (object?)true;  // 防 Changed 後重複插入
                var blocks = new List<BlockNode> { node };
                if (EditorSettings.AutoInsertBaseEngraving
                    && TotemLibrary.DefaultActionEngraveId.TryGetValue(tid, out var actId))
                {
                    blocks.Add(new BlockNode
                    {
                        Type   = BlockType.Engraving,
                        Params = new Dictionary<string, object?> { ["engraveId"] = (object?)actId, ["pts"] = (object?)0f },
                    });
                }
                if (_spell.Blocks.Count == 0)
                {
                    // 空白技能無主腳本可 snap → 直接加入，主腳本建在落點位置
                    foreach (var b in blocks) _spell.Blocks.Add(b);
                    SyncSlotsFromBlocks();
                    _canvas.SyncFrom(_spell.Blocks, GetSlotOptions, localPos);
                    RefreshCost();
                    RefreshDescription();
                    _isDirty = true;
                }
                else
                {
                    // 畫布已有積木：浮動停在拖放位置，不插入主腳本
                    _canvas.SpawnPaletteScript(blocks, localPos);
                }
            }
            else
            {
                _canvas.SpawnPaletteScript(new List<BlockNode> { node }, localPos);
            }
        };
        // 雙擊動作刻印積木 → 若為容器型 Action 刻印則進入容器效果編輯
        _canvas.BlockDoubleClicked += node =>
        {
            if (node.Type != BlockType.Engraving) return;
            if (_navStack.Count >= SafetyGuard.MaxContainerDepth) return;

            string eid = node.Params.TryGetValue("engraveId", out var ev) ? ev?.ToString() ?? "" : "";
            if (!TotemLibrary.ContainerActionIds.Contains(eid)) return;

            string displayName = TotemLibrary.AllEngravings.FirstOrDefault(e => e.Id == eid)?.DisplayName ?? eid;
            EnterContainerEffect(displayName);
        };
        vbox.AddChild(_canvas);
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

        // 基礎 MP 消耗（設計者填寫；ActivationType 乘數由計算層套用）
        var baseMpRow = new HBoxContainer();
        HSpacer(baseMpRow, 8);
        baseMpRow.AddChild(Lbl("基礎消耗："));
        _baseMpCostSpin = new SpinBox { MinValue = 0, MaxValue = 9999, Step = 1,
            CustomMinimumSize = new Vector2(70, 0) };
        _baseMpCostSpin.ValueChanged += v =>
        {
            _spell.BaseMpCost = (float)v;
            RefreshCost();
            _isDirty = true;
        };
        baseMpRow.AddChild(_baseMpCostSpin);
        baseMpRow.AddChild(Lbl(" MP"));
        vbox.AddChild(baseMpRow);

        VSpacer(vbox, 4);
        vbox.AddChild(new HSeparator());
        VSpacer(vbox, 4);

        // MP 消耗：逐類型 read-only 顯示（由 CalculateSlotCostByType 提供）
        VSpacer(vbox, 2);
        vbox.AddChild(SectionLbl("  MP 消耗"));
        VSpacer(vbox, 2);
        var mpBreakdownMargin = new MarginContainer();
        mpBreakdownMargin.AddThemeConstantOverride("margin_left",  8);
        mpBreakdownMargin.AddThemeConstantOverride("margin_right", 8);
        _mpBreakdown = new VBoxContainer();
        _mpBreakdown.AddThemeConstantOverride("separation", 2);
        mpBreakdownMargin.AddChild(_mpBreakdown);
        vbox.AddChild(mpBreakdownMargin);

        VSpacer(vbox, 4);
        vbox.AddChild(new HSeparator());
        VSpacer(vbox, 2);
        vbox.AddChild(SectionLbl("  技能整構摘要"));
        VSpacer(vbox, 2);

        // 可捲動描述區（填滿剩餘空間）
        var descScroll = new ScrollContainer();
        descScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        descScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var descMargin = new MarginContainer();
        descMargin.AddThemeConstantOverride("margin_left",  8);
        descMargin.AddThemeConstantOverride("margin_right", 8);
        descMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _descLabel = new Label();
        _descLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _descLabel.AddThemeFontSizeOverride("font_size", 11);
        _descLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.7f));
        _descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        descMargin.AddChild(_descLabel);
        descScroll.AddChild(descMargin);
        vbox.AddChild(descScroll);

        // 儲存按鈕
        var saveMargin = new MarginContainer();
        saveMargin.AddThemeConstantOverride("margin_left", 8);
        saveMargin.AddThemeConstantOverride("margin_right", 8);
        saveMargin.AddThemeConstantOverride("margin_bottom", 12);
        var saveBtn = Btn("儲存技能整構", new Color(0.15f, 0.35f, 0.55f));
        saveBtn.CustomMinimumSize = new Vector2(0, 38);
        saveBtn.Pressed += () => SaveSpell();
        saveMargin.AddChild(saveBtn);
        vbox.AddChild(saveMargin);
    }

    // ════════════════════════════════════════════════════════════
    //  刷新
    // ════════════════════════════════════════════════════════════

    private void RefreshAll()
    {
        SyncSlotsFromBlocks();   // 確保載入後 BlockNode.Params["manaTypeKey"] 已補全再建 Canvas
        RefreshHeaderState();
        if (_nameInput != null) _nameInput.Text = _spell.Name;
        RefreshCost();
        RefreshDescription();
        SyncCanvas();
        RefreshGroupDots();
    }

    private void RefreshCost()
    {
        _baseMpCostSpin?.SetValueNoSignal(_spell.BaseMpCost);

        int ap   = AbilityPointCalculator.CalculateTotalCost(_spell);
        bool over = AbilityPointCalculator.ExceedsLevelCap(_spell, PlayerLevel);
        int cap = LvCap(PlayerLevel);

        _apValue.Text = $"{ap} 點";
        _apValue.AddThemeColorOverride("font_color", over
            ? new Color(1f, 0.3f, 0.3f)
            : new Color(0.9f, 0.9f, 0.9f));

        _apBar.MaxValue = cap;
        _apBar.Value = Math.Min(ap, cap);

        RefreshMpBreakdown();
    }

    private void RefreshMpBreakdown()
    {
        if (_mpBreakdown is null) return;
        foreach (var child in _mpBreakdown.GetChildren()) child.QueueFree();

        var byType = AbilityPointCalculator.CalculateSlotCostByType(_spell);
        if (byType.Count == 0)
        {
            // 沒有已綁定類型時，顯示總量作為後備
            float mp = AbilityPointCalculator.CalculateMpCost(_spell);
            var fallback = Lbl($"{mp:F0} 點");
            fallback.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.7f));
            _mpBreakdown.AddChild(fallback);
            return;
        }

        foreach (var (key, cost) in byType)
        {
            string name = ManaTypeRegistry.Get(key)?.DisplayName ?? key;
            var lbl = Lbl($"{name}：{cost:F0} 點");
            lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
            _mpBreakdown.AddChild(lbl);
        }

        if (_spell.HasUnboundMpBlocks())
        {
            var warn = Lbl("⚠ 部分插槽未指定 MP 種類");
            warn.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.3f));
            _mpBreakdown.AddChild(warn);
        }
    }

    private void RefreshDescription()
    {
        if (_descLabel is null) return;
        _descLabel.Text = SpellDescriptionGenerator.GenerateStructured(_spell);
    }

    // ── 積木編輯器 ───────────────────────────────────────────────

    private void SyncCanvas()
    {
        if (_canvas is null) return;
        _canvas.SyncFrom(_spell.Blocks, GetSlotOptions);
    }


    // 從 canvas 積木掃描 Totem/Engraving 積木，重建 Slots + GlobalEngravings + Container
    private void SyncSlotsFromBlocks()
    {
        var oldSlots = _spell.Slots.ToList();
        int slotIdx = 0;
        _spell.Slots.Clear();
        _spell.GlobalEngravings.Clear();
        TotemData? firstNonPassive = null;

        foreach (var b in _spell.Blocks)
        {
            if (b.Type == BlockType.Totem)
            {
                string id = b.Params.TryGetValue("totemId", out var v) && v is string s ? s : "";
                TotemData? totem;
                if (id == "custom")
                {
                    string cn = b.Params.TryGetValue("customName", out var cv) ? cv?.ToString() ?? "" : "";
                    totem = new TotemData { Id = "custom", DisplayName = string.IsNullOrEmpty(cn) ? "自定義" : cn, Type = TotemType.Custom };
                }
                else
                {
                    totem = TotemLibrary.AllTotems.FirstOrDefault(t => t.Id == id);
                }
                if (totem is not null)
                {
                    // 遷移：首次載入時 BlockNode.Params 無 manaTypeKey，從 oldSlots 補入
                    if (!b.Params.ContainsKey("manaTypeKey") && slotIdx < oldSlots.Count)
                        b.Params["manaTypeKey"] = (object?)oldSlots[slotIdx].ManaTypeKey;
                    string? manaKey = b.Params.TryGetValue("manaTypeKey", out var manaObj) ? manaObj?.ToString() : null;
                    var newSlot = new SpellSlot { Totem = totem, ManaTypeKey = manaKey };
                    slotIdx++;
                    _spell.Slots.Add(newSlot);
                    if (firstNonPassive is null && totem.Type != TotemType.Passive)
                        firstNonPassive = totem;
                }
            }
            else if (b.Type == BlockType.Engraving)
            {
                string id = b.Params.TryGetValue("engraveId", out var v) && v is string s ? s : "";
                float pts  = b.Params.TryGetValue("pts", out var pv) && pv is float f ? f : 0f;
                var template = TotemLibrary.AllEngravings.FirstOrDefault(e => e.Id == id);
                if (template is not null)
                    _spell.GlobalEngravings.Add(new EngraveData
                    {
                        Id                  = template.Id,
                        DisplayName         = template.DisplayName,
                        Color               = template.Color,
                        Category            = template.Category,
                        Trigger             = template.Trigger,
                        ScalingType         = template.ScalingType,
                        ScalingCoefficient  = template.ScalingCoefficient,
                        BaseEffect          = template.BaseEffect,
                        BaseCost            = template.BaseCost,
                        IsGlobal            = true,
                        IsRestriction       = template.IsRestriction,
                        RequiredPlayerLevel = template.RequiredPlayerLevel,
                        PointsInvested      = (int)pts,
                    });
            }
        }

        // Container 優先順序：投射 > 召喚 > 其他（範圍/武技等 = DirectCast）
        var projectileTotem = _spell.Slots.Select(s => s.Totem).FirstOrDefault(t => t?.Type == TotemType.Projectile);
        var summonTotem     = _spell.Slots.Select(s => s.Totem).FirstOrDefault(t => t?.Type == TotemType.Summon);
        _spell.Container = TotemToContainer(projectileTotem ?? summonTotem ?? firstNonPassive);
    }

    // 掃描主腳本，對尚未處理的 Totem 積木自動插入預設 Action 刻印。
    // 標記 _actInserted = true 防止重複插入（即使使用者手動刪除後也不重插）。
    // 回傳 true 表示有插入，呼叫端應呼叫 SyncCanvas() 更新畫面。
    private bool AutoInsertBaseEngravings()
    {
        if (!EditorSettings.AutoInsertBaseEngraving) return false;
        bool inserted = false;
        for (int i = 0; i < _spell.Blocks.Count; i++)
        {
            var b = _spell.Blocks[i];
            if (b.Type != BlockType.Totem) continue;
            if (b.Params.ContainsKey("_actInserted")) continue;
            b.Params["_actInserted"] = (object?)true;

            string tid = b.Params.TryGetValue("totemId", out var v) ? v?.ToString() ?? "" : "";
            if (!TotemLibrary.DefaultActionEngraveId.TryGetValue(tid, out var actId)) continue;

            _spell.Blocks.Insert(i + 1, new BlockNode
            {
                Type   = BlockType.Engraving,
                Params = new Dictionary<string, object?> { ["engraveId"] = (object?)actId, ["pts"] = (object?)0f },
            });
            i++;
            inserted = true;
        }
        return inserted;
    }

    private static ContainerType TotemToContainer(TotemData? totem) => totem?.Type switch
    {
        TotemType.Projectile                                     => ContainerType.Projectile,
        TotemType.Summon when totem.Id == "summon_minion"   => ContainerType.SummonMinion,
        TotemType.Summon when totem.Id == "summon_turret"   => ContainerType.SummonTurret,
        TotemType.Summon when totem.Id == "summon_guardian" => ContainerType.SummonGuardian,
        TotemType.Summon                                         => ContainerType.SummonMinion,
        _                                                        => ContainerType.DirectCast,
    };

    private List<(string display, string refKey)> GetSlotOptions()
    {
        var result = new List<(string, string)>();
        for (int i = 0; i < _spell.Slots.Count; i++)
        {
            var s = _spell.Slots[i];
            if (s.IsEmpty) continue;
            string refKey  = string.IsNullOrEmpty(s.Name) ? (s.Totem?.Id ?? $"slot_{i}") : s.Name;
            string display = $"{s.Totem!.DisplayName}（{refKey}）";
            result.Add((display, refKey));
        }
        return result;
    }


    // 積木名稱統一委託給 ScratchCanvas._descs，避免重複定義與英文 fallback
    private static string BlockTypeName(BlockType type) => ScratchCanvas.BlockName(type);

    // 顏色同樣委託給 ScratchCanvas._descs，與積木頭部色塊保持一致
    private static Color BlockTypeColor(BlockType type) => ScratchCanvas.BlockColor(type);

    // 遞迴掃描 blocks，檢查是否有 SetActivation* 積木（含 IF/Loop 內部）
    private static bool HasActivationTypeBlock(List<BlockNode> blocks)
    {
        foreach (var b in blocks)
        {
            if (b.Type is BlockType.SetActivationInstant
                       or BlockType.SetActivationDeclare
                       or BlockType.SetActivationSustained)
                return true;
            if (HasActivationTypeBlock(b.ThenBranch)) return true;
            if (HasActivationTypeBlock(b.ElseBranch)) return true;
            if (HasActivationTypeBlock(b.LoopBody))   return true;
        }
        return false;
    }

    private bool SaveSpell()
    {
        // 先同步 Slots / GlobalEngravings / Container
        SyncSlotsFromBlocks();

        // ── 收集所有驗證錯誤 ──
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(_spell.Name))
            errors.Add("• 請填寫技能整構名稱（必填）");

        // 主被動驗證：主腳本必須含有至少一個技能因子
        bool hasTotem = _spell.Blocks.Any(b => b.Type == BlockType.Totem);
        if (!hasTotem)
            errors.Add("• 請加入技能因子來定義主被動類型");
        else if (!_spell.IsPassive && !HasActivationTypeBlock(_spell.Blocks))
            errors.Add("• 主動技能需要定義發動類型（請加入「即時型」、「宣告型」或「持續生效型」積木）");

        // MP 未綁定驗證
        if (_spell.HasUnboundMpBlocks())
            errors.Add("• 有技能因子存在需消耗 MP 的積木，但尚未指定 MP 類型");

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
            return false;
        }

        // ── 儲存（W-6F：序列化後通知 Main.cs 寫入角色存檔）──
        if (_editingPassive)
        {
            if (_passiveEditIdx < 0)
                Loadout.AddPassive(_passiveBuffer);
            else
                Loadout.ReplacePassive(_passiveEditIdx, _passiveBuffer);
            string pName = _passiveBuffer.Name is { Length: > 0 } pn ? pn : "被動技能";
            GD.Print($"[儲存] 被動 ← 技能整構「{pName}」  AP：{AbilityPointCalculator.CalculateTotalCost(_passiveBuffer)}");
            _status.Text = $"✓ 被動「{pName}」已存";
            _editingPassive = false;
            _passiveEditIdx = -1;
        }
        else
        {
            Loadout.SetSlot(_activeEditorSlot, _spell);
            GD.Print($"[儲存] 槽位 {_activeEditorSlot + 1} ← 技能整構「{_spell.Name}」  " +
                     $"主被動：{(_spell.IsPassive ? "被動" : "主動")}  " +
                     $"AP：{AbilityPointCalculator.CalculateTotalCost(_spell)}  " +
                     $"MP：{AbilityPointCalculator.CalculateMpCost(_spell):F0}");
            _status.Text = $"✓ 槽位 {_activeEditorSlot + 1}「{_spell.Name}」已存";
        }
        _isDirty = false;
        EmitSignal(SignalName.SpellDataSaved, GetSpellGroupJson());
        return true;
    }

    private void SelectEditorSlot(int i)
    {
        _activeEditorSlot = i;
        _navStack.Clear();
        RefreshAll();
    }

    // 供 SpellListUI 呼叫，從圓球列表導覽到指定槽位
    public void OpenSlot(int index)
    {
        if (index < 0 || index >= _spells.Length) return;
        _editingPassive = false;
        _passiveEditIdx = -1;
        _activeEditorSlot = index;
        _navStack.Clear();
        RefreshAll();
    }

    // 開啟被動技能編輯（passiveIdx=-1 = 新建；>=0 = 編輯現有）
    public void OpenPassive(int passiveIdx)
    {
        _editingPassive = true;
        _passiveEditIdx = passiveIdx;
        _navStack.Clear();
        if (passiveIdx >= 0)
        {
            _passiveBuffer = Loadout.GetPassive(passiveIdx) ?? new SpellArray();
        }
        else
        {
            _passiveBuffer = new SpellArray();
            var pt = TotemLibrary.AllTotems.FirstOrDefault(t => t.Id == "passive_continuous");
            if (pt is not null) _passiveBuffer.Slots.Add(new SpellSlot { Totem = pt });
        }
        RefreshAll();
    }

    // ════════════════════════════════════════════════════════════
    //  Helper 工廠
    // ════════════════════════════════════════════════════════════

    // 有色背景的 Panel（子節點自行用 FullRect anchor 填滿）
    // ── 調色盤拖放輸入處理 ────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        bool hasPending = _palDragType != null || _palDragBlock != null;
        if (!hasPending && !BlockDrag.Active) return;

        if (@event is InputEventMouseMotion mm)
        {
            // 移動超過 4px 才啟動拖放（避免普通點擊誤觸）
            if (hasPending && !BlockDrag.Active)
            {
                if (mm.GlobalPosition.DistanceTo(_palDragStart) > 4f)
                {
                    var block = _palDragBlock ?? ScratchCanvas.MakeDefaultBlock(_palDragType!.Value);
                    BlockDrag.BeginNew(block);
                    _palDragType  = null;
                    _palDragBlock = null;
                }
            }
            return;
        }

        if (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // 放開滑鼠：清除待拖狀態（ScriptCanvas 的 _Input 先於此處處理真正的拖放落點）
            _palDragType  = null;
            _palDragBlock = null;
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
        TotemType.Passive      => new Color(0.60f, 0.92f, 0.65f), // 淺綠
        TotemType.Morph        => new Color(0.40f, 0.90f, 0.80f), // 青
        TotemType.Displacement => new Color(0.65f, 0.95f, 0.30f), // 黃綠
        TotemType.Summon       => new Color(1.0f,  0.75f, 0.20f), // 金
        TotemType.Domain       => new Color(0.85f, 0.40f, 1.0f),  // 紫
        TotemType.Custom       => new Color(0.50f, 0.85f, 0.72f), // 淡綠
        _                      => new Color(0.8f,  0.8f,  0.8f),
    };

    private static string TotemTypeTag(TotemType t) => t switch
    {
        TotemType.Area         => "[範圍]",
        TotemType.Technique    => "[武技]",
        TotemType.Projectile   => "[投射]",
        TotemType.Passive      => "[被動]",
        TotemType.Morph        => "[變幻]",
        TotemType.Displacement => "[位移]",
        TotemType.Summon       => "[召喚]",
        TotemType.Domain       => "[領域]",
        TotemType.Custom       => "[自訂]",
        _                      => "[?]",
    };

    private static string TotemTypeName(TotemType t) => t switch
    {
        TotemType.Area         => "── 範圍技能因子 ──",
        TotemType.Technique    => "── 武技技能因子 ──",
        TotemType.Projectile   => "── 投射物技能因子 ──",
        TotemType.Passive      => "── 被動技能因子 ──",
        TotemType.Morph        => "── 變幻技能因子 ──",
        TotemType.Displacement => "── 位移技能因子 ──",
        TotemType.Summon       => "── 召喚技能因子 ──",
        TotemType.Domain       => "── 領域技能因子 ──",
        TotemType.Custom       => "── 自定義技能因子 ──",
        _                      => "──",
    };

    private static Color EngraveClr(EngraveColor c) => c switch
    {
        EngraveColor.Action    => new Color(0.35f, 0.90f, 0.82f), // 青色
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
        EngraveColor.Action    => "  ── 動作（基礎行為）",
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
