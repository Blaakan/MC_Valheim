namespace MC.UX.AutoPickupFilterMod;

// Me = three auto pickup filter modes. Name text saved in character (m_customData): never rename a member.
internal enum FilterMode
{
    // Pick up all, like vanilla.
    Everything,

    // Pick up all except Ignored list.
    SkipIgnored,

    // Pick up only Selected list.
    OnlySelected,
}
