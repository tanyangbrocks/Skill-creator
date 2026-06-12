namespace SkillCreator.World.Items;

using SkillCreator.World.Materials;

public class DroppedItemManager
{
    private readonly List<DroppedItem> _items = new();
    private readonly Random _rng = new();

    public IReadOnlyList<DroppedItem> Items => _items;

    // 由 TileWorld.OnTileDestroyed 事件觸發，依 DestroyReason 分流
    public void Spawn(GridPos pos, MaterialType mat, DestroyReason reason)
    {
        if (reason == DestroyReason.Explosion)
            return; // 爆炸碎片由爆炸端批次計算，呼叫 SpawnFragments

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

    /// <summary>
    /// 爆炸/採掘結束後批次呼叫：依 tileCount 換算 material unit，產生對應碎片數量。
    /// 1 unit ≈ 1000 tiles；Mining=100 碎片/unit（全回收），Explosion=20~80 碎片/unit。
    /// </summary>
    public void SpawnFragments(GridPos center, MaterialType mat, int tileCount, DestroyReason reason)
    {
        var data = MaterialRegistry.Get(mat);
        if (data.FragmentItem == ItemId.None) return;

        float units = tileCount / 1000f;
        int fragments = reason == DestroyReason.Mining
            ? (int)(units * 100)
            : (int)(units * _rng.Next(20, 81));
        if (fragments <= 0) return;

        _items.Add(new DroppedItem(center, new ItemStack(data.FragmentItem, fragments)));
    }

    public void Update(TileWorld3D world, PlayerController player, float delta)
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
                if (added > 0)
                {
                    // 拾取裝備時，若對應裝備欄空則自動穿戴
                    var data = ItemRegistry.Get(item.Stack.ItemId);
                    if (data.EquipSlot != EquipmentSlotType.None)
                    {
                        bool slotEmpty = data.EquipSlot switch
                        {
                            EquipmentSlotType.Weapon    => player.Equipment.WeaponId    == ItemId.None,
                            EquipmentSlotType.Armor     => player.Equipment.ArmorId     == ItemId.None,
                            EquipmentSlotType.Accessory => player.Equipment.AccessoryId == ItemId.None,
                            _                           => false,
                        };
                        if (slotEmpty)
                        {
                            for (int j = 0; j < Inventory.TotalSize; j++)
                            {
                                if (player.Inventory.Slots[j].ItemId == item.Stack.ItemId)
                                {
                                    player.Equipment.TryEquip(player.Inventory, j);
                                    break;
                                }
                            }
                        }
                    }
                }
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
