using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ItemChests
{
    internal static class Patches
    {
        public static void Apply(Harmony harmony)
        {
            // Which inventory is the player's own (I key / map bar / town scene): mark its logic as it is built.
            // The private OpenInventoryPresentation, not the one-line public wrapper the JIT could inline.
            harmony.Patch(AccessTools.Method(typeof(InventoryScreenHelper), "OpenInventoryPresentation"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(OpenOwnPrefix)),
                finalizer: new HarmonyMethod(typeof(Patches), nameof(OpenOwnFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(InventoryLogic), nameof(InventoryLogic.Initialize), new[]
                {
                    typeof(ItemRoster), typeof(ItemRoster), typeof(TroopRoster), typeof(bool), typeof(bool), typeof(CharacterObject),
                    typeof(InventoryScreenHelper.InventoryCategoryType), typeof(IMarketData), typeof(bool),
                    typeof(InventoryScreenHelper.InventoryMode), typeof(TextObject), typeof(TroopRoster), typeof(InventoryLogic.CapacityData),
                }),
                postfix: new HarmonyMethod(typeof(Patches), nameof(InitializePostfix)));

            // A click on a chest opens it instead of selecting the row.
            harmony.Patch(AccessTools.Method(typeof(SPInventoryVM), "ProcessItemSelect"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(ItemSelectPrefix)));

            // No animals into a chest. TransferItem is where every move ends up (arrows, drag, unequip, transfer
            // all), and it is too big to be inlined, unlike the public AddTransferCommand.
            harmony.Patch(AccessTools.Method(typeof(InventoryLogic), "TransferItem"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(TransferItemPrefix)));

            // "Take all" from a chest: the game caps it by the party's free capacity, which already counts the
            // chest's weight - that is, the very items being taken out.
            harmony.Patch(AccessTools.Method(typeof(SPInventoryVM), "TransferAll"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(TransferAllPrefix)),
                finalizer: new HarmonyMethod(typeof(Patches), nameof(TransferAllFinalizer)));
        }

        // Only costs the contents list in the tooltip; patched apart so a failure leaves the rest working.
        public static void ApplyTooltip(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ItemMenuVM), nameof(ItemMenuVM.SetItem)),
                postfix: new HarmonyMethod(typeof(Patches), nameof(SetItemPostfix)));
        }

        // Chests above everything else in the item list, whatever the sort column and direction. Only costs the
        // order; patched apart so a failure leaves the rest working.
        public static void ApplySorting(Harmony harmony)
        {
            foreach (var comparer in new[]
                {
                    typeof(SPInventorySortControllerVM.ItemTypeComparer), typeof(SPInventorySortControllerVM.ItemNameComparer),
                    typeof(SPInventorySortControllerVM.ItemQuantityComparer), typeof(SPInventorySortControllerVM.ItemCostComparer),
                })
                harmony.Patch(AccessTools.Method(comparer, nameof(SPInventorySortControllerVM.ItemComparer.Compare)),
                    prefix: new HarmonyMethod(typeof(Patches), nameof(ComparePrefix)));
        }

        private static void OpenOwnPrefix(Action doneLogicExtrasDelegate)
        {
            try { ChestScreens.BeforeOpenOwnInventory(doneLogicExtrasDelegate); }
            catch (Exception e) { SubModule.LogOnce("OpenScreenAsInventory prefix", e); }
        }

        private static Exception? OpenOwnFinalizer(Exception? __exception)
        {
            ChestScreens.AfterOpenOwnInventory();
            return __exception;
        }

        private static void InitializePostfix(InventoryLogic __instance)
        {
            try { ChestScreens.OnLogicInitialized(__instance); }
            catch (Exception e) { SubModule.LogOnce("InventoryLogic.Initialize postfix", e); }
        }

        private static bool ItemSelectPrefix(SPInventoryVM __instance, ItemVM item)
        {
            try
            {
                if (item is SPItemVM row && row.InventorySide == InventoryLogic.InventorySide.PlayerInventory)
                {
                    int slot = ChestItems.SlotOf(row.ItemRosterElement.EquipmentElement.Item);
                    if (slot != 0 && ItemChestsCampaignBehavior.Instance?.IsActive(slot) == true)
                    {
                        ChestScreens.OnChestClicked(__instance, slot);
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                SubModule.LogOnce("opening a chest on click", e);
            }
            return true;
        }

        private static bool TransferItemPrefix(InventoryLogic __instance, ref TransferCommand transferCommand, ref List<TransferCommandResult> __result)
        {
            try
            {
                if (ChestScreens.AllowTransfer(__instance, transferCommand))
                    return true;
                __result = new List<TransferCommandResult>();
                return false;
            }
            catch (Exception e)
            {
                SubModule.LogOnce("checking a transfer", e);
                return true;
            }
        }

        private static void TransferAllPrefix(SPInventoryVM __instance, bool isBuy, out int __state)
        {
            __state = 0;
            try
            {
                var screen = ChestScreens.Find(ChestScreens.LogicOf(__instance));
                if (!isBuy || screen == null || screen.Kind != ChestScreens.Kind.Chest)
                    return;
                var item = ChestItems.Get(screen.Slot);
                if (item == null)
                    return;
                // Weightless while the budget is worked out; the first transfer gives it its true weight back.
                __state = screen.Slot;
                ChestItems.SetWeight(item, 0f);
                MobileParty.MainParty?.ItemRoster?.UpdateVersion();
            }
            catch (Exception e)
            {
                SubModule.LogOnce("take all from a chest", e);
            }
        }

        private static Exception? TransferAllFinalizer(Exception? __exception, int __state)
        {
            if (__state != 0)
                ItemChestsCampaignBehavior.Instance?.RefreshWeight(__state);
            return __exception;
        }

        // A chest before any other item; chests among themselves by name, then by slot.
        private static bool ComparePrefix(SPItemVM x, SPItemVM y, ref int __result)
        {
            try
            {
                int slotX = ChestItems.SlotOf(x?.ItemRosterElement.EquipmentElement.Item);
                int slotY = ChestItems.SlotOf(y?.ItemRosterElement.EquipmentElement.Item);
                if (slotX == 0 && slotY == 0)
                    return true;
                if (slotX == 0 || slotY == 0)
                    __result = slotX != 0 ? -1 : 1;
                else
                {
                    __result = string.Compare(x!.ItemDescription, y!.ItemDescription, StringComparison.CurrentCultureIgnoreCase);
                    if (__result == 0)
                        __result = slotX.CompareTo(slotY);
                }
                return false;
            }
            catch (Exception e)
            {
                SubModule.LogOnce("sorting chests first", e);
                return true;
            }
        }

        // The tooltip of a chest: what is in it instead of trade rumours.
        private static void SetItemPostfix(ItemMenuVM __instance, SPItemVM item)
        {
            try
            {
                int slot = ChestItems.SlotOf(item?.ItemRosterElement.EquipmentElement.Item);
                var contents = slot == 0 ? null : ItemChestsCampaignBehavior.Instance?.ContentsOf(slot);
                if (contents == null)
                    return;
                var lines = __instance.TargetItemProperties;
                lines.Clear();
                int count = ChestItems.CountOf(contents);
                Add(lines, "", count == 0 ? "An empty chest. Click it to open." : "A chest. Click it to open.");
                if (count > 0)
                {
                    Add(lines, "Items: ", count + (contents.Count > 1 ? " (" + contents.Count + " kinds)" : ""));
                    Add(lines, "Weight: ", ChestItems.WeightOf(contents).ToString("0.#"));
                    Add(lines, "Worth: ", ChestItems.WorthOf(contents).ToString("N0") + "<img src=\"General\\Icons\\Coin@2x\" extend=\"6\"/>");
                    const int shown = 12;
                    var sorted = contents.OrderBy(e => e.EquipmentElement.GetModifiedItemName().ToString()).ToList();
                    foreach (var element in sorted.Take(shown))
                        Add(lines, "", element.Amount + " × " + element.EquipmentElement.GetModifiedItemName());
                    if (sorted.Count > shown)
                        Add(lines, "", "… and " + (sorted.Count - shown) + " more");
                }
                Add(lines, "", "Out of reach of your party: never sold, eaten or equipped.");
            }
            catch (Exception e)
            {
                SubModule.LogOnce("chest tooltip", e);
            }
        }

        private static void Add(MBBindingList<ItemMenuTooltipPropertyVM> lines, string definition, string value) =>
            lines.Add(new ItemMenuTooltipPropertyVM(definition, value, 0));
    }
}
