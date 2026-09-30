namespace DragonDeskPet.Core;

public enum PetSnack { Cookie, Strawberry, Cake, Candy, CottonCandy }
public enum PetDance { Step, Guofeng, WingTail }

public static class PetInteractionVariants
{
    public static IReadOnlyList<PetSnack> Snacks { get; } = Enum.GetValues<PetSnack>();
    public static IReadOnlyList<PetDance> Dances { get; } = Enum.GetValues<PetDance>();

    public static string Name(PetSnack snack) => snack switch
    {
        PetSnack.Cookie => "饼干",
        PetSnack.Strawberry => "草莓",
        PetSnack.Cake => "蛋糕",
        PetSnack.Candy => "糖果",
        PetSnack.CottonCandy => "棉花糖",
        _ => throw new ArgumentOutOfRangeException(nameof(snack))
    };

    public static string Glyph(PetSnack snack) => snack switch
    {
        PetSnack.Cookie => "🍪",
        PetSnack.Strawberry => "🍓",
        PetSnack.Cake => "🍰",
        PetSnack.Candy => "🍬",
        PetSnack.CottonCandy => "☁",
        _ => throw new ArgumentOutOfRangeException(nameof(snack))
    };

    public static string ClipId(PetSnack snack) => $"Feed{snack}";
    public static string Caption(PetSnack snack) => snack switch
    {
        PetSnack.Cookie => "饼干脆脆的，啊呜～",
        PetSnack.Strawberry => "草莓甜甜的，谢谢你！",
        PetSnack.Cake => "小蛋糕！今天好开心～",
        PetSnack.Candy => "糖果甜得眯起眼啦～",
        PetSnack.CottonCandy => "像小云一样软绵绵～",
        _ => throw new ArgumentOutOfRangeException(nameof(snack))
    };

    public static string Name(PetDance dance) => dance switch
    {
        PetDance.Step => "轻快踏步",
        PetDance.Guofeng => "国风轻舞",
        PetDance.WingTail => "翼尾合拍",
        _ => throw new ArgumentOutOfRangeException(nameof(dance))
    };

    public static string ClipId(PetDance dance) => $"Dance{dance}";
    public static string Caption(PetDance dance) => dance switch
    {
        PetDance.Step => "跟着节拍踏踏步～",
        PetDance.Guofeng => "轻轻转腕，跳支国风舞～",
        PetDance.WingTail => "翅膀和尾巴也来合拍！",
        _ => throw new ArgumentOutOfRangeException(nameof(dance))
    };
}
