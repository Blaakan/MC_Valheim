using UnityEngine;

namespace MC.UX.CheatedTagRemoverMod;

// Me = the pass over what this game hold in memory. Saved data alone is not enough on a game that has objects:
//   - items the local player carry live only in this game and in the character file: nobody else can reach them;
//   - a loaded chest keep its items in memory and write them back when its owner move one (Container.Save);
//   - a loaded ground item keep its item in memory too (ItemDrop.Save on stack change, auto-stack...).
// So me clear the bit on those copies first, then WorldSweep fix the saved bytes. Me never call the game's own save
// here: it would write the whole in-memory inventory back, and an item with unknown prefab (mod gone) is not in that
// memory, only in the bytes. Bytes get fixed in place by WorldSweep, memory copies just stop being able to bring the
// tag back. Dedicated server has no player and no object: both methods find nothing there.
internal static class LiveSweep
{
    // Items the local player carry. write false = count only.
    internal static void Carried(bool write, SweepCounts c)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }
        var inventory = player.GetInventory();
        if (inventory == null)
        {
            return;
        }
        var found = 0;
        foreach (var item in inventory.GetAllItems())
        {
            if (item == null || !item.m_cheated)
            {
                continue;
            }
            found++;
            if (write)
            {
                item.m_cheated = false;
            }
        }
        c.CarriedItems += found;
        if (write && found > 0)
        {
            // Vanilla keep "am I cheated" for the frame, and say "no cheated item left" from Inventory.Changed.
            Achievements.m_cheatCheckFrame = -1;
            inventory.Changed();
        }
    }

    // In-memory items of loaded chests and ground items. No count: the saved bytes are what WorldSweep count.
    internal static void Loaded()
    {
        foreach (var container in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
        {
            if (container == null)
            {
                continue;
            }
            var nview = container.m_nview;
            var inventory = container.GetInventory();
            if (nview == null || !nview.IsValid() || inventory == null)
            {
                continue;
            }
            foreach (var item in inventory.GetAllItems())
            {
                if (item != null)
                {
                    item.m_cheated = false;
                }
            }
        }
        foreach (var drop in ItemDrop.s_instances)
        {
            if (drop == null || drop.m_itemData == null)
            {
                continue;
            }
            var nview = drop.m_nview;
            if (nview == null || !nview.IsValid())
            {
                continue;
            }
            drop.m_itemData.m_cheated = false;
        }
    }
}
