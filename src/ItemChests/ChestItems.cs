using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ItemChests
{
    // The 50 chest items of module\ModuleData\item_chests.xml. A chest slot owns exactly one of them, so the
    // item's weight and name can be set to that chest's contents and name without touching any other item.
    internal static class ChestItems
    {
        public const int MaxChests = 50;
        private const string IdPrefix = "item_chest_";

        private static readonly MethodInfo? NameSetter = AccessTools.PropertySetter(typeof(ItemObject), nameof(ItemObject.Name));
        private static readonly MethodInfo? WeightSetter = AccessTools.PropertySetter(typeof(ItemObject), nameof(ItemObject.Weight));

        public static string Id(int slot) => IdPrefix + slot.ToString("00");

        public static ItemObject? Get(int slot) => MBObjectManager.Instance?.GetObject<ItemObject>(Id(slot));

        // 0 when the item is not a chest.
        public static int SlotOf(ItemObject? item)
        {
            string? id = item?.StringId;
            if (id == null || !id.StartsWith(IdPrefix, StringComparison.Ordinal))
                return 0;
            return int.TryParse(id.Substring(IdPrefix.Length), out int slot) && slot >= 1 && slot <= MaxChests ? slot : 0;
        }

        // The chest as it sits in the party roster: a quest item, which the game never lets be sold,
        // discarded, given, looted or moved into another chest.
        public static EquipmentElement Element(ItemObject item) => new EquipmentElement(item, null, null, isQuestItem: true);

        public static void SetName(ItemObject item, string name)
        {
            try { NameSetter?.Invoke(item, new object[] { new TextObject("{=!}" + name) }); }
            catch (Exception e) { SubModule.LogOnce("setting a chest name", e); }
        }

        public static void SetWeight(ItemObject item, float weight)
        {
            try { WeightSetter?.Invoke(item, new object[] { weight }); }
            catch (Exception e) { SubModule.LogOnce("setting a chest weight", e); }
        }

        public static float WeightOf(IEnumerable<ItemRosterElement> contents) =>
            contents.Sum(e => e.EquipmentElement.GetEquipmentElementWeight() * e.Amount);

        public static int WorthOf(IEnumerable<ItemRosterElement> contents) =>
            contents.Sum(e => e.EquipmentElement.GetBaseValue() * e.Amount);

        public static int CountOf(IEnumerable<ItemRosterElement> contents) => contents.Sum(e => e.Amount);

        // Horses, mules and livestock add carrying capacity and herd penalties to a party; boxed up they would not.
        public static bool FitsInChest(ItemObject? item) => item != null && !item.HasHorseComponent && SlotOf(item) == 0;
    }
}
