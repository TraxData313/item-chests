using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace ItemChests
{
    // Owns the chests of the player's party: a name and an ItemRoster of contents per slot. The contents are NOT
    // in the party's roster, so nothing in the game (or another mod) can sell, eat, equip, count or upgrade troops
    // with them; the chest item in the party roster carries their weight instead.
    //
    // Saved as vanilla types only (List<string> and ItemRoster under this behavior's own keys), so a save still
    // loads without the mod: the chest items then turn into "Trash Item" and the contents are lost.
    internal sealed class ItemChestsCampaignBehavior : CampaignBehaviorBase
    {
        private const string NamesKey = "ItemChests_names";
        private const string ContentsKeyPrefix = "ItemChests_contents_";

        public static ItemChestsCampaignBehavior? Instance { get; private set; }

        // Index = slot - 1; an empty string is a free slot.
        private List<string> _names = NewNames();
        private readonly Dictionary<int, ItemRoster> _contents = new Dictionary<int, ItemRoster>();

        public ItemChestsCampaignBehavior()
        {
            Instance = this;
        }

        public IEnumerable<int> ActiveSlots => Enumerable.Range(1, ChestItems.MaxChests).Where(IsActive);

        public bool IsActive(int slot) => slot >= 1 && slot <= ChestItems.MaxChests && !string.IsNullOrEmpty(_names[slot - 1]);

        public string NameOf(int slot) => IsActive(slot) ? _names[slot - 1] : "";

        public ItemRoster? ContentsOf(int slot) => IsActive(slot) && _contents.TryGetValue(slot, out var roster) ? roster : null;

        public int FreeSlot => Enumerable.Range(1, ChestItems.MaxChests).FirstOrDefault(s => !IsActive(s) && ChestItems.Get(s) != null);

        public int Count => ActiveSlots.Count();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => OnSessionLaunched());
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, Sanitize);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData(NamesKey, ref _names);
            if (dataStore.IsLoading)
            {
                _names ??= NewNames();
                while (_names.Count < ChestItems.MaxChests)
                    _names.Add("");
                for (int i = 0; i < _names.Count; i++)
                    _names[i] ??= "";
                _contents.Clear();
            }
            foreach (int slot in ActiveSlots.ToList())
            {
                ItemRoster? roster = null;
                if (!dataStore.IsLoading && !_contents.TryGetValue(slot, out roster))
                    roster = _contents[slot] = new ItemRoster();
                dataStore.SyncData(ContentsKeyPrefix + slot, ref roster);
                if (dataStore.IsLoading)
                    _contents[slot] = roster ?? new ItemRoster();
            }
        }

        private void OnSessionLaunched()
        {
            for (int slot = 1; slot <= ChestItems.MaxChests; slot++)
                ApplyToItem(slot);
            Sanitize();
        }

        // Creates an empty chest and puts it in the party. 0 when every slot is taken.
        public int Create(string name)
        {
            int slot = FreeSlot;
            if (slot == 0)
                return 0;
            _names[slot - 1] = CleanName(name, slot);
            _contents[slot] = new ItemRoster();
            ApplyToItem(slot);
            Sanitize();
            return slot;
        }

        public void Rename(int slot, string name)
        {
            if (!IsActive(slot))
                return;
            _names[slot - 1] = CleanName(name, slot);
            ApplyToItem(slot);
        }

        // Empties the chest into the party and takes the chest away.
        public void Remove(int slot)
        {
            if (!IsActive(slot))
                return;
            var party = MobileParty.MainParty?.ItemRoster;
            var contents = ContentsOf(slot);
            if (party != null && contents != null)
            {
                foreach (var element in contents.ToList())
                    party.AddToCounts(element.EquipmentElement, element.Amount);
            }
            _names[slot - 1] = "";
            _contents.Remove(slot);
            ApplyToItem(slot);
            Sanitize();
        }

        // The chest item weighs what is inside it. Called after every change to the contents.
        public void RefreshWeight(int slot)
        {
            var item = ChestItems.Get(slot);
            if (item == null)
                return;
            var contents = ContentsOf(slot);
            ChestItems.SetWeight(item, contents == null ? 0f : ChestItems.WeightOf(contents));
            // The party's carried weight is cached against its roster version.
            MobileParty.MainParty?.ItemRoster?.UpdateVersion();
        }

        private void ApplyToItem(int slot)
        {
            var item = ChestItems.Get(slot);
            if (item == null)
                return;
            ChestItems.SetName(item, IsActive(slot) ? _names[slot - 1] : "Chest");
            RefreshWeight(slot);
        }

        // Keeps the one rule everything else relies on: every chest in use is in the main party's roster exactly
        // once, as a quest item; chest items of unused slots are not there at all (cheat-mode inventories hand out
        // ten of every item, for one). Never runs while an inventory screen is open on the party roster.
        public void Sanitize()
        {
            var roster = MobileParty.MainParty?.ItemRoster;
            if (roster == null)
                return;
            var present = new HashSet<int>();
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                var element = roster.GetElementCopyAtIndex(i);
                int slot = ChestItems.SlotOf(element.EquipmentElement.Item);
                if (slot == 0)
                    continue;
                bool keep = IsActive(slot) && !present.Contains(slot) && element.Amount == 1
                    && element.EquipmentElement.IsQuestItem && element.EquipmentElement.ItemModifier == null;
                if (keep)
                {
                    present.Add(slot);
                    continue;
                }
                // Removing swaps the last element into i; that one was already looked at.
                roster.AddToCounts(element.EquipmentElement, -element.Amount);
            }
            foreach (int slot in ActiveSlots)
            {
                if (present.Contains(slot))
                    continue;
                var item = ChestItems.Get(slot);
                if (item != null)
                    roster.Add(new ItemRosterElement(ChestItems.Element(item), 1));
            }
        }

        private string CleanName(string name, int slot)
        {
            name = (name ?? "").Trim();
            if (name.Length > 40)
                name = name.Substring(0, 40);
            return name.Length > 0 ? name : "Chest " + slot;
        }

        public string SuggestName()
        {
            for (int n = Count + 1; ; n++)
            {
                string name = "Chest " + n;
                if (!_names.Contains(name))
                    return name;
            }
        }

        private static List<string> NewNames() => Enumerable.Repeat("", ChestItems.MaxChests).ToList();
    }
}
