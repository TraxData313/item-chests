# Item Chests — research and design (2026-10-17)

Game: Bannerlord v1.4.8 + War Sails. Decompiled with ilspycmd into a scratch folder (not in the repo):
TaleWorlds.CampaignSystem(.ViewModelCollection), TaleWorlds.Core, SandBox(.View/.GauntletUI), UIExtenderEx 3.0.3.
Product decisions by Toni (2026-10-17): name **Item Chests**; chests open from your own inventory only;
no animals in chests; UIExtenderEx is an accepted dependency.

## What the game gives us

- `InventoryLogic` holds two rosters, `_rosters[0]` = left ("other"), `_rosters[1]` = right (party).
  Transfers to the left are "sells" (`IsSell`), but gold only moves when `IsTrading`. Reset/cancel copies
  `_rostersBackup` back **into the same roster objects** (`Clear()` + `Add(backup)`).
- `InventoryScreenHelper.OpenScreenAsStash(roster)` is a vanilla "storage" screen: mode `Stash`, left = any
  `ItemRoster`, no trading, no discard XP (`CanGainXpFromDiscarding` only in Loot/Default-with-no-party),
  hints say "Give". This is exactly a chest. We build the same state ourselves (to set the left title, the
  selected hero and `DoneLogicExtrasDelegate`).
- The plain inventory (I key, map bar, in missions) is `OpenScreenAsInventory(Action)` → mode `Default`,
  `OtherParty == null`, left = a fresh "Discard" roster, `isSpecialActionsPermitted = true`.
  In missions the delegate refreshes the agent's equipment, so it must be carried along.
- `SPInventoryVM` wires **static** callbacks (`ItemVM.ProcessItemSelect` …) and nulls them in `OnFinalize`.
  Two inventory screens stacked would break the lower one → never nest; close then open.
- `InventoryScreenHelper.CloseScreen(false)` = Done: `DoneLogic()` → `DoneLogicExtrasDelegate` → `PopState`.
  Cancel = `CloseScreen(true)` = `Reset` then the same path. `PushState`/`PopState` run synchronously.
  `HandleDone` (private) already asks "discard items?" in Default mode; `SaveItemLockStates/SortStates` are private.
- Clicking a row: `ItemVM.ExecuteSelectItem` → static `ProcessItemSelect` → `SPInventoryVM.ProcessItemSelect(ItemVM)`.
- Hover tooltip: `ItemMenuVM.SetItem(...)` fills public `TargetItemProperties`.

## Items, quest flag, weight

- `EquipmentElement.IsQuestItem` (ctor arg) is kept in the roster element. With it:
  `InventoryLogic.TransferIsMovementValid` refuses every move, `SPItemVM.IsTransferable` is false (no sell
  button, excluded from "transfer all"), `MapEvent.LootDefeatedPartyItems` never loots it (also skips
  `NotMerchandise`), hideout/quest code skips it. Smart Steward also skips quest/non-transferable items.
- `IsEqualTo` ignores the quest flag, so `FindIndexOfElement` still finds it.
- Template in vanilla: `stolen_goods` — `Type="Goods"`, `is_merchandise="false"`, `mesh="crate_a"`, no
  category (filled by `DetermineItemCategoryForItem`). Our chest items copy it. Goods = misc filter, not mounts.
- Party weight: `DefaultInventoryCapacityModel.CalculateTotalWeightCarried` sums
  `GetItemEffectiveWeight` → `EquipmentElement.GetEquipmentElementWeight()` → `ItemObject.Weight`
  (horses weigh 0 and add capacity). War Sails does not override the model. Cached in
  `MobileParty.TotalWeightCarried`, invalidated by `ItemRoster.VersionNo` → call `UpdateVersion()`.
- `ItemObject.Weight` / `Name` have private setters. Each chest is its **own** ItemObject, always amount 1,
  so its `Weight` can simply be set to the contents' weight (reflection), and its `Name` to the chest name.
  `Value` stays constant (1): `ItemRoster.TotalValue` is cached with the value at add time.

## Saves and removal

- Behavior data is `Dictionary<string, object>` in the save; vanilla types load even without the mod.
  Contents are stored as plain `ItemRoster`s (vanilla saveable) under `ItemChests_contents_<slot>`, names as
  `List<string>`. No custom saveable types.
- A save whose party holds items of a removed mod: `ItemRoster.ReplaceInvalidItemsWithTrash` turns them into
  "Trash Item". So removing the mod loads fine; the chests become trash and **their contents are lost** →
  README: empty chests before removing. ItemRosters inside our data also get the trash treatment for items of
  other removed mods.
- Crafted weapons are kept alive by `CraftingCampaignBehavior._craftedItemDictionary`, not by rosters, so a
  crafted sword can live in a chest.
- `MobileParty.RemoveParty` / game-over clear the party roster; normal defeat does not take quest items.

## Design

- 50 fixed ItemObjects `item_chest_01..50` in `module/ModuleData/item_chests.xml` (Goods, not merchandise,
  crate mesh, weight 0, value 1). A slot = one chest. `ChestBehavior` keeps name + contents per slot.
- Contents live **outside** the party roster: nothing in the game or other mods can sell, eat, equip, count
  or upgrade with them. The chest item carries their weight.
- Invariant (sanitised on load, before the own inventory opens, before a chest opens, daily): every active
  chest is in the main party roster exactly once, quest-flagged; inactive chest items are removed.
- Opening: click a chest row (`ProcessItemSelect` prefix) in the own inventory or a chest screen → the current
  screen is finished like Done (vanilla discard confirmation if the Discard side has items) → next tick a
  Stash-mode screen opens with the chest on the left → its Done/Cancel returns to the own inventory.
  Elsewhere (trade, loot …) a click only shows a hint.
- Live weight: chest screens update the chest's weight after every transfer/reset, so capacity checks and
  the weight readout stay true while it is open.
- Animals are filtered out of transfer commands into a chest (`AddTransferCommand(s)` prefix).
- Buttons (UIExtenderEx prefab insert in the left header + mixin on `SPInventoryVM`): New chest (own inventory
  and chest screens), Rename and Remove (chest screens). Remove returns the contents to the party.
- Tooltip (`ItemMenuVM.SetItem` postfix): item count, weight, worth, and the first lines of contents.
