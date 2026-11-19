using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace Vintagestory.MovieMaker
{
    public class GuiDialogAddActor : API.Client.GuiDialog
    {
        public bool Saved;
        public SceneActorType SelectedActorType
        {
            get
            {
                var code = SingleComposer.GetDropDown("actortype").SelectedValues.FirstOrDefault();
                return ms.actorTypes.FirstOrDefault(at => at.Code == code);
            }
        }
        public string ActorName => SingleComposer.GetTextInput("actorname").GetText();

        public override string ToggleKeyCombinationCode => null;
        ModSystemMachinima ms => capi.ModLoader.GetModSystem<ModSystemMachinima>();

        bool nameManuallyChanged = false;
        MachinimaWorkSpace workspace;

        public override bool PrefersUngrabbedMouse => false;
        public GuiDialogAddActor(ICoreClientAPI capi, MachinimaWorkSpace workspace) : base(capi)
        {
            this.workspace = workspace;
            Compose();
        }


        private void Compose()
        {
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            ElementBounds dialogBounds = ElementStdBounds
                .AutosizedMainDialog.WithAlignment(EnumDialogArea.LeftMiddle)
                .WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, 0);


            ElementBounds leftButton = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);
            ElementBounds rightButton = ElementBounds.Fixed(EnumDialogArea.RightFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);

            var bounds = ElementBounds.Fixed(0, 30, 250, 25);

            string[] values = ms.actorTypes.Select(at => at.Code).ToArray();
            string[] names = ms.actorTypes.Select(at => at.Name).ToArray();

            SingleComposer =
                capi.Gui
                .CreateCompo("addactor", dialogBounds)
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar("Add actor", OnTitleBarClose)
                .BeginChildElements(bgBounds)
                .AddStaticText("Type", CairoFont.WhiteDetailText(), bounds)
                .AddTextInput(bounds = bounds.BelowCopy(0, 5), onSearchTextChanged, CairoFont.TextInput(), "search")
                .AddDropDown(values, names, 0, onSelectedActorChanged, bounds = bounds.BelowCopy(0, 5), "actortype")
                .AddDynamicText("Name", CairoFont.WhiteDetailText(), bounds = bounds.BelowCopy(0, 5))
                .AddTextInput(bounds = bounds.BelowCopy(0, 5), null, CairoFont.TextInput(), "actorname")
            ;

            var b = SingleComposer.LastAddedElementBounds;
            SingleComposer
                
                .AddSmallButton(Lang.Get("Cancel"), OnClose, leftButton.FixedUnder(b, 25))
                .AddSmallButton(Lang.Get("Add"), OnSave, rightButton.FixedUnder(b, 25), EnumButtonStyle.Normal, "confirm")
                .EndChildElements()
                .Compose()
            ;

            SingleComposer.GetTextInput("search").SetPlaceHolderText(Lang.Get("Search..."));
            SingleComposer.GetTextInput("actorname").OnKeyPressed = () => nameManuallyChanged = true;
            SingleComposer.FocusElement(SingleComposer.GetTextInput("search").TabIndex);

            onSelectedActorChanged(null, false);
        }

        private void onSearchTextChanged(string text)
        {
            var regex = RegexFromSearchText(text);
            var actorTypes = regex == null ? ms.actorTypes : ms.actorTypes.Where(at => regex.IsMatch(at.Name.ToSearchFriendly())).ToArray();
            var dropDown = SingleComposer.GetDropDown("actortype");
            dropDown.SetSelectedIndex(-1);
            dropDown.SetList(actorTypes.Select(at => at.Code).ToArray(), actorTypes.Select(at => at.Name).ToArray());
            if (actorTypes.Length > 0) dropDown.SetSelectedIndex(0);
            dropDown.Enabled = actorTypes.Length > 0;
            onSelectedActorChanged(null, false);
        }

        private void onSelectedActorChanged(string code, bool selected)
        {
            if (!nameManuallyChanged && SelectedActorType is SceneActorType actorType)
            {
                SingleComposer.GetTextInput("actorname").SetValue(actorType.Name + " #" + (workspace.SelectedSequence.Actors.Count + 1));
            }
        }

        private void OnTitleBarClose() => TryClose();


        private bool OnClose()
        {
            TryClose();
            return true;
        }

        private bool OnSave()
        {
            Saved = SelectedActorType != null;
            TryClose();
            return true;
        }
    }




    public class GuiDialogCreateSequence : API.Client.GuiDialog
    {
        public bool Saved;
        public string SequenceName => SingleComposer.GetTextInput("name").GetText();
        public float Duration => SingleComposer.GetNumberInput("duration").GetValue();
        public override string ToggleKeyCombinationCode => null;
        public GuiDialogCreateSequence(ICoreClientAPI capi) : base(capi) => Compose();

        private void Compose()
        {
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            ElementBounds dialogBounds = ElementStdBounds
                .AutosizedMainDialog.WithAlignment(EnumDialogArea.LeftMiddle)
                .WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, 0);


            ElementBounds leftButton = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);
            ElementBounds rightButton = ElementBounds.Fixed(EnumDialogArea.RightFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);

            var bounds = ElementBounds.Fixed(0, 0, 250, 25);

            SingleComposer =
                capi.Gui
                .CreateCompo("createsequence", dialogBounds)
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar("Create Sequence", OnTitleBarClose)
                .BeginChildElements(bgBounds)
                .AddDynamicText("Name", CairoFont.WhiteDetailText(), bounds = bounds.BelowCopy(0, 0))
                .AddTextInput(bounds = bounds.BelowCopy(0, -5), null, CairoFont.TextInput(), "name")

                .AddDynamicText("Duration in seconds", CairoFont.WhiteDetailText(), bounds = bounds.BelowCopy(0, 10))
                .AddNumberInput(bounds = bounds.BelowCopy(0, -5), null, CairoFont.TextInput(), "duration")
            ;

            var b = SingleComposer.LastAddedElementBounds;
            SingleComposer

                .AddSmallButton(Lang.Get("Cancel"), OnClose, leftButton.FixedUnder(b, 20))
                .AddSmallButton(Lang.Get("Create"), OnSave, rightButton.FixedUnder(b, 20), EnumButtonStyle.Normal, "confirm")
                .EndChildElements()
                .Compose()
            ;

            SingleComposer.GetNumberInput("duration").SetValue(10);
        }

        private void OnTitleBarClose() => TryClose();


        private bool OnClose()
        {
            TryClose();
            return true;
        }

        private bool OnSave()
        {
            Saved = true;
            TryClose();
            return true;
        }
    }
}
