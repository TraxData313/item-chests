# Item Chests — technical notes

For Bannerlord v1.4.8, with or without the War Sails DLC. Needs Harmony and UIExtenderEx.

## Using it

- **New chest** (left header of your own inventory): asks for a name, then the current screen is finished as
  if you pressed Done and the new chest opens on the left.
- **Open a chest:** click its row in your item list. Again the current screen is finished like Done (with the
  game's usual question if items wait on the Discard side), and the chest opens in the game's storage screen:
  chest on the left, party on the right, the usual arrows, "transfer all" and drag-and-drop. You can dress
  your companions from there too.
- **Done or Cancel** in a chest screen brings you back to your inventory. Cancel and Reset undo the moves.
- **Rename / Remove** (left header, while a chest is open). Removing puts the contents back in your inventory.
- Clicking another chest while one is open switches to it.
- In trade, loot and other screens chests are shown but locked; clicking one says where to open it.
- Up to 50 chests. Names up to 40 letters.

## What "in a chest" means

- The items are not in the party's item roster at all. The game, and any other mod, sees only the chest:
  nothing inside is sold, eaten, equipped, used for troop upgrades, counted as trade goods or food.
- The chest is a quest item in the roster: it can't be sold, discarded, given away, moved into another chest,
  or looted when your party loses a battle.
- The chest weighs what is inside (weight × count of every item). Party speed and the inventory's weight
  readout use it like any other cargo, also while the chest is open.
- No horses, mules or livestock: they add carrying capacity and herd penalties, which a boxed animal would not.
- The chest's own value is 1; the tooltip shows the contents' worth at base value.

## How it works

- `module/ModuleData/item_chests.xml` defines 50 items `item_chest_01..50`: goods, not merchandise, the
  crate mesh — the same shape as the vanilla quest item "stolen goods". A chest slot owns one of them, so its
  `ItemObject.Weight` and `Name` are set (by reflection) to that chest's contents and name.
- `ChestBehavior` stores per slot a name (`List<string>`) and an `ItemRoster` of contents, under its own save
  keys. It keeps one rule: every chest in use is in the main party's roster exactly once, quest-flagged; chest
  items of unused slots are removed. Checked after loading, before your inventory opens, before a chest opens
  and once a day.
- The own inventory is recognised by a prefix on the private `InventoryScreenHelper.OpenInventoryPresentation`
  (the public `OpenScreenAsInventory` is a one-liner the JIT may inline) and a postfix on
  `InventoryLogic.Initialize` that marks the logic it builds. In cheat mode it also takes the chest items out
  of the Discard side the game fills with ten of everything.
- A prefix on `SPInventoryVM.ProcessItemSelect` turns a click on a chest into "open". Two inventory screens
  can't be stacked (their view models share static callbacks), so the current screen is closed through the
  game's own `InventoryScreenHelper.CloseScreen` with a wrapped `DoneLogicExtrasDelegate`, and the chest screen
  — an `InventoryState` in `Stash` mode with the chest's roster on the left — is pushed on the next tick. The
  delegate the inventory was opened with (in a scene it re-dresses the player) is carried along every screen.
- The chest screen's `AfterTransfer`/`AfterReset` update the chest's weight before the screen's own view
  model reads it.
- A prefix on the private `InventoryLogic.TransferItem`, where every move ends, drops animals bound for a chest.
- "Take all" from a chest: the game caps it by the party's free capacity, which already counts the chest. A
  prefix on `SPInventoryVM.TransferAll` makes the chest weightless while the budget is worked out; the first
  transfer (or a finalizer) gives it its true weight back.
- The chest's weight is the contents' weight by the game's capacity model (`GetItemEffectiveWeight`).
- A postfix on `ItemMenuVM.SetItem` writes the chest's contents into its tooltip.
- The buttons are a UIExtenderEx prefab insert before the left panel's gold readout, bound to a view model
  mixin on `SPInventoryVM`.

## Known limits

- Leaving a chest screen through the top bar (Party, Clan… or their keys) commits it and goes straight to that
  screen instead of back to the inventory. Nothing is lost.
- Like the game's own stash, goods put in a chest count as "sold" for the Trade skill's profit records: if none
  of that good is left in the party, the purchase price is forgotten, so the Trade XP for selling it later is
  off.

## Adding and removing the mod

Safe to add mid-campaign. The save holds only vanilla types under this mod's keys. Without the mod the save
still loads, but the chest items become "Trash Item" (the game's own fallback for missing items) and the
contents are lost — empty your chests first (Remove, or move everything out).

The dev build (`ItemChests.Dev`) and a released copy define the same items: enable only one of them.

## Build

Needs the .NET SDK. Paths are in `Directory.Build.props`; put yours in a git-ignored
`Directory.Build.props.user`.

```
powershell -ExecutionPolicy Bypass -File tools\deploy.ps1
```

installs the dev build as `Modules\ItemChests.Dev` ("Item Chests (dev)" in the launcher).

```
powershell -ExecutionPolicy Bypass -File tools\package.ps1
```

builds the release layout under `dist\ItemChests` and the zip for a GitHub release.
