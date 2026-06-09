namespace SkillCreator.World.Items;

using SkillCreator.World.Materials;

public class DroppedItemManager
{
    private readonly List<DroppedItem> _items = new();
    private readonly Random _rng = new();

    public IReadOnlyList<DroppedItem> Items => _items;

    // 由 TileWorld.OnTileDestroyed 事件觸發（爆炸使用 Set 直接清格，不觸發此事件，故不掉落）
    public void Spawn(GridPos pos, MaterialType mat)
    {
        var data = MaterialRegistry.Get(mat);
        if (!data.IsMineable || data.DefaultDrops.Length == 0) return;

        foreach (var drop in data.DefaultDrops)
        {
            if (_rng.NextSingle() > drop.Chance) continue;
            int count = _rng.Next(drop.MinCount, drop.MaxCount + 1);
            if (count <= 0) continue;
            _items.Add(new DroppedItem(pos, new ItemStack(drop.ItemId, count)));
        }
    }

    public void Update(TileWorld world, PlayerController player, float delta)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.Update(world, delta);

            if (!item.IsAlive) { _items.RemoveAt(i); continue; }

            // 自動拾取：玩家 2 格以內
            int dx = Math.Abs(item.Position.X - player.Position.X);
            int dy = Math.Abs(item.Position.Y - player.Position.Y);
            if (dx <= 2 && dy <= 2)
            {
                int added = player.Inventory.TryAdd(item.Stack.ItemId, item.Stack.Count);
                if (added >= item.Stack.Count)
                {
                    _items.RemoveAt(i);
                }
                else if (added > 0)
                {
                    item.Stack = item.Stack.WithCount(item.Stack.Count - added);
                }
            }
        }
    }
}
