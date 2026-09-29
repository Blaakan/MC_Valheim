namespace MC.Shared;

// Me = what kind of thing an item is, for sort and filter UIs. Shared so every MC mod put same item in same group
// (Crafting Search and Sort menu, Sort Chest type order). Each mod make own groups and labels on top of kinds.
// Pure enum compares: no state, no Harmony, no allocation. First rule that match win (see Classify).
internal enum ItemKind
{
    Other = 0,     // None, Customization, unknown modded types
    Weapon,        // melee, bows, crossbows, staffs; family = SharedData.m_skillType
    Ammo,          // arrows, bolts, fishing bait
    Shield,
    Helmet,
    Chest,
    Legs,
    Hands,
    Cape,
    Utility,       // belt, wishbone
    Trinket,
    Tool,          // hammer, hoe, cultivator
    SkillTool,     // pickaxe, fishing rod, scythe: weapon slot but gather thing
    Torch,
    Food,          // consumable that fill health, stamina or eitr
    Potion,        // other consumable: meads, potions
    Material,
    Fish,
    Trophy,
    Misc,          // Misc item type, tankards
}

internal static class ItemKinds
{
    // Me sort item into kind. Null = Other.
    internal static ItemKind Classify(ItemDrop.ItemData.SharedData s)
    {
        if (s == null)
        {
            return ItemKind.Other;
        }

        switch (s.m_itemType)
        {
            case ItemDrop.ItemData.ItemType.Ammo:
            case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                return ItemKind.Ammo;
            case ItemDrop.ItemData.ItemType.Shield:
                return ItemKind.Shield;
            case ItemDrop.ItemData.ItemType.Helmet:
                return ItemKind.Helmet;
            case ItemDrop.ItemData.ItemType.Chest:
                return ItemKind.Chest;
            case ItemDrop.ItemData.ItemType.Legs:
                return ItemKind.Legs;
            case ItemDrop.ItemData.ItemType.Hands:
                return ItemKind.Hands;
            case ItemDrop.ItemData.ItemType.Shoulder:
                return ItemKind.Cape;
            case ItemDrop.ItemData.ItemType.Utility:
                return ItemKind.Utility;
            case ItemDrop.ItemData.ItemType.Trinket:
                return ItemKind.Trinket;
            case ItemDrop.ItemData.ItemType.Tool:
                return ItemKind.Tool;
            case ItemDrop.ItemData.ItemType.Torch:
                return ItemKind.Torch;
            case ItemDrop.ItemData.ItemType.Consumable:
                return s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f ? ItemKind.Food : ItemKind.Potion;
            case ItemDrop.ItemData.ItemType.Material:
                return ItemKind.Material;
            case ItemDrop.ItemData.ItemType.Fish:
                return ItemKind.Fish;
            case ItemDrop.ItemData.ItemType.Trophy:
                return ItemKind.Trophy;
            case ItemDrop.ItemData.ItemType.Misc:
                return ItemKind.Misc;
        }

        if (!IsWeaponType(s.m_itemType))
        {
            return ItemKind.Other;
        }

        // Weapon slot, but maybe not for fight. Torch prefab is one-handed weapon with torch animation.
        var anim = s.m_animationState;
        if (anim == ItemDrop.ItemData.AnimationState.Torch || anim == ItemDrop.ItemData.AnimationState.LeftTorch)
        {
            return ItemKind.Torch;
        }
        if (anim == ItemDrop.ItemData.AnimationState.FishingRod || anim == ItemDrop.ItemData.AnimationState.Scythe)
        {
            return ItemKind.SkillTool;
        }
        switch (s.m_skillType)
        {
            case Skills.SkillType.Pickaxes:
            case Skills.SkillType.WoodCutting:
            case Skills.SkillType.Fishing:
            case Skills.SkillType.Farming:
                return ItemKind.SkillTool;
        }
        // Tankards (feaster animation) = drink thing, not weapon.
        return anim == ItemDrop.ItemData.AnimationState.Feaster ? ItemKind.Misc : ItemKind.Weapon;
    }

    // Item type that sit in weapon hands (vanilla IsWeapon without torch).
    internal static bool IsWeaponType(ItemDrop.ItemData.ItemType t)
    {
        return t == ItemDrop.ItemData.ItemType.OneHandedWeapon
               || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon
               || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
               || t == ItemDrop.ItemData.ItemType.Bow
               || t == ItemDrop.ItemData.ItemType.Attach_Atgeir;
    }
}
