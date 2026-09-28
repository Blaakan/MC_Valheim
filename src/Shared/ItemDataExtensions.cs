namespace MC.Shared;

// Me put small flags on item. ItemData.m_customData is Dictionary<string,string>.
// Game save it with inventory, with dropped item ZDO, and Clone() copy it. Vanilla ignore keys it no know.
// Key must be namespaced ("<ModGuid>.<Name>") so mods no stomp each other.
internal static class ItemDataExtensions
{
    private const string True = "1";

    public static bool GetCustomBool(this ItemDrop.ItemData item, string key)
    {
        return item?.m_customData != null
            && item.m_customData.TryGetValue(key, out var value)
            && value == True;
    }

    // Me store true as "1". False = key gone, so no junk left in saves.
    public static void SetCustomBool(this ItemDrop.ItemData item, string key, bool value)
    {
        if (item == null)
        {
            return;
        }

        if (value)
        {
            item.m_customData ??= new System.Collections.Generic.Dictionary<string, string>();
            item.m_customData[key] = True;
        }
        else
        {
            item.m_customData?.Remove(key);
        }
    }
}
