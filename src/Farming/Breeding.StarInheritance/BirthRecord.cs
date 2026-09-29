namespace MC.Farming.BreedingStarInheritanceMod;

// Me = partner note on pregnant parent's ZDO: partner level + pregnancy stamp (vanilla s_pregnant ticks).
// Note count only while stamp match current pregnancy: old note from older pregnancy = ignored, harmless.
// Level 0 = bred alone. Stamp 0 = cleared (no note). Vanilla ignore unknown keys; key names start with GUID, new
// meaning = new name.
internal static class BirthRecord
{
    internal const string PartnerLevelKey = ModInfo.Guid + ".PartnerLevel";
    internal const string ConceivedAtKey = ModInfo.Guid + ".ConceivedAt";

    private static readonly int PartnerLevelHash = PartnerLevelKey.GetStableHashCode();
    private static readonly int ConceivedAtHash = ConceivedAtKey.GetStableHashCode();

    internal static void Write(ZDO zdo, int partnerLevel, long pregnancyStamp)
    {
        zdo.Set(PartnerLevelHash, partnerLevel);
        zdo.Set(ConceivedAtHash, pregnancyStamp);
    }

    internal static bool TryRead(ZDO zdo, long currentStamp, out int partnerLevel)
    {
        partnerLevel = 0;
        if (currentStamp == 0L)
        {
            return false;
        }
        var conceivedAt = zdo.GetLong(ConceivedAtHash, 0L);
        if (conceivedAt != currentStamp)
        {
            return false; // no note, cleared note, or note of other pregnancy
        }
        return zdo.GetInt(PartnerLevelHash, out partnerLevel);
    }

    // Me overwrite with 0, never remove: removal no bump revision and receiving game only add/overwrite keys
    // (ZDO.Deserialize), so removed key stay on server copy and other games. Written value reach everyone.
    // No note (no key, or already 0) = nothing to write.
    internal static void Clear(ZDO zdo)
    {
        if (zdo.GetLong(ConceivedAtHash, 0L) == 0L)
        {
            return;
        }
        zdo.Set(PartnerLevelHash, BirthRule.NoPartner);
        zdo.Set(ConceivedAtHash, 0L);
    }

    // Self test: -1 = no stamp key, 0 = cleared, else stamp of the note.
    internal static long StoredStamp(ZDO zdo)
    {
        return zdo.GetLong(ConceivedAtHash, -1L);
    }
}
