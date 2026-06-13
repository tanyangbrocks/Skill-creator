namespace SkillCreator.World.Terrain;

using SkillCreator.World.Materials;

/// <summary>
/// 地形特徵基礎類別。
/// 每種地形（水池、熔岩區、蘑菇林、沼澤…）繼承此類別並實作以下三個方法。
///
/// 生命週期：
///   1. Initialize()  ── 從 seed 決定位置、大小等佈局（純計算，無副作用）
///   2. PlaceInWorld() ── 把 tile 寫入世界（僅初始生成時呼叫，懶加載 chunk 不呼叫）
///   3. GetSurfaceOverride() ── 懶加載 chunk 生成時查詢 tile 覆寫
///
/// 新增地形時只需：繼承此類、實作三個方法、加到 MapGenerator3D._terrainFeatures 清單。
/// </summary>
public abstract class TerrainFeature
{
    /// <summary>地形顯示名稱（供 debug / 編輯器顯示）。</summary>
    public abstract string Name { get; }

    /// <summary>
    /// 從 seed 計算地形特徵的佈局（位置、大小等），不寫入任何 tile。
    /// Generate() 和 InitTerrainParams() 都需要呼叫，確保懶加載路徑行為一致。
    /// </summary>
    public abstract void Initialize(int seed, int worldW, int worldH, int worldD);

    /// <summary>
    /// 根據地形高度函數計算衍生狀態（例如水面 Y）。
    /// Generate() 和 InitTerrainParams() 都需要呼叫；預設實作為空，子類按需覆寫。
    /// </summary>
    public virtual void Prepare(Func<int, int, int> getHeight) { }

    /// <summary>
    /// 把地形特徵的 tile 寫入世界（僅在新世界初始生成時呼叫）。
    /// 只需處理 x &lt; initW &amp;&amp; z &lt; initD 的初始條帶；範圍外由懶加載路徑處理。
    /// <paramref name="getHeight"/> 回傳指定 (wx, wz) 的自然地表 Y（tile 座標）。
    /// </summary>
    public abstract void PlaceInWorld(TileWorld3D world, Func<int, int, int> getHeight, int initW, int initD);

    /// <summary>
    /// 懶加載 chunk 生成時的 tile 覆寫查詢。
    /// 若此地形不影響 (wx, wz)，回傳 null（使用預設生成邏輯）。
    /// 若影響，回傳 (effectiveH, surfaceMat)：
    ///   • effectiveH：有效地表 Y；wy &lt; effectiveH → Air，wy == effectiveH → surfaceMat
    ///   • surfaceMat：地表材質（取代預設的 Dirt/Stone）
    /// </summary>
    public abstract (int h, MaterialType mat)? GetSurfaceOverride(int wx, int wz, int naturalH);
}
