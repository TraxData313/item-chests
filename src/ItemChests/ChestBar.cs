using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ItemChests
{
    // The chest buttons in the left header of the inventory screen, before the gold readout:
    // "New chest" in the own inventory and in chest screens, "Rename" and "Remove" in chest screens.
    [PrefabExtension("Inventory", "descendant::BrushWidget[@Id='LeftPanel']/descendant::ListPanel[@Id='GoldContainer']")]
    public sealed class ChestBarPrefab : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Prepend;

        [PrefabExtensionText]
        public string Content => @"
<ListPanel Id=""ItemChestsBar"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""StretchToParent"" VerticalAlignment=""Center"" MarginRight=""10"" IsVisible=""@IsChestBarVisible"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
  <Children>
    " + Button("ExecuteNewChest", "NewChestText", "NewChestHint", "100", null) + @"
    " + Button("ExecuteRenameChest", "RenameChestText", "RenameChestHint", "80", "@IsChestOpen") + @"
    " + Button("ExecuteRemoveChest", "RemoveChestText", "RemoveChestHint", "80", "@IsChestOpen") + @"
  </Children>
</ListPanel>";

        private static string Button(string command, string text, string hint, string width, string? visible) => @"
    <ButtonWidget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""" + width + @""" SuggestedHeight=""36"" VerticalAlignment=""Center"" MarginRight=""4"" Brush=""ButtonBrush2"" Command.Click=""" + command + @""" UpdateChildrenStates=""true""" + (visible == null ? "" : @" IsVisible=""" + visible + @"""") + @">
      <Children>
        <TextWidget DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""Popup.Button.Text"" Brush.FontSize=""16"" Text=""@" + text + @""" />
        <HintWidget DataSource=""{" + hint + @"}"" DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
      </Children>
    </ButtonWidget>";
    }

    [ViewModelMixin]
    public sealed class ChestBarMixin : BaseViewModelMixin<SPInventoryVM>
    {
        private readonly bool _isVisible;
        private readonly bool _isChestOpen;

        public ChestBarMixin(SPInventoryVM vm) : base(vm)
        {
            var screen = ChestScreens.Find(ChestScreens.LogicOf(vm));
            _isVisible = screen != null && ItemChestsCampaignBehavior.Instance != null;
            _isChestOpen = screen?.Kind == ChestScreens.Kind.Chest;
        }

        [DataSourceProperty] public bool IsChestBarVisible => _isVisible;
        [DataSourceProperty] public bool IsChestOpen => _isChestOpen;
        [DataSourceProperty] public string NewChestText => "New chest";
        [DataSourceProperty] public string RenameChestText => "Rename";
        [DataSourceProperty] public string RemoveChestText => "Remove";

        [DataSourceProperty] public HintViewModel NewChestHint { get; } = Hint(
            "Make a new chest and open it. What you put in a chest is out of your party's reach: it is never sold, eaten or equipped, "
            + "but its weight still counts.");
        [DataSourceProperty] public HintViewModel RenameChestHint { get; } = Hint("Rename this chest.");
        [DataSourceProperty] public HintViewModel RemoveChestHint { get; } = Hint("Remove this chest. What is in it goes back into your inventory.");

        [DataSourceMethod]
        public void ExecuteNewChest()
        {
            if (ViewModel != null)
                ChestScreens.NewChest(ViewModel);
        }

        [DataSourceMethod]
        public void ExecuteRenameChest()
        {
            if (ViewModel != null)
                ChestScreens.RenameChest(ViewModel);
        }

        [DataSourceMethod]
        public void ExecuteRemoveChest()
        {
            if (ViewModel != null)
                ChestScreens.RemoveChest(ViewModel);
        }

        private static HintViewModel Hint(string text) => new HintViewModel(new TextObject("{=!}" + text));
    }
}
