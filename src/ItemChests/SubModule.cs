using System;
using System.Collections.Generic;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ItemChests
{
    public class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "trax.bannerlord.itemchests";

        private static bool _patched;
        private static UIExtender? _uiExtender;
        private static readonly HashSet<string> Logged = new HashSet<string>();

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                _uiExtender = UIExtender.Create("ItemChests");
                _uiExtender.Register(typeof(SubModule).Assembly);
                _uiExtender.Enable();
            }
            catch (Exception e)
            {
                // Without it there are no buttons, but existing chests still open, close and weigh right.
                Debug.Print("[ItemChests] UIExtenderEx setup failed: " + e);
            }
        }

        // Patched on the first campaign start rather than in OnSubModuleLoad, so nothing the patched methods
        // touch is JIT-compiled (and its static constructors run) before a campaign exists.
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (!(game.GameType is Campaign) || !(gameStarterObject is CampaignGameStarter starter))
                return;
            starter.AddBehavior(new ChestBehavior());
            if (_patched)
                return;
            _patched = true;
            var harmony = new Harmony(HarmonyId);
            try
            {
                Patches.Apply(harmony);
            }
            catch (Exception e)
            {
                Warn("patching the inventory failed, chests can't be opened: " + e);
            }
            try
            {
                Patches.ApplyTooltip(harmony);
            }
            catch (Exception e)
            {
                Debug.Print("[ItemChests] chest tooltip patch failed: " + e);
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            ChestScreens.Tick();
        }

        internal static void Warn(string message)
        {
            Debug.Print("[ItemChests] " + message);
            InformationManager.DisplayMessage(new InformationMessage("Item Chests: " + message.Split('\n')[0], Colors.Yellow));
        }

        // For the code that runs inside the game's own methods: log the first failure of each site, never rethrow.
        internal static void LogOnce(string where, Exception e)
        {
            if (!Logged.Add(where))
                return;
            Debug.Print("[ItemChests] " + where + " failed, using vanilla behaviour: " + e);
        }
    }
}
