namespace MC.Exploration.SailingSkillMod.Patches;

// Me = one float field scaled for one call: value before, and value me wrote. Prefix call Scale, finalizer call PutBack.
// PutBack write the old value only if the field still hold what me wrote. Another mod that changed the field during the
// call (set it once, or scaled it and put it back its own way) keep its value, whatever order the patches run: me never
// write a stale copy over it, so nothing grow step after step.
internal struct ScaledField
{
    internal bool Set;
    internal float Saved;
    internal float Wrote;

    internal static ScaledField Scale(ref float field, float factor)
    {
        var state = new ScaledField { Set = true, Saved = field, Wrote = field * factor };
        field = state.Wrote;
        return state;
    }

    internal void PutBack(ref float field)
    {
        if (Set && field == Wrote)
        {
            field = Saved;
        }
    }
}
