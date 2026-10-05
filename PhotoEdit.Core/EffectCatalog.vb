Imports System.Drawing

''' <summary>特效筆的效果（數值存進編輯檔，不可更動；新的往後加）。</summary>
Public Enum FxKind
    Fire = 0
    Smoke = 1
    Stars = 2
    Blood = 3
    ' 光／能量
    Flash = 4
    LightDots = 5
    Halo = 6
    LightRays = 7
    LensFlare = 8
    Beam = 9
    EnergyParticles = 10
    MagicAura = 11
    Arc = 12
    Thunder = 13
    Sparks = 14
    ExplosionLight = 15
    ' 火／煙／爆炸
    Embers = 16
    BlackSmoke = 17
    Explosion = 18
    Debris = 19
    Ash = 20
    SparkSpray = 21
    HeatHaze = 22
    BurnEdge = 23
    ' 液體／自然
    Splash = 24
    WaterDrops = 25
    Rain = 26
    BloodSplatter = 27
    Mud = 28
    OilStain = 29
    DirtyWater = 30
    Snow = 31
    IceCrystal = 32
    ' 動態效果
    SpeedLines = 33
    MotionLines = 34
    Whirlwind = 35
    Vortex = 36
    Airflow = 37
    DustScatter = 38
    Sandstorm = 39
    ParticleFlow = 40
    Trail = 41
    ' 魔法／科幻
    MagicCircle = 42
    Runes = 43
    HaloRing = 44
    Stardust = 45
    Cosmic = 46
    BlackHole = 47
    EnergyWave = 48
    Laser = 49
    Plasma = 50
    Hud = 51
    Glitch = 52
    ' 漫畫效果
    ComicBurst = 53
    FocusLines = 54
    RadialLines = 55
    Halftone = 56
    ComicSpeed = 57
    ShakeLines = 58
    ShockWave = 59
    ComicShadow = 60
    ComicDryBrush = 61
    ' 動畫／特攝／電影
    AtomicBreath = 62
    GravityBeam = 63
    SpiralHeatRay = 64
    SpeciumRay = 65
    ZeperionRay = 66
    M87Ray = 67
End Enum

''' <summary>紋理筆的材質（數值存進編輯檔，不可更動；新的往後加）。</summary>
Public Enum MaterialKind
    Stone = 0
    Wood = 1
    BrushedMetal = 2
    Skin = 3
    ' 自然
    Rock = 4
    Granite = 5
    Marble = 6
    Sand = 7
    Soil = 8
    Mud = 9
    Desert = 10
    Gravel = 11
    CrackedSoil = 12
    ' 植物／木材
    Bark = 13
    TreeRings = 14
    Leaf = 15
    Grass = 16
    Shrub = 17
    Vine = 18
    Moss = 19
    Petal = 20
    DeadLeaf = 21
    ' 建築
    Cement = 22
    Concrete = 23
    Brick = 24
    StoneWall = 25
    WallCrack = 26
    OldWall = 27
    PeelingPaint = 28
    Rust = 29
    Tile = 30
    ' 手工／材料
    Paper = 31
    Kraft = 32
    Parchment = 33
    Fabric = 34
    Canvas = 35
    Blanket = 36
    Fur = 37
    Plank = 38
    Leather = 39
    Burlap = 40
    ' 金屬
    Iron = 41
    Steel = 42
    Copper = 43
    Brass = 44
    Silver = 45
    Gold = 46
    Corroded = 47
    Scratched = 48
    CastIron = 49
    ' 生物／有機
    Hair = 50
    Leopard = 51
    Tiger = 52
    Zebra = 53
    Lizard = 54
    Snake = 55
    Dino = 56
    Monster = 57
    Feather = 58
    FishScale = 59
    Slime = 60
    Meat = 61
    Bone = 62
    ' 之後新增
    Mercury = 63
End Enum

