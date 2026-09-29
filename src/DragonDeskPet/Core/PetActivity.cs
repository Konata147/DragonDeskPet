namespace DragonDeskPet.Core;

public enum PetActivity
{
    Greet, Pet, Feed, Cuddle, Hop, Dance, Stretch, LookAround, Land, Wake, Celebrate
}

public sealed record PetActivityInfo(string Caption, string Accent, int DurationMilliseconds);

/// <summary>Local character behavior, independent of animation technology and AI providers.</summary>
public static class PetActivities
{
    public static PetActivityInfo Describe(PetActivity activity) => activity switch
    {
        PetActivity.Greet => new("我在这里～", "✦", 1500),
        PetActivity.Pet => new("嘿嘿，再摸一下～", "♡", 1900),
        PetActivity.Feed => new("啊呜，谢谢款待！", "♪", 2200),
        PetActivity.Cuddle => new("贴贴，陪着你～", "♡ ♡", 2200),
        PetActivity.Hop => new("接住我的开心！", "✧", 1800),
        PetActivity.Dance => new("一起晃一晃～", "♫", 2400),
        PetActivity.Stretch => new("舒展一下～", "✧", 2200),
        PetActivity.LookAround => new("看看你在做什么～", "?", 1900),
        PetActivity.Land => new("稳稳落地！", "✦", 1100),
        PetActivity.Wake => new("醒啦，我在～", "☀", 2000),
        PetActivity.Celebrate => new("完成啦！给你小星星", "✦ ★ ✦", 2400),
        _ => throw new ArgumentOutOfRangeException(nameof(activity))
    };

    public static bool CanStart(PetState state, bool busy, bool dragging, bool visible) =>
        visible && !busy && !dragging && state is not (PetState.Thinking or PetState.Dragged);

    public static bool CanPlayAmbient(PetState state, bool busy, bool dragging, bool visible,
        bool focusing, bool panelOpen, bool reducedMotion, bool enabled) =>
        enabled && !reducedMotion && !focusing && !panelOpen && state == PetState.Idle
        && CanStart(state, busy, dragging, visible);
}
