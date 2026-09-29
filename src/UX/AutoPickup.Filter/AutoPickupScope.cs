namespace MC.UX.AutoPickupFilterMod;

// Me = the "inside local player's auto pickup loop" flag. IsPiece postfix only answer for filter while me open.
// AutoPickup prefix open me, finalizer close me (even when something throw), OnDeactivated close me too.
internal static class AutoPickupScope
{
    // True only while local player's Player.AutoPickup run, auto pickup is on and mode is not Everything.
    internal static bool Active;

    // Vanilla auto pickup on/off seen at last AutoPickup call. Null = not seen yet for this character
    // (then no "mode" message at login even if V was off before, flag survive logout).
    internal static bool? LastEnabled;
}
