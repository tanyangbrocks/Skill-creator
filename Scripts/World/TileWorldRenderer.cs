namespace SkillCreator.World;

using Godot;
using SkillCreator.AbilitySystem;
using SkillCreator.World.Materials;

// Godot Node2D：渲染 TileWorld 並處理滑鼠繪製輸入
public partial class TileWorldRenderer : Node2D
{
    public const int TilePixels = 4;

    public TileWorld      World    { get; } = new TileWorld(200, 150);
    public MaterialType   SelectedMaterial { get; set; } = MaterialType.Sand;
    public PlayerController? Player    { get; set; }
    public EnemyManager?     Enemies   { get; set; }
    public List<SpellProjectile>? Projectiles { get; set; }

    // 開啟編輯器時暫停物理模擬
    public bool Paused { get; set; } = false;

    private Image _image = null!;
    private ImageTexture _texture = null!;
    private Sprite2D _sprite = null!;
    private readonly byte[] _renderBuf = new byte[200 * 150 * 3];

    public int SimStepsPerFrame { get; set; } = 1;

    public override void _Ready()
    {
        int w = World.Width, h = World.Height;
        _image   = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        _texture = ImageTexture.CreateFromImage(_image);
        _sprite  = new Sprite2D
        {
            Texture       = _texture,
            Centered      = false,
            Scale         = new Vector2(TilePixels, TilePixels),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        AddChild(_sprite);
        GetTree().Root.MinSize = new Vector2I(w * TilePixels, h * TilePixels);
    }

    public override void _Process(double delta)
    {
        if (!Paused)
            for (int i = 0; i < SimStepsPerFrame; i++)
                World.Tick();

        RenderToBuffer();
        _image.SetData(World.Width, World.Height, false, Image.Format.Rgb8, _renderBuf);
        _texture.Update(_image);

        if (!Paused) HandleMouseDraw();
    }

    // ── 渲染 ─────────────────────────────────────────────────────

    private void RenderToBuffer()
    {
        int w = World.Width, h = World.Height;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var cell = World.GetCell(x, y);
            var c    = MaterialRegistry.GetColor(cell.Type, cell.Variant);
            int i    = (y * w + x) * 3;
            _renderBuf[i]     = (byte)(c.R * 255f);
            _renderBuf[i + 1] = (byte)(c.G * 255f);
            _renderBuf[i + 2] = (byte)(c.B * 255f);
        }

        // 投射物：青白色
        if (Projectiles != null)
        {
            foreach (var p in Projectiles)
            {
                if (!p.IsAlive) continue;
                var pp = p.Position;
                int pi = (pp.Y * w + pp.X) * 3;
                if (pi < 0 || pi + 2 >= _renderBuf.Length) continue;
                _renderBuf[pi]     = 200;
                _renderBuf[pi + 1] = 240;
                _renderBuf[pi + 2] = 255;
            }
        }

        // 玩家：亮白
        if (Player != null)
        {
            var pp = Player.Position;
            int pi = (pp.Y * w + pp.X) * 3;
            if (pi >= 0 && pi + 2 < _renderBuf.Length)
            {
                _renderBuf[pi]     = 255;
                _renderBuf[pi + 1] = 255;
                _renderBuf[pi + 2] = 255;
            }
            var fp = new GridPos(pp.X + Player.Facing.X, pp.Y + Player.Facing.Y);
            int fi = (fp.Y * w + fp.X) * 3;
            if (fi >= 0 && fi + 2 < _renderBuf.Length)
            {
                _renderBuf[fi]     = 120;
                _renderBuf[fi + 1] = 180;
                _renderBuf[fi + 2] = 255;
            }
        }

        // 敵人：亮紅→暗紅依 HP
        if (Enemies != null)
        {
            foreach (var e in Enemies.Enemies)
            {
                if (!e.IsAlive) continue;
                var ep = e.Position;
                int ei = (ep.Y * w + ep.X) * 3;
                if (ei < 0 || ei + 2 >= _renderBuf.Length) continue;
                float r = e.Hp / e.MaxHp;
                _renderBuf[ei]     = (byte)(80 + (int)(140 * r));
                _renderBuf[ei + 1] = (byte)(20 + (int)(40  * r));
                _renderBuf[ei + 2] = 20;
            }
        }
    }

    // ── 滑鼠繪製 ──────────────────────────────────────────────────

    private bool _painting = false;
    private bool _erasing  = false;

    private void HandleMouseDraw()
    {
        if (!(_painting || _erasing)) return;
        var mp   = GetLocalMousePosition();
        int tx   = (int)(mp.X / TilePixels);
        int ty   = (int)(mp.Y / TilePixels);
        int size = BrushSize;
        for (int dy = -size; dy <= size; dy++)
        for (int dx = -size; dx <= size; dx++)
        {
            if (dx * dx + dy * dy <= size * size)
                World.Set(tx + dx, ty + dy,
                    _erasing ? MaterialType.Air : SelectedMaterial);
        }
    }

    public int BrushSize { get; set; } = 2;

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)  _painting = mb.Pressed;
            if (mb.ButtonIndex == MouseButton.Right) _erasing  = mb.Pressed;
        }
    }
}
