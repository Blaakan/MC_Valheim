namespace MC.Exploration.ViewSpyglassMod;

// Me = crosshair, hover text and piece health bar off while the spyglass view is on screen (Hud.UpdateCrosshair
// postfix, every frame); Image back on as soon as it is not (or when the feature turns off: Restore).
internal static class ScopeHud
{
    private static bool _hidden;

    internal static bool Hidden => _hidden;

    internal static void Update(Hud hud)
    {
        if (Scope.Active && ScopeOverlay.Shown)
        {
            if (hud.m_crosshair != null)
            {
                hud.m_crosshair.enabled = false;
            }
            if (hud.m_crosshairBow != null)
            {
                hud.m_crosshairBow.enabled = false;
            }
            if (hud.m_hoverName != null)
            {
                hud.m_hoverName.text = "";
            }
            if (hud.m_pieceHealthRoot != null)
            {
                hud.m_pieceHealthRoot.gameObject.SetActive(false);
            }
            _hidden = true;
            return;
        }
        if (_hidden)
        {
            Restore(hud);
        }
    }

    internal static void Restore(Hud hud = null)
    {
        if (!_hidden)
        {
            return;
        }
        _hidden = false;
        if (hud == null)
        {
            hud = Hud.instance;
        }
        if (hud == null)
        {
            return;
        }
        if (hud.m_crosshair != null)
        {
            hud.m_crosshair.enabled = true;
        }
        if (hud.m_crosshairBow != null)
        {
            hud.m_crosshairBow.enabled = true;
        }
    }
}
