using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ItemChests
{
    // The inventory screens chests live in.
    //
    // A chest opens in the game's own storage ("Stash") screen: chest on the left, party on the right, no
    // trading. Two inventory screens can't be stacked (their view models share static callbacks), so opening a
    // chest first finishes the current screen exactly like Done, then opens the chest screen on the next tick.
    // Done or Cancel there comes back to the party inventory.
    //
    // Chests open only from the player's own inventory (the I key / map bar, also in town scenes) and from a
    // chest screen. Trade, loot and other screens show chests locked.
    internal static class ChestScreens
    {
        public enum Kind { Own, Chest }

        public sealed class Screen
        {
            public Screen(InventoryLogic logic, Kind kind, int slot, Action? extras)
            {
                Logic = logic;
                Kind = kind;
                Slot = slot;
                Extras = extras;
            }

            public InventoryLogic Logic { get; }
            public Kind Kind { get; }
            public int Slot { get; }
            // The delegate the own inventory was opened with (in a scene it re-dresses the player's agent).
            // Every screen of the chain runs it on close.
            public Action? Extras { get; }
            public bool WarnedAboutAnimals { get; set; }
        }

        private static readonly FieldInfo? LogicField = AccessTools.Field(typeof(SPInventoryVM), "_inventoryLogic");
        private static readonly FieldInfo? CharacterField = AccessTools.Field(typeof(SPInventoryVM), "_currentCharacter");
        private static readonly MethodInfo? SaveLocks = AccessTools.Method(typeof(SPInventoryVM), "SaveItemLockStates");
        private static readonly MethodInfo? SaveSorts = AccessTools.Method(typeof(SPInventoryVM), "SaveItemSortStates");
        private static readonly MethodInfo? LeftNameSetter = AccessTools.PropertySetter(typeof(InventoryLogic), nameof(InventoryLogic.LeftRosterName));
        private static readonly MethodInfo? MarketDataGetter = AccessTools.Method(typeof(InventoryScreenHelper), "GetCurrentMarketDataForPlayer");

        // The one inventory screen open now, when it is ours or the own inventory.
        private static Screen? _current;
        private static bool _openingOwn;
        private static Action? _openingOwnExtras;

        // Runs once the closing screen is gone.
        private static Action? _pending;
        private static int _pendingTicks;

        public static Screen? Find(InventoryLogic? logic) => logic != null && _current != null && _current.Logic == logic ? _current : null;

        public static InventoryLogic? LogicOf(SPInventoryVM vm) => LogicField?.GetValue(vm) as InventoryLogic;

        // ---- hooks from the patches -------------------------------------------------------------------------

        public static void BeforeOpenOwnInventory(Action? extras)
        {
            // Nothing is open on the party roster yet, so this is the moment to fix it up.
            ChestBehavior.Instance?.Sanitize();
            _openingOwn = true;
            _openingOwnExtras = extras;
        }

        public static void AfterOpenOwnInventory()
        {
            _openingOwn = false;
            _openingOwnExtras = null;
        }

        public static void OnLogicInitialized(InventoryLogic logic)
        {
            if (!_openingOwn)
                return;
            _openingOwn = false;
            _current = new Screen(logic, Kind.Own, 0, _openingOwnExtras);
        }

        public static void Tick()
        {
            if (_pending == null)
                return;
            var states = Game.Current?.GameStateManager;
            if (states == null || Campaign.Current == null)
            {
                _pending = null;
                return;
            }
            if (states.ActiveState is InventoryState)
            {
                // The screen never closed after all (or something else opened one): drop it rather than
                // opening a chest out of the blue later.
                if (++_pendingTicks > 30)
                    _pending = null;
                return;
            }
            var next = _pending;
            _pending = null;
            try
            {
                next();
            }
            catch (Exception e)
            {
                SubModule.Warn("opening the chest failed: " + e);
            }
        }

        // ---- user actions -------------------------------------------------------------------------------------

        public static void OnChestClicked(SPInventoryVM vm, int slot)
        {
            var behavior = ChestBehavior.Instance;
            var screen = Find(LogicOf(vm));
            if (behavior == null || !behavior.IsActive(slot))
                return;
            if (screen == null)
            {
                Hint("Chests open from your own inventory (the I key), not from this screen.");
                return;
            }
            if (screen.Kind == Kind.Chest && screen.Slot == slot)
            {
                Hint("This chest is open on the left.");
                return;
            }
            var character = CurrentCharacter(vm);
            Leave(vm, screen, () => OpenChest(slot, screen.Extras, character));
        }

        public static void NewChest(SPInventoryVM vm)
        {
            var behavior = ChestBehavior.Instance;
            var screen = Find(LogicOf(vm));
            if (behavior == null || screen == null)
                return;
            if (behavior.FreeSlot == 0)
            {
                Hint("You already carry " + ChestItems.MaxChests + " chests, the most there can be.");
                return;
            }
            var character = CurrentCharacter(vm);
            AskName("New chest", "Name the chest. It opens on the left; put in what you want kept apart.", behavior.SuggestName(), "Create",
                name => Leave(vm, screen, () =>
                {
                    int slot = behavior.Create(name);
                    if (slot != 0)
                        OpenChest(slot, screen.Extras, character);
                }));
        }

        public static void RenameChest(SPInventoryVM vm)
        {
            var behavior = ChestBehavior.Instance;
            var screen = Find(LogicOf(vm));
            if (behavior == null || screen == null || screen.Kind != Kind.Chest)
                return;
            int slot = screen.Slot;
            AskName("Rename chest", "", behavior.NameOf(slot), "Rename", name =>
            {
                if (!behavior.IsActive(slot) || Find(LogicOf(vm)) != screen)
                    return;
                behavior.Rename(slot, name);
                string shown = behavior.NameOf(slot);
                try { LeftNameSetter?.Invoke(screen.Logic, new object[] { new TextObject("{=!}" + shown) }); }
                catch (Exception e) { SubModule.LogOnce("renaming the open chest", e); }
                vm.LeftInventoryOwnerName = shown;
                foreach (var row in vm.RightItemListVM.Where(r => ChestItems.SlotOf(r?.ItemRosterElement.EquipmentElement.Item) == slot))
                    row.ItemDescription = shown;
            });
        }

        public static void RemoveChest(SPInventoryVM vm)
        {
            var behavior = ChestBehavior.Instance;
            var screen = Find(LogicOf(vm));
            if (behavior == null || screen == null || screen.Kind != Kind.Chest)
                return;
            int slot = screen.Slot;
            string name = behavior.NameOf(slot);
            int count = behavior.ContentsOf(slot) is { } contents ? ChestItems.CountOf(contents) : 0;
            string text = count == 0
                ? "Remove the empty chest \"" + name + "\"?"
                : "Remove the chest \"" + name + "\"? The " + count + (count == 1 ? " item" : " items")
                  + " in it go back into your inventory, where they can be sold, used and eaten again.";
            InformationManager.ShowInquiry(new InquiryData("Remove chest", text, true, true, "Remove", GameTexts.FindText("str_cancel").ToString(),
                () => Leave(vm, screen, () =>
                {
                    behavior.Remove(slot);
                    ReopenOwnInventory(screen.Extras);
                }), null));
        }

        // Animals stay out of chests. Returns false for a transfer that must not happen.
        public static bool AllowTransfer(InventoryLogic logic, TransferCommand command)
        {
            var screen = Find(logic);
            if (screen == null || screen.Kind != Kind.Chest || command.ToSide != InventoryLogic.InventorySide.OtherInventory)
                return true;
            if (ChestItems.FitsInChest(command.ElementToTransfer.EquipmentElement.Item))
                return true;
            if (!screen.WarnedAboutAnimals)
            {
                screen.WarnedAboutAnimals = true;
                Hint("Horses and other animals don't go in chests.");
            }
            return false;
        }

        // ---- screens ------------------------------------------------------------------------------------------

        // Finishes the current screen as Done does (asking first when the Discard side holds items), then runs next.
        private static void Leave(SPInventoryVM vm, Screen screen, Action next)
        {
            vm.ExecuteRemoveZeroCounts();
            int discards = screen.Kind == Kind.Own && !Game.Current.CheatMode
                ? screen.Logic.GetElementCountOnSide(InventoryLogic.InventorySide.OtherInventory)
                : 0;
            if (discards > 0)
            {
                InformationManager.ShowInquiry(new InquiryData("", GameTexts.FindText("str_discarding_items").ToString(), true, true,
                    GameTexts.FindText("str_yes").ToString(), GameTexts.FindText("str_no").ToString(),
                    () => Close(vm, screen, next), null));
            }
            else
            {
                Close(vm, screen, next);
            }
        }

        private static void Close(SPInventoryVM vm, Screen screen, Action next)
        {
            if (!(Game.Current?.GameStateManager?.ActiveState is InventoryState state) || state.InventoryLogic != screen.Logic)
                return;
            var original = state.DoneLogicExtrasDelegate;
            state.DoneLogicExtrasDelegate = () =>
            {
                original?.Invoke();
                _pending = next;
                _pendingTicks = 0;
            };
            try
            {
                SaveLocks?.Invoke(vm, null);
                SaveSorts?.Invoke(vm, null);
            }
            catch (Exception e)
            {
                SubModule.LogOnce("saving inventory locks", e);
            }
            InventoryScreenHelper.CloseScreen(fromCancel: false);
            if (Game.Current.GameStateManager.ActiveState == state)
            {
                // Done refused (an item preview is open, for one): leave the screen as it was.
                state.DoneLogicExtrasDelegate = original;
                _pending = null;
            }
        }

        private static void OpenChest(int slot, Action? extras, CharacterObject character)
        {
            var behavior = ChestBehavior.Instance;
            if (behavior == null || !behavior.IsActive(slot) || MobileParty.MainParty == null)
                return;
            behavior.Sanitize();
            var contents = behavior.ContentsOf(slot);
            if (contents == null)
                return;

            var states = Game.Current.GameStateManager;
            var state = states.CreateState<InventoryState>();
            state.InventoryMode = InventoryScreenHelper.InventoryMode.Stash;
            var logic = new InventoryLogic(null);
            logic.Initialize(contents, MobileParty.MainParty, isTrading: false, isSpecialActionsPermitted: false, character,
                InventoryScreenHelper.InventoryCategoryType.None, MarketData(), useBasePrices: false,
                InventoryScreenHelper.InventoryMode.Stash, new TextObject("{=!}" + behavior.NameOf(slot)));

            var screen = new Screen(logic, Kind.Chest, slot, extras);
            // Subscribed before the screen's view model, so the chest weighs its new contents by the time the
            // screen reads the party's weight.
            logic.AfterTransfer += (_, __) => behavior.RefreshWeight(slot);
            logic.AfterReset += (_, __) => behavior.RefreshWeight(slot);
            state.InventoryLogic = logic;
            state.DoneLogicExtrasDelegate = () =>
            {
                behavior.RefreshWeight(slot);
                extras?.Invoke();
                _pending = () => ReopenOwnInventory(extras);
                _pendingTicks = 0;
            };
            _current = screen;
            states.PushState(state);
        }

        private static void ReopenOwnInventory(Action? extras)
        {
            if (Campaign.Current == null || MobileParty.MainParty == null)
                return;
            InventoryScreenHelper.OpenScreenAsInventory(extras);
        }

        // ---- helpers ------------------------------------------------------------------------------------------

        // The hero the player was looking at, if they can still be dressed from the chest screen.
        private static CharacterObject CurrentCharacter(SPInventoryVM vm)
        {
            try
            {
                if (CharacterField?.GetValue(vm) is CharacterObject character && character.IsHero
                    && MobileParty.MainParty.MemberRoster.FindIndexOfTroop(character) >= 0)
                    return character;
            }
            catch (Exception e)
            {
                SubModule.LogOnce("reading the selected hero", e);
            }
            return CharacterObject.PlayerCharacter;
        }

        private static IMarketData MarketData()
        {
            try
            {
                if (MarketDataGetter?.Invoke(null, null) is IMarketData data)
                    return data;
            }
            catch (Exception e)
            {
                SubModule.LogOnce("reading market prices", e);
            }
            var settlement = MobileParty.MainParty.CurrentSettlement;
            if (settlement?.Town != null)
                return settlement.Town.MarketData;
            if (settlement?.Village != null)
                return settlement.Village.MarketData;
            return (IMarketData)AccessTools.CreateInstance(AccessTools.TypeByName("TaleWorlds.CampaignSystem.Settlements.FakeMarketData"));
        }

        private static void AskName(string title, string text, string current, string affirmative, Action<string> done)
        {
            InformationManager.ShowTextInquiry(new TextInquiryData(title, text, true, true, affirmative, GameTexts.FindText("str_cancel").ToString(),
                done, null, false,
                input => string.IsNullOrWhiteSpace(input)
                    ? new Tuple<bool, string>(false, "The chest needs a name.")
                    : input.Trim().Length > 40
                        ? new Tuple<bool, string>(false, "40 letters at most.")
                        : new Tuple<bool, string>(true, ""),
                "", current));
        }

        private static void Hint(string text) => MBInformationManager.AddQuickInformation(new TextObject("{=!}" + text));
    }
}
