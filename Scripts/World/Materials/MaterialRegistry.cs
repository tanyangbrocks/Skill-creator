namespace SkillCreator.World.Materials;

using Godot;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.World.Items;

public static class MaterialRegistry
{
    private static readonly MaterialData[] _data = new MaterialData[Enum.GetValues<MaterialType>().Length];

    static MaterialRegistry()
    {
        // 不可採掘的材質：使用預設值（IsMineable=false）
        Register(new MaterialData(MaterialType.Air,   "空氣",  new Color(0.10f, 0.10f, 0.13f), PhysicsCategory.Empty,  false, 0.0f,  0,   0  )
            { NativeElement = ElementType.None  });
        Register(new MaterialData(MaterialType.Water, "水",    new Color(0.18f, 0.42f, 0.82f), PhysicsCategory.Liquid, false, 4.0f,  0,   0  )
            { NativeElement = ElementType.Water, IsTransparent = true, Opacity = 0.55f });
        Register(new MaterialData(MaterialType.Lava,  "岩漿", new Color(0.90f, 0.28f, 0.06f), PhysicsCategory.Liquid, false, 7.0f,  0,   0  )
            { NativeElement = ElementType.Fire  });
        Register(new MaterialData(MaterialType.Fire,  "火",    new Color(1.00f, 0.50f, 0.10f), PhysicsCategory.Gas,    false, 0.5f,  30,  90 )
            { NativeElement = ElementType.Fire,  IsTransparent = true, Opacity = 0.65f });
        Register(new MaterialData(MaterialType.Steam, "蒸汽", new Color(0.70f, 0.80f, 0.90f), PhysicsCategory.Gas,    false, 0.2f,  60,  120)
            { NativeElement = ElementType.Water, IsTransparent = true, Opacity = 0.35f });  // 蒸汽仍屬水元素（氣化的水）

        // 可採掘的材質：補上採掘屬性、掉落表與元素屬性
        Register(new MaterialData(MaterialType.Dirt,  "土",   new Color(0.48f, 0.34f, 0.20f), PhysicsCategory.Static, false, 8.0f,  0,   0  )
            { IsMineable = true, Hardness = 15, RequiredToolTier = 0, BlastResistance = 0.5f,
              NativeElement = ElementType.Earth,
              DefaultDrops = [new ItemDrop(ItemId.BlockDirt,  1, 1)],
              FragmentItem = ItemId.FragmentDirt });
        Register(new MaterialData(MaterialType.Sand,  "沙",   new Color(0.84f, 0.74f, 0.40f), PhysicsCategory.Powder, false, 6.0f,  0,   0  )
            { IsMineable = true, Hardness = 10, RequiredToolTier = 0, BlastResistance = 0.3f,
              NativeElement = ElementType.Earth,
              DefaultDrops = [new ItemDrop(ItemId.BlockSand,  1, 1)],
              FragmentItem = ItemId.FragmentSand });
        Register(new MaterialData(MaterialType.Wood,  "木",   new Color(0.38f, 0.26f, 0.12f), PhysicsCategory.Static, true,  9.0f,  60,  120)
            { IsMineable = true, Hardness = 20, RequiredToolTier = 0, BlastResistance = 1.0f,
              NativeElement = ElementType.Wood,
              DefaultDrops = [new ItemDrop(ItemId.BlockWood,  1, 1)],
              FragmentItem = ItemId.FragmentWood });
        Register(new MaterialData(MaterialType.Stone, "石",   new Color(0.50f, 0.50f, 0.52f), PhysicsCategory.Static, false, 10.0f, 0,   0  )
            { IsMineable = true, Hardness = 40, RequiredToolTier = 1, BlastResistance = 2.0f,
              NativeElement = ElementType.Earth,
              DefaultDrops = [new ItemDrop(ItemId.BlockStone, 1, 1)],
              FragmentItem = ItemId.FragmentStone });
        Register(new MaterialData(MaterialType.Ash,   "灰燼", new Color(0.35f, 0.35f, 0.38f), PhysicsCategory.Static, false, 5.0f,  0,   0  )
            { IsMineable = true, Hardness = 5,  RequiredToolTier = 0, BlastResistance = 0.2f,
              NativeElement = ElementType.None,
              DefaultDrops = [new ItemDrop(ItemId.BlockAsh,   1, 1)],
              FragmentItem = ItemId.FragmentAsh });

        // ── 礦石（W-4）──────────────────────────────────────────────────
        Register(new MaterialData(MaterialType.CoalOre, "煤礦", new Color(0.22f, 0.22f, 0.24f), PhysicsCategory.Static, false, 10.0f, 0, 0)
            { IsMineable = true, Hardness = 20, RequiredToolTier = 1, BlastResistance = 1.5f,
              NativeElement = ElementType.Earth,
              DefaultDrops = [new ItemDrop(ItemId.OreCoal, 1, 2)],
              FragmentItem = ItemId.FragmentCoal });

        Register(new MaterialData(MaterialType.CopperOre, "銅礦", new Color(0.60f, 0.36f, 0.16f), PhysicsCategory.Static, false, 10.0f, 0, 0)
            { IsMineable = true, Hardness = 30, RequiredToolTier = 1, BlastResistance = 1.8f,
              NativeElement = ElementType.Metal,
              DefaultDrops = [new ItemDrop(ItemId.OreCopperRaw, 1, 2)],
              FragmentItem = ItemId.FragmentCopper });

        Register(new MaterialData(MaterialType.IronOre, "鐵礦", new Color(0.50f, 0.30f, 0.26f), PhysicsCategory.Static, false, 10.0f, 0, 0)
            { IsMineable = true, Hardness = 45, RequiredToolTier = 1, BlastResistance = 2.2f,
              NativeElement = ElementType.Metal,
              DefaultDrops = [new ItemDrop(ItemId.OreIronRaw, 1, 2)],
              FragmentItem = ItemId.FragmentIron });

        Register(new MaterialData(MaterialType.MagicCrystalOre, "魔晶礦", new Color(0.12f, 0.68f, 0.80f), PhysicsCategory.Static, false, 10.0f, 0, 0)
            { IsMineable = true, Hardness = 65, RequiredToolTier = 2, BlastResistance = 2.8f,
              NativeElement = ElementType.Light,
              DefaultDrops = [new ItemDrop(ItemId.OreMagicCrystal, 1, 1)],
              FragmentItem = ItemId.FragmentMagicCrystal });
    }

    private static void Register(MaterialData d) => _data[(int)d.Type] = d;

    public static MaterialData Get(MaterialType t) => _data[(int)t];

    // 渲染顏色（加上 variant 微小色差，視覺更自然；alpha = 材質 Opacity）
    public static Color GetColor(MaterialType t, byte variant)
    {
        var data = _data[(int)t];
        var c    = data.BaseColor;
        float v  = (variant / 255f) * 0.06f - 0.03f;
        return new Color(
            Math.Clamp(c.R + v, 0f, 1f),
            Math.Clamp(c.G + v, 0f, 1f),
            Math.Clamp(c.B + v, 0f, 1f),
            data.Opacity);
    }
}
