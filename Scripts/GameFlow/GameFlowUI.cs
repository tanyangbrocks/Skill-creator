namespace SkillCreator.GameFlow;

using System;
using System.Collections.Generic;
using Godot;

// 遊戲啟動流程 UI：標題 → 角色列表 → 創建角色 → 世界列表 → 創建世界 → 遊戲開始
// 全部以程式碼構建，與 Main.cs 現有做法一致。
// 視窗固定 800×600，各元素以絕對 Position 定位。
public sealed partial class GameFlowUI : CanvasLayer
{
    public event Action<CharacterSaveData, WorldSaveData>? GameStarted;

    private List<CharacterSaveData> _chars  = [];
    private List<WorldSaveData>     _worlds = [];
    private CharacterSaveData?      _selChar;

    // 五個畫面（同一時間只顯示一個）
    private Panel _titleScreen       = null!;
    private Panel _charSelectScreen  = null!;
    private Panel _charCreateScreen  = null!;
    private Panel _worldSelectScreen = null!;
    private Panel _worldCreateScreen = null!;

    // 動態列表容器
    private VBoxContainer _charListBox  = null!;
    private VBoxContainer _worldListBox = null!;

    public override void _Ready()
    {
        Layer = 100; // 始終在最頂層

        var (chars, worlds) = FlowSaveSystem.Load();
        _chars  = chars;
        _worlds = worlds;

        BuildTitleScreen();
        BuildCharSelectScreen();
        BuildCharCreateScreen();
        BuildWorldSelectScreen();
        BuildWorldCreateScreen();

        ShowScreen(_titleScreen);
    }

    // ── 共用 helper ──────────────────────────────────────────────────────

