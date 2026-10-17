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
            harmony.Patch(AccessTools.Method(typeof(InventoryScreenHelper), nameof(InventoryScreenHelper.OpenScreenAsInventory)),
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

            // No animals into a chest.
            harmony.Patch(AccessTools.Method(typeof(InventoryLogic), nameof(InventoryLogic.AddTransferCommand)),
                prefix: new HarmonyMethod(typeof(Patches), nameof(AddTransferCommandPrefix)));
            harmony.Patch(AccessTools.Method(typeof(InventoryLogic), nameof(InventoryLogic.AddTransferCommands)),
                prefix: new HarmonyMethod(typeof(Patches), nameof(AddTransferCommandsPrefix)));
        }

        // Only costs the contents list in the tooltip; patched apart so a failure leaves the rest working.
        public static void ApplyTooltip(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ItemMenuVM), nameof(ItemMenuVM.SetItem)),
                postfix: new HarmonyMethod(typeof(Patches), nameof(SetItemPostfix)));
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
                    if (slot != 0 && ChestBehavior.Instance?.IsActive(slot) == true)
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

        private static bool AddTransferCommandPrefix(InventoryLogic __instance, TransferCommand command)
        {
            try { return ChestScreens.AllowTransfer(__instance, command); }
            catch (Exception e) { SubModule.LogOnce("checking a transfer", e); return true; }
        }

        private static void AddTransferCommandsPrefix(InventoryLogic __instance, ref IEnumerable<TransferCommand> commands)
        {
            try
            {
                if (ChestScreens.Find(__instance)?.Kind != ChestScreens.Kind.Chest || commands == null)
                    return;
                var logic = __instance;
                commands = commands.Where(c => ChestScreens.AllowTransfer(logic, c)).ToList();
            }
            catch (Exception e)
            {
                SubModule.LogOnce("checking transfers", e);
            }
        }

        // The tooltip of a chest: what is in it instead of trade rumours.
        private static void SetItemPostfix(ItemMenuVM __instance, SPItemVM item)
        {
            try
            {
                int slot = ChestItems.SlotOf(item?.ItemRosterElement.EquipmentElement.Item);
                var contents = slot == 0 ? null : ChestBehavior.Instance?.ContentsOf(slot);
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
