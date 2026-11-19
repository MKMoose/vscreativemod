using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace Vintagestory.MovieMaker
{
    public class GuiDialogContextMenu : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => false;
        public override bool UnregisterOnClose => true;

        GuiElementListMenu listMenu;
        Action[] actions;

        public GuiDialogContextMenu(ICoreClientAPI capi, int x, int y, (string name, Action action)[] entries) : base(capi)
        {
            actions = entries.Select(e => e.action).ToArray();
            var font = CairoFont.WhiteSmallText();
            double width = entries.Max(e => font.GetTextExtents(e.name).Width) / RuntimeEnv.GUIScale + 20;
            double left = x / RuntimeEnv.GUIScale;
            if (left + width > capi.Render.FrameWidth / RuntimeEnv.GUIScale) left -= width;
            listMenu = new GuiElementListMenu(capi, entries.Select((e, i) => i + "").ToArray(), entries.Select(e => e.name).ToArray(), -1, onSelected, ElementBounds.Fixed(0, 0, width, 30), font, false);
            SingleComposer = capi.Gui
                .CreateCompo("contextmenu", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.None).WithFixedPosition(left, y / RuntimeEnv.GUIScale - 30))
                .AddInteractiveElement(listMenu)
                .Compose();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            listMenu.Open();
        }

        private void onSelected(string value, bool on)
        {
            actions[value.ToInt()]();
            TryClose();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            Dispose();
        }

        public override void UnFocus()
        {
            base.UnFocus();
            if (IsOpened()) TryClose();
        }

        public override void OnMouseDown(MouseEvent args)
        {
            base.OnMouseDown(args);
            if (!args.Handled || !listMenu.IsOpened) TryClose();
        }
    }
}