    private static Panel MakeFullPanel(Color bg)
    {
        var p = new Panel();
        p.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = bg });
        return p;
    }

    private static Label MakeLabel(string text, int fontSize, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        if (color.HasValue)
            l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    private static Button MakeBtn(string text, Vector2 minSize, Color? bg = null)
    {
        var b = new Button { Text = text, CustomMinimumSize = minSize, FocusMode = Control.FocusModeEnum.None };
        if (bg.HasValue)
        {
            var s = new StyleBoxFlat
            {
                BgColor = bg.Value,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
                ContentMarginLeft = 8, ContentMarginRight = 8,
                ContentMarginTop = 4, ContentMarginBottom = 4,
            };
            b.AddThemeStyleboxOverride("normal", s);
        }
        return b;
    }

    private void ShowScreen(Panel target)
    {
        _titleScreen.Visible       = target == _titleScreen;
        _charSelectScreen.Visible  = target == _charSelectScreen;
        _charCreateScreen.Visible  = target == _charCreateScreen;
        _worldSelectScreen.Visible = target == _worldSelectScreen;
        _worldCreateScreen.Visible = target == _worldCreateScreen;
    }

    // ── 標題畫面 ─────────────────────────────────────────────────────────

    private void BuildTitleScreen()
    {
        _titleScreen = MakeFullPanel(new Color(0.07f, 0.07f, 0.11f));
        AddChild(_titleScreen);

        // 標題文字
        var title = MakeLabel("SkillCreator", 64, new Color(0.92f, 0.86f, 0.55f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Position            = new Vector2(0, 180);
        title.Size                = new Vector2(800, 80);
        _titleScreen.AddChild(title);

        // 進入遊戲按鈕（水平置中，標題下方）
        var btn = MakeBtn("進入遊戲", new Vector2(200, 52), new Color(0.18f, 0.35f, 0.18f));
        btn.AddThemeFontSizeOverride("font_size", 20);
        btn.Position = new Vector2(300, 310);
        btn.Pressed += () =>
        {
            RebuildCharList();
            ShowScreen(_charSelectScreen);
        };
        _titleScreen.AddChild(btn);
    }

    // ── 角色列表 ─────────────────────────────────────────────────────────

    private void BuildCharSelectScreen()
    {
        _charSelectScreen = MakeFullPanel(new Color(0.09f, 0.09f, 0.14f));
        AddChild(_charSelectScreen);

        var title = MakeLabel("選擇角色", 30, new Color(0.92f, 0.86f, 0.55f));
        title.Position = new Vector2(20, 16);
        _charSelectScreen.AddChild(title);

        // 可捲動列表
        var scroll = new ScrollContainer { Position = new Vector2(20, 70), Size = new Vector2(760, 450) };
        _charListBox = new VBoxContainer();
        _charListBox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_charListBox);
        _charSelectScreen.AddChild(scroll);

        // 左下角：創建角色
        var createBtn = MakeBtn("創建角色", new Vector2(140, 40), new Color(0.22f, 0.22f, 0.32f));
        createBtn.Position = new Vector2(20, 540);
        createBtn.Pressed += () => ShowScreen(_charCreateScreen);
        _charSelectScreen.AddChild(createBtn);
    }

    private void RebuildCharList()
    {
        foreach (Node c in _charListBox.GetChildren()) c.QueueFree();

        if (_chars.Count == 0)
        {
            var hint = MakeLabel("尚無角色，請點擊左下角「創建角色」。", 16, new Color(0.55f, 0.55f, 0.55f));
            hint.Position = new Vector2(8, 8);
            _charListBox.AddChild(hint);
            return;
        }

        foreach (var ch in _chars)
            _charListBox.AddChild(MakeCharCard(ch));
    }

    private Panel MakeCharCard(CharacterSaveData ch)
    {
        var card = new Panel { CustomMinimumSize = new Vector2(750, 64) };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.16f, 0.20f, 0.28f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });

        var lbl = MakeLabel($"  {ch.Name}    LV {ch.Level}", 18);
        lbl.Position = new Vector2(0, 20);
        lbl.Size     = new Vector2(600, 30);
        card.AddChild(lbl);

        // 透明點擊層，覆蓋整張卡片
        var clickBtn = new Button
        {
            Text = "", Flat = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        clickBtn.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        clickBtn.Pressed += () =>
        {
            _selChar = ch;
            RebuildWorldList();
            ShowScreen(_worldSelectScreen);
        };
        card.AddChild(clickBtn);
        return card;
    }

    // ── 創建角色 ─────────────────────────────────────────────────────────

    private void BuildCharCreateScreen()
    {
        _charCreateScreen = MakeFullPanel(new Color(0.09f, 0.09f, 0.14f));
        AddChild(_charCreateScreen);

        var title = MakeLabel("創建角色", 30, new Color(0.92f, 0.86f, 0.55f));
        title.Position = new Vector2(20, 16);
        _charCreateScreen.AddChild(title);

        // 返回按鈕（左上）
        var backBtn = MakeBtn("← 返回", new Vector2(100, 36));
        backBtn.Position = new Vector2(20, 66);
        backBtn.Pressed += () =>
        {
            RebuildCharList();
            ShowScreen(_charSelectScreen);
        };
        _charCreateScreen.AddChild(backBtn);

        // 角色名稱
        var nameLbl = MakeLabel("角色名稱：", 18);
        nameLbl.Position = new Vector2(20, 140);
        _charCreateScreen.AddChild(nameLbl);

        var nameInput = new LineEdit
        {
            PlaceholderText   = "旅者",
            CustomMinimumSize = new Vector2(300, 40),
            Position          = new Vector2(140, 136),
        };
        _charCreateScreen.AddChild(nameInput);

        // 確認創建角色（右下）
        var confirmBtn = MakeBtn("確認創建角色", new Vector2(160, 44), new Color(0.18f, 0.35f, 0.18f));
        confirmBtn.Position = new Vector2(620, 536);
        confirmBtn.Pressed += () =>
        {
            var name = nameInput.Text.Trim();
            if (name.Length == 0) name = "旅者";
            _chars.Add(new CharacterSaveData { Name = name });
            FlowSaveSystem.Save(_chars, _worlds);
            nameInput.Text = "";
            RebuildCharList();
            ShowScreen(_charSelectScreen);
        };
        _charCreateScreen.AddChild(confirmBtn);
    }

    // ── 世界列表 ─────────────────────────────────────────────────────────

    private void BuildWorldSelectScreen()
    {
        _worldSelectScreen = MakeFullPanel(new Color(0.07f, 0.11f, 0.09f));
        AddChild(_worldSelectScreen);

        var title = MakeLabel("選擇世界", 30, new Color(0.80f, 0.95f, 0.68f));
        title.Position = new Vector2(20, 16);
        _worldSelectScreen.AddChild(title);

        // 返回按鈕（左上）
        var backBtn = MakeBtn("← 返回", new Vector2(100, 36));
        backBtn.Position = new Vector2(20, 66);
        backBtn.Pressed += () =>
        {
            RebuildCharList();
            ShowScreen(_charSelectScreen);
        };
        _worldSelectScreen.AddChild(backBtn);

        // 可捲動列表
        var scroll = new ScrollContainer { Position = new Vector2(20, 120), Size = new Vector2(760, 400) };
        _worldListBox = new VBoxContainer();
        _worldListBox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_worldListBox);
        _worldSelectScreen.AddChild(scroll);

        // 左下角：創建世界
        var createBtn = MakeBtn("創建世界", new Vector2(140, 40), new Color(0.22f, 0.22f, 0.32f));
        createBtn.Position = new Vector2(20, 540);
        createBtn.Pressed += () => ShowScreen(_worldCreateScreen);
        _worldSelectScreen.AddChild(createBtn);
    }

    private void RebuildWorldList()
    {
        foreach (Node c in _worldListBox.GetChildren()) c.QueueFree();

        if (_worlds.Count == 0)
        {
            var hint = MakeLabel("尚無世界，請點擊左下角「創建世界」。", 16, new Color(0.55f, 0.55f, 0.55f));
            hint.Position = new Vector2(8, 8);
            _worldListBox.AddChild(hint);
            return;
        }

        foreach (var w in _worlds)
            _worldListBox.AddChild(MakeWorldCard(w));
    }

    private Panel MakeWorldCard(WorldSaveData w)
    {
        var card = new Panel { CustomMinimumSize = new Vector2(750, 64) };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.16f, 0.26f, 0.20f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });

        var lbl = MakeLabel($"  {w.Name}", 18);
        lbl.Position = new Vector2(0, 20);
        lbl.Size     = new Vector2(600, 30);
        card.AddChild(lbl);

        var clickBtn = new Button
        {
            Text = "", Flat = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        clickBtn.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        clickBtn.Pressed += () =>
        {
            w.LastPlayed = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            FlowSaveSystem.Save(_chars, _worlds);
            Visible = false;
            GameStarted?.Invoke(_selChar!, w);
        };
        card.AddChild(clickBtn);
        return card;
    }

    // ── 創建世界 ─────────────────────────────────────────────────────────

    private void BuildWorldCreateScreen()
    {
        _worldCreateScreen = MakeFullPanel(new Color(0.07f, 0.11f, 0.09f));
        AddChild(_worldCreateScreen);

        var title = MakeLabel("創建世界", 30, new Color(0.80f, 0.95f, 0.68f));
        title.Position = new Vector2(20, 16);
        _worldCreateScreen.AddChild(title);

        var backBtn = MakeBtn("← 返回", new Vector2(100, 36));
        backBtn.Position = new Vector2(20, 66);
        backBtn.Pressed += () => ShowScreen(_worldSelectScreen);
        _worldCreateScreen.AddChild(backBtn);

        var nameLbl = MakeLabel("世界名稱：", 18);
        nameLbl.Position = new Vector2(20, 140);
        _worldCreateScreen.AddChild(nameLbl);

        var worldNameInput = new LineEdit
        {
            PlaceholderText   = "新世界",
            CustomMinimumSize = new Vector2(300, 40),
            Position          = new Vector2(140, 136),
        };
        _worldCreateScreen.AddChild(worldNameInput);

        var confirmBtn = MakeBtn("確認創建世界", new Vector2(160, 44), new Color(0.18f, 0.35f, 0.18f));
        confirmBtn.Position = new Vector2(620, 536);
        confirmBtn.Pressed += () =>
        {
            var name = worldNameInput.Text.Trim();
            if (name.Length == 0) name = "新世界";
            var newWorld = new WorldSaveData
            {
                Name = name,
                Seed = (int)Godot.Time.GetTicksMsec(),
                IsFirstEnter = true,
            };
            // G-5: 建立世界目錄（chunks/ 在進入世界後才由 TileWorld3D 建立）
            newWorld.WorldDir = FlowSaveSystem.MakeWorldDir(newWorld);
            System.IO.Directory.CreateDirectory(newWorld.WorldDir);
            _worlds.Add(newWorld);
            FlowSaveSystem.Save(_chars, _worlds);
            worldNameInput.Text = "";
            RebuildWorldList();
            ShowScreen(_worldSelectScreen);
        };
        _worldCreateScreen.AddChild(confirmBtn);
    }
}