''' <summary>
''' 特效與紋理的分類目錄：面板依分類列出，選取時把線條色換成建議顏色。
''' Tinted = True 表示顏色由線條色決定；False 表示用材質／效果本身的顏色（線條色只略微影響或不影響）。
''' </summary>
Public NotInheritable Class EffectCatalog
    Private Sub New()
    End Sub

    Public Structure Entry
        Public Value As Integer
        Public Name As String
        Public Color As Color
        Public Tinted As Boolean
    End Structure

    Public Structure Category
        Public Name As String
        Public Items As Entry()
    End Structure

    Private Shared Function E(value As Integer, name As String, r As Integer, g As Integer, b As Integer, Optional tinted As Boolean = True) As Entry
        Return New Entry With {.Value = value, .Name = name, .Color = Color.FromArgb(r, g, b), .Tinted = tinted}
    End Function

    Public Shared ReadOnly FxCategories As Category() = {
        New Category With {.Name = "光／能量", .Items = {
            E(FxKind.Stars, "星光", 255, 220, 120), E(FxKind.Flash, "閃光", 255, 245, 200), E(FxKind.LightDots, "光點", 255, 200, 140),
            E(FxKind.Halo, "光暈", 255, 220, 150), E(FxKind.LightRays, "光線", 120, 200, 255), E(FxKind.LensFlare, "鏡頭光斑", 255, 190, 110),
            E(FxKind.Beam, "光束", 255, 240, 190), E(FxKind.EnergyParticles, "能量粒子", 90, 220, 255), E(FxKind.MagicAura, "魔法光環", 190, 120, 255),
            E(FxKind.Arc, "電弧／閃電", 130, 180, 255), E(FxKind.Thunder, "雷電", 170, 150, 255), E(FxKind.Sparks, "火花", 255, 190, 80),
            E(FxKind.ExplosionLight, "爆炸光", 255, 170, 60)}},
        New Category With {.Name = "火／煙／爆炸", .Items = {
            E(FxKind.Fire, "火焰", 255, 160, 60, False), E(FxKind.Embers, "火星", 255, 140, 40, False), E(FxKind.Smoke, "煙霧", 200, 200, 205),
            E(FxKind.BlackSmoke, "黑煙", 40, 38, 38), E(FxKind.Explosion, "爆炸", 255, 150, 50, False), E(FxKind.Debris, "爆炸碎片", 90, 80, 72),
            E(FxKind.Ash, "灰燼", 150, 145, 140), E(FxKind.SparkSpray, "火花飛散", 255, 180, 70, False), E(FxKind.HeatHaze, "熱氣", 255, 230, 210),
            E(FxKind.BurnEdge, "燃燒邊緣", 255, 120, 30, False)}},
        New Category With {.Name = "液體／自然", .Items = {
            E(FxKind.Splash, "水花", 120, 190, 245), E(FxKind.WaterDrops, "水滴", 150, 205, 245), E(FxKind.Rain, "雨", 210, 225, 245),
            E(FxKind.BloodSplatter, "血液飛濺", 140, 5, 15), E(FxKind.Blood, "血滴", 150, 10, 20), E(FxKind.Mud, "泥漿", 100, 70, 40),
            E(FxKind.OilStain, "油漬", 30, 28, 25), E(FxKind.DirtyWater, "污水", 95, 90, 55), E(FxKind.Snow, "雪花", 255, 255, 255),
            E(FxKind.IceCrystal, "冰晶", 170, 225, 255)}},
        New Category With {.Name = "動態效果", .Items = {
            E(FxKind.SpeedLines, "速度線", 255, 255, 255), E(FxKind.MotionLines, "動態線", 255, 255, 255), E(FxKind.Whirlwind, "旋風", 190, 190, 185),
            E(FxKind.Vortex, "漩渦", 120, 190, 255), E(FxKind.Airflow, "氣流", 230, 245, 255), E(FxKind.DustScatter, "塵土飛散", 165, 135, 100),
            E(FxKind.Sandstorm, "沙塵", 215, 175, 115), E(FxKind.ParticleFlow, "粒子流", 120, 220, 255), E(FxKind.Trail, "拖影", 255, 255, 255)}},
        New Category With {.Name = "魔法／科幻", .Items = {
            E(FxKind.MagicCircle, "魔法陣", 140, 200, 255), E(FxKind.Runes, "符文", 255, 200, 90), E(FxKind.HaloRing, "光環", 255, 230, 140),
            E(FxKind.Stardust, "星塵", 255, 225, 160), E(FxKind.Cosmic, "宇宙粒子", 170, 120, 255), E(FxKind.BlackHole, "黑洞", 255, 170, 80),
            E(FxKind.EnergyWave, "能量波", 90, 230, 255), E(FxKind.Laser, "雷射", 255, 40, 60), E(FxKind.Plasma, "電漿", 220, 90, 255),
            E(FxKind.Hud, "科技 HUD", 80, 230, 255), E(FxKind.Glitch, "數位故障 Glitch", 0, 255, 230)}},
        New Category With {.Name = "漫畫效果", .Items = {
            E(FxKind.ComicBurst, "爆炸框", 255, 215, 50), E(FxKind.FocusLines, "集中線", 20, 20, 20), E(FxKind.RadialLines, "放射線", 20, 20, 20),
            E(FxKind.Halftone, "網點", 20, 20, 20), E(FxKind.ComicSpeed, "速度線（漫畫）", 20, 20, 20), E(FxKind.ShakeLines, "震動線", 20, 20, 20),
            E(FxKind.ShockWave, "衝擊波", 20, 20, 20), E(FxKind.ComicShadow, "漫畫陰影", 20, 20, 20), E(FxKind.ComicDryBrush, "漫畫飛白", 20, 20, 20)}},
        New Category With {.Name = "動畫／特攝／電影", .Items = {
            E(FxKind.AtomicBreath, "傳奇哥吉拉-原子吐息", 40, 120, 255), E(FxKind.GravityBeam, "傳奇基多拉-引力光線", 255, 196, 70),
            E(FxKind.SpiralHeatRay, "1995紅蓮哥吉拉-放射熱線", 255, 80, 25), E(FxKind.SpeciumRay, "奧特曼-斯派修姆光線", 60, 170, 255),
            E(FxKind.ZeperionRay, "奧特曼-哉佩利敖光線", 255, 165, 45), E(FxKind.M87Ray, "奧特曼-M87光線", 255, 110, 200, False)}}}

    Public Shared ReadOnly MaterialCategories As Category() = {
        New Category With {.Name = "自然材質", .Items = {
            E(MaterialKind.Stone, "石頭", 150, 145, 135, False), E(MaterialKind.Rock, "岩石", 110, 100, 90, False), E(MaterialKind.Granite, "花崗岩", 160, 150, 150, False),
            E(MaterialKind.Marble, "大理石", 235, 232, 228, False), E(MaterialKind.Sand, "沙", 220, 190, 130, False), E(MaterialKind.Soil, "土", 100, 70, 45, False),
            E(MaterialKind.Mud, "泥", 75, 52, 32, False), E(MaterialKind.Desert, "沙漠", 225, 160, 90, False), E(MaterialKind.Gravel, "礫石", 140, 135, 125, False),
            E(MaterialKind.CrackedSoil, "裂土", 180, 145, 100, False)}},
        New Category With {.Name = "植物／木材", .Items = {
            E(MaterialKind.Wood, "木紋", 150, 100, 60, False), E(MaterialKind.Bark, "樹皮", 85, 70, 55, False), E(MaterialKind.TreeRings, "年輪", 190, 140, 85, False),
            E(MaterialKind.Leaf, "樹葉", 70, 140, 40, False), E(MaterialKind.Grass, "草", 80, 150, 40, False), E(MaterialKind.Shrub, "灌木", 40, 90, 30, False),
            E(MaterialKind.Vine, "藤蔓", 60, 110, 35, False), E(MaterialKind.Moss, "苔蘚", 95, 135, 30, False), E(MaterialKind.Petal, "花瓣", 245, 150, 185),
            E(MaterialKind.DeadLeaf, "枯葉", 170, 105, 40, False)}},
        New Category With {.Name = "建築材質", .Items = {
            E(MaterialKind.Cement, "水泥", 160, 160, 155, False), E(MaterialKind.Concrete, "混凝土", 140, 140, 135, False), E(MaterialKind.Brick, "磚牆", 160, 70, 45, False),
            E(MaterialKind.StoneWall, "石牆", 130, 125, 115, False), E(MaterialKind.WallCrack, "牆面裂紋", 210, 200, 185, False), E(MaterialKind.OldWall, "舊牆", 190, 175, 150, False),
            E(MaterialKind.PeelingPaint, "油漆剝落", 70, 140, 170), E(MaterialKind.Rust, "鐵鏽", 160, 80, 30, False), E(MaterialKind.Tile, "磁磚", 120, 175, 200)}},
        New Category With {.Name = "手工／材料", .Items = {
            E(MaterialKind.Paper, "紙張", 242, 240, 230, False), E(MaterialKind.Kraft, "牛皮紙", 170, 128, 85, False), E(MaterialKind.Parchment, "羊皮紙", 230, 205, 150, False),
            E(MaterialKind.Fabric, "布料", 70, 100, 170), E(MaterialKind.Canvas, "帆布", 210, 195, 160, False), E(MaterialKind.Blanket, "毛毯", 180, 60, 60),
            E(MaterialKind.Fur, "毛皮", 140, 100, 65, False), E(MaterialKind.Plank, "木板", 160, 110, 65, False), E(MaterialKind.Leather, "皮革", 120, 70, 40),
            E(MaterialKind.Burlap, "麻布", 180, 150, 100, False)}},
        New Category With {.Name = "金屬", .Items = {
            E(MaterialKind.Iron, "鐵", 110, 110, 115, False), E(MaterialKind.Steel, "鋼", 160, 168, 180, False), E(MaterialKind.Copper, "銅", 215, 120, 75, False),
            E(MaterialKind.Brass, "黃銅", 205, 165, 80, False), E(MaterialKind.Silver, "銀", 210, 212, 218, False), E(MaterialKind.Gold, "金", 240, 190, 70, False),
            E(MaterialKind.Corroded, "鏽蝕金屬", 130, 110, 95, False), E(MaterialKind.BrushedMetal, "拉絲金屬", 180, 184, 190, False),
            E(MaterialKind.Scratched, "刮痕金屬", 150, 155, 162, False), E(MaterialKind.CastIron, "鑄鐵", 60, 60, 62, False),
            E(MaterialKind.Mercury, "水銀（液態金屬）", 205, 210, 220, False)}},
        New Category With {.Name = "生物／有機", .Items = {
            E(MaterialKind.Skin, "皮膚", 235, 190, 160), E(MaterialKind.Hair, "毛髮", 90, 60, 40), E(MaterialKind.Leopard, "豹紋", 215, 160, 80, False),
            E(MaterialKind.Tiger, "虎紋", 230, 130, 35, False), E(MaterialKind.Zebra, "斑馬紋", 240, 240, 235, False), E(MaterialKind.Lizard, "蜥蜴皮", 110, 130, 50, False),
            E(MaterialKind.Snake, "蛇皮", 160, 130, 80, False), E(MaterialKind.Dino, "恐龍皮", 100, 110, 85, False), E(MaterialKind.Monster, "怪獸皮膚", 90, 150, 70),
            E(MaterialKind.Feather, "羽毛", 80, 120, 200), E(MaterialKind.FishScale, "魚鱗", 150, 185, 205), E(MaterialKind.Slime, "黏液", 110, 220, 70),
            E(MaterialKind.Meat, "肉", 170, 40, 40, False), E(MaterialKind.Bone, "骨頭", 230, 220, 190, False)}}}

    ''' <summary>依列舉值排列的名稱（找不到的值為空字串）。</summary>
    Public Shared ReadOnly FxNames As String() = NamesByValue(FxCategories)
    Public Shared ReadOnly MaterialNames As String() = NamesByValue(MaterialCategories)

    Private Shared Function NamesByValue(cats As Category()) As String()
        Dim items = cats.SelectMany(Function(c) c.Items).ToList()
        Dim names(items.Max(Function(i) i.Value)) As String
        For i = 0 To names.Length - 1
            names(i) = ""
        Next
        For Each it In items
            names(it.Value) = it.Name
        Next
        Return names
    End Function

    ''' <summary>value 所在的分類索引與分類內索引；找不到時 (0, 0)。</summary>
    Public Shared Function Locate(cats As Category(), value As Integer) As (Category As Integer, Item As Integer)
        For c = 0 To cats.Length - 1
            For i = 0 To cats(c).Items.Length - 1
                If cats(c).Items(i).Value = value Then Return (c, i)
            Next
        Next
        Return (0, 0)
    End Function
End Class
