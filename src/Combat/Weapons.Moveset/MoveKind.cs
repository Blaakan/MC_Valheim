namespace MC.Combat.WeaponsMovesetMod;

// Which move a swing is. None = normal swing.
internal enum MoveKind : byte
{
    None = 0,
    Jump,
    Roll,
}

// Me = names of the two moves for settings and logs. Index 0 = jump, 1 = roll (rules and cache arrays).
internal static class Moves
{
    internal const int Count = 2;
    internal const string JumpSection = "Jump attack";
    internal const string RollSection = "Roll attack";
    internal const string JumpAnimationSection = "Jump attack animations";
    internal const string RollAnimationSection = "Roll attack animations";

    internal static readonly MoveKind[] All = { MoveKind.Jump, MoveKind.Roll };

    // -1 for None.
    internal static int Index(MoveKind kind) => (int)kind - 1;

    internal static string Name(MoveKind kind) =>
        kind == MoveKind.Jump ? "jump attack" : kind == MoveKind.Roll ? "roll attack" : "normal swing";

    // Same, first letter big (start of a log line).
    internal static string Title(MoveKind kind) =>
        kind == MoveKind.Jump ? "Jump attack" : kind == MoveKind.Roll ? "Roll attack" : "Normal swing";

    internal static string AnimationSection(MoveKind kind) =>
        kind == MoveKind.Jump ? JumpAnimationSection : RollAnimationSection;

    // Last sentence of a warning about an animation setting. Server's rules: player's own settings do nothing on that
    // server (Decision 20), only server admin can change it (dedicated server has no player: never warn itself).
    internal static string AnimationAdvice(MoveKind kind, bool fromServer) =>
        fromServer
            ? $"This setting comes from the server (\"{AnimationSection(kind)}\" section; your own settings do not "
              + "apply on this server): ask the server admin to pick another animation from the list."
            : $"Pick another animation from the list in the \"{AnimationSection(kind)}\" settings.";
}
