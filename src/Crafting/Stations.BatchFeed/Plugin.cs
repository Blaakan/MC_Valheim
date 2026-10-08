using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me make no UI object: hint is text added to vanilla hover. Off = patches gone = hint gone, one item per press.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<int> Amount;
    internal static ConfigEntry<KeyCode> ModifierKey;
    internal static ConfigEntry<bool> ShowHint;

    protected override void BindConfig()
    {
        Amount = Config.Bind("General", "Amount", 5, new ConfigDescription(
            "How many items one batch press adds at most. Fewer are added when the station has less room or you carry fewer.",
            new AcceptableValueRange<int>(2, 50),
            new ConfigurationManagerAttributes { Order = 90 }));
        ModifierKey = Config.Bind("General", "ModifierKey", KeyCode.None, new ConfigDescription(
            "Key to hold while pressing Use (E) to add several items. None = the game's own \"Alternative placement\" key, "
            + "which is Left Shift by default (Right Shift is not part of it); you can change it in the game's Settings, controls. "
            + "Pick a key here to use a different key for this mod only, for example RightShift or LeftAlt, which the game does "
            + "not use while you play; avoid LeftControl (Crouch) and other keys that already do something. With a controller, "
            + "this setting is ignored and the game's own button is always used: Alternative placement on the default "
            + "controller layout, the alt-keys button (the one you hold for alternative functions) on the Alternative 1 and 2 "
            + "layouts. The hover hint shows which button to hold.",
            null,
            new ConfigurationManagerAttributes { Order = 80 }));
        ShowHint = Config.Bind("General", "ShowHint", true, new ConfigDescription(
            "Show the batch key hint below the Use hint when you look at a station.",
            null,
            new ConfigurationManagerAttributes { Order = 70 }));

        // Hint line cached: rebuild on any change (panel, ConfigurationManager, file edit).
        Amount.SettingChanged += (_, _) => HoverHint.Invalidate();
        ModifierKey.SettingChanged += (_, _) => HoverHint.Invalidate();
        ShowHint.SettingChanged += (_, _) => HoverHint.Invalidate();
    }

#if DEBUG
    // Self test only (Debug build): settings and input forced in memory, never written to the cfg. Null = real value.
    // Who set one must call HoverHint.Invalidate() (hint line is cached).
    internal static int? TestAmount;
    internal static KeyCode? TestModifierKey;
    internal static bool? TestShowHint;
    internal static bool? TestGamepad; // controller in use?
    internal static bool? TestKeyHeld; // mod-only key down?
#endif

    // Me = single door to the settings and input the feature read. Release build: plain config and game value.
    internal static int ReadAmount()
    {
#if DEBUG
        if (TestAmount.HasValue)
        {
            return TestAmount.Value;
        }
#endif
        return Amount.Value;
    }

    internal static KeyCode ReadModifierKey()
    {
#if DEBUG
        if (TestModifierKey.HasValue)
        {
            return TestModifierKey.Value;
        }
#endif
        return ModifierKey.Value;
    }

    internal static bool ReadShowHint()
    {
#if DEBUG
        if (TestShowHint.HasValue)
        {
            return TestShowHint.Value;
        }
#endif
        return ShowHint.Value;
    }

    internal static bool GamepadActive()
    {
#if DEBUG
        if (TestGamepad.HasValue)
        {
            return TestGamepad.Value;
        }
#endif
        return ZInput.IsGamepadActive();
    }

    // Mod-only key down now? ZInput throw ArgumentException on a key it no can map: caller catch.
    internal static bool KeyHeld(KeyCode key)
    {
#if DEBUG
        if (TestKeyHeld.HasValue)
        {
            return TestKeyHeld.Value;
        }
#endif
        return ZInput.GetKey(key, logWarning: false);
    }

    // Turned on mid-game: list covered pieces now (world already loaded, ZNetScene.Awake long gone).
    protected override void OnActivated()
    {
        CoverageDump.RunIfWorldLoaded();
        SelfTests.Register();
    }

    // Turned off: forget everything me keep. Loop state and effect swap never live outside one press anyway.
    protected override void OnDeactivated()
    {
        SelfTests.Unregister();
        BatchFeeder.Reset();
        PendingAdds.Clear();
        HoverHint.Clear();
    }
}
