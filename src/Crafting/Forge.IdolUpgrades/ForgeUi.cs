using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me = small UI helpers shared by the Idols tab and the refinement panel: fill a requirement slot like vanilla
// SetupRequirement (icon, name, amount, red flash when missing), start the craft timer like vanilla OnCraftPressed,
// and the click on the idol slot (pick which idol level a refinement spend).
internal static class ForgeUi
{
    internal static void ShowSlot(Transform slot, Sprite icon, string name, int amount, bool enough, string tooltip)
    {
        var image = slot.Find("res_icon")?.GetComponent<Image>();
        var nameText = slot.Find("res_name")?.GetComponent<TMP_Text>();
        var amountText = slot.Find("res_amount")?.GetComponent<TMP_Text>();
        if (image != null)
        {
            image.gameObject.SetActive(true);
            image.sprite = icon;
            image.color = Color.white;
        }
        if (nameText != null)
        {
            nameText.gameObject.SetActive(true);
            nameText.text = Localization.instance.Localize(name);
        }
        if (amountText != null)
        {
            amountText.gameObject.SetActive(true);
            amountText.text = amount.ToString();
            amountText.color = enough ? Color.white : (Mathf.Sin(Time.time * 10f) > 0f ? Color.red : Color.white);
        }
        var tip = slot.GetComponent<UITooltip>();
        if (tip != null)
        {
            tip.m_text = tooltip ?? "";
        }
    }

    // Vanilla OnCraftPressed after its checks, minus the Forge free-slot rule (vanilla keep room for the refund of a
    // broken item; nothing break any more). Station sound, not the generic one.
    internal static void StartTimer(InventoryGui gui, Player player)
    {
        gui.SetActiveGroup(gui.m_uiGroups[3]);
        gui.m_craftRecipe = gui.m_selectedRecipe.Recipe;
        gui.m_craftUpgradeItem = gui.m_selectedRecipe.ItemData;
        gui.m_craftVariant = gui.m_selectedVariant;
        gui.m_multiCrafting = false;
        gui.m_touchMultiCrafting = false;
        gui.m_craftTimer = 0f;
        Object.Instantiate(gui.CraftingVibration);
        var station = player.GetCurrentCraftingStation();
        if (station != null)
        {
            station.m_craftItemEffects.Create(player.transform.position, Quaternion.identity);
        }
        else
        {
            gui.m_craftItemEffects.Create(player.transform.position, Quaternion.identity);
        }
    }

    // Clicks on requirement slots (mouse). Me add one to each of the 4 vanilla slots when first needed.
    internal static void EnsureClickable(InventoryGui gui)
    {
        var slots = gui.m_recipeRequirementList;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].GetComponent<SlotClick>() == null)
            {
                slots[i].AddComponent<SlotClick>().Index = i;
            }
        }
    }

    internal static void RemoveClickable(InventoryGui gui)
    {
        if (gui == null)
        {
            return;
        }
        foreach (var slot in gui.m_recipeRequirementList)
        {
            if (slot != null)
            {
                var click = slot.GetComponent<SlotClick>();
                if (click != null)
                {
                    Object.Destroy(click);
                }
            }
        }
    }

    internal sealed class SlotClick : MonoBehaviour, IPointerClickHandler
    {
        internal int Index;

        public void OnPointerClick(PointerEventData eventData)
        {
            try
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    ForgePanel.OnSlotClicked(Index);
                }
            }
            catch (System.Exception e)
            {
                MC.Shared.PatchGuard.Report("Idol slot click", e);
            }
        }
    }
}
