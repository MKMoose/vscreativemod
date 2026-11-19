using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace Vintagestory.MovieMaker
{
    public class GuiDialogActorAnimations : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;

        protected Sequence seq;
        protected SceneActor actor;
        protected SequenceTrack track;
        protected DropDownEntry[] clipEntries;
        protected DropDownEntry[] animationEntries;
        protected AnimationClip? selectedClip;
        protected Action onChanged;

        public override bool PrefersUngrabbedMouse => false;

        public GuiDialogActorAnimations(ICoreClientAPI capi, Sequence seq, SceneActor actor, SequenceTrack track, DropDownEntry[] animationEntries, Action onChanged) : base(capi)
        {
            this.seq = seq;
            this.actor = actor;
            this.track = track;
            this.animationEntries = animationEntries;
            this.onChanged = onChanged;
            Compose();
        }

        protected void Compose()
        {
            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.LeftTop)
                .WithFixedAlignmentOffset(400 + 345 + GuiStyle.DialogToScreenPadding, GuiStyle.DialogToScreenPadding);

            var rightButton = ElementBounds.Fixed(EnumDialogArea.RightFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);

            var inputBounds = ElementBounds.Fixed(0, 30, 300, 30);
            var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding / 2).WithSizing(ElementSizing.FitToChildren);
            loadClipEntries();

            Composers["animations"] = capi.Gui
               .CreateCompo("animations", dialogBounds.FlatCopy())
               .AddShadedDialogBG(bgBounds, true)
               .AddDialogTitleBar("Animations - " + actor.Name, () => { TryClose(); })
               .BeginChildElements(bgBounds)
                   .AddDropDown(clipEntries, 0, onClipSelected, inputBounds, "clipList")
                   .AddSmallButton(Lang.Get("Add Animation"), OnCreateClip, rightButton.FlatCopy().FixedUnder(inputBounds, 15), EnumButtonStyle.Normal, "create")
               .EndChildElements()
               .Compose()
            ;
        }

        public bool OnCreateClip()
        {
            var anim = actor.Type.ShapeAnimations?.FirstOrDefault();
            track.Animations.Add(new AnimationClip()
            {
                Code = anim?.Code ?? "",
                StartTime = seq.CurrentFrame,
                EndTime = Math.Min(seq.Duration, seq.CurrentFrame + (anim?.QuantityFrames ?? 30) / 30.0)
            });

            reloadClipList();
            SelectClip(track.Animations[^1]);
            onChanged();
            return true;
        }

        public void SelectClip(AnimationClip clip)
        {
            int index = track.Animations.IndexOf(clip);
            Composers["animations"].GetDropDown("clipList").SetSelectedIndex(index);
            onClipSelected(index + "", false);
        }

        private void loadClipEntries()
        {
            clipEntries = new DropDownEntry[track.Animations.Count];
            for (int i = 0; i < clipEntries.Length; i++)
            {
                var clip = track.Animations[i];
                clipEntries[i] = new DropDownEntry(i + "", clip.Code + " " + clip.StartTime.ToString("0.00") + "s - " + clip.EndTime.ToString("0.00") + "s");
            }
        }

        private void reloadClipList()
        {
            loadClipEntries();
            Composers["animations"].GetDropDown("clipList").SetList(clipEntries);
        }

        private void onClipSelected(string code, bool selected)
        {
            selectedClip = track.Animations[code.ToInt()];

            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.LeftTop)
                .WithFixedAlignmentOffset(400 + 345 + GuiStyle.DialogToScreenPadding, 130 + GuiStyle.DialogToScreenPadding);

            var leftButton = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);
            var rightButton = ElementBounds.Fixed(EnumDialogArea.RightFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);

            var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding / 2).WithSizing(ElementSizing.FitToChildren);

            var bounds = ElementBounds.Fixed(0, 0, 250, 25);

            Composers["editclip"] = capi.Gui
               .CreateCompo("editclip", dialogBounds.FlatCopy())
               .AddShadedDialogBG(bgBounds, true)
               .AddDialogTitleBar("Edit animation #" + code, () => { TryClose(); })
               .BeginChildElements(bgBounds)
                    .AddStaticText("Animation", CairoFont.WhiteDetailText(), bounds = bounds.BelowCopy(0, 5))
                    .AddDropDown(animationEntries, -1, null, bounds = bounds.BelowCopy(0, 0), CairoFont.TextInput(), "animCode")

                    .AddStaticText("Animation Speed", CairoFont.WhiteDetailText(), bounds = bounds.BelowCopy(0, 10))
                    .AddNumberInput(bounds = bounds.BelowCopy(0, -5), null, CairoFont.TextInput(), "speed")

                    .AddStaticText("Start (seconds)", CairoFont.WhiteDetailText(), bounds = bounds.BelowCopy(0, 10))
                    .AddNumberInput(bounds = bounds.BelowCopy(0, -5), null, CairoFont.TextInput(), "start")

                    .AddStaticText("End (seconds)", CairoFont.WhiteDetailText(), bounds = bounds.BelowCopy(0, 10))
                    .AddNumberInput(bounds = bounds.BelowCopy(0, -5), null, CairoFont.TextInput(), "end")

                    .AddStaticText("Offset (seconds)", CairoFont.WhiteDetailText(), bounds = bounds.BelowCopy(0, 10))
                    .AddNumberInput(bounds = bounds.BelowCopy(0, -5), null, CairoFont.TextInput(), "offset")

                   .AddSmallButton(Lang.Get("Delete Animation"), OnDeleteClip, leftButton.FlatCopy().FixedUnder(bounds, 15))
                   .AddSmallButton(Lang.Get("Save"), OnSaveClip, rightButton.FlatCopy().FixedUnder(bounds, 15), EnumButtonStyle.Normal, "create")
               .EndChildElements()
               .Compose()
            ;

            Composers["editclip"].GetNumberInput("speed").SetValue(selectedClip.Speed);
            Composers["editclip"].GetNumberInput("start").SetValue((float)selectedClip.StartTime);
            Composers["editclip"].GetNumberInput("end").SetValue((float)selectedClip.EndTime);
            Composers["editclip"].GetNumberInput("offset").SetValue(selectedClip.Offset);
            Composers["editclip"].GetDropDown("animCode").SetSelectedIndex(animationEntries.IndexOf(entry => entry.Value == selectedClip.Code));
        }

        private bool OnDeleteClip()
        {
            selectedClip.Stop(actor);
            track.Animations.Remove(selectedClip);
            seq.SelectedClip = null;

            reloadClipList();
            Composers["editclip"].Dispose();
            Composers.Remove("editclip");
            onChanged();
            return true;
        }

        private bool OnSaveClip()
        {
            var compo = Composers["editclip"];
            selectedClip.Stop(actor);
            selectedClip.Code = compo.GetDropDown("animCode").SelectedValue;
            selectedClip.Speed = compo.GetNumberInput("speed").GetValue();
            selectedClip.StartTime = compo.GetNumberInput("start").GetValue();
            selectedClip.EndTime = compo.GetNumberInput("end").GetValue();
            selectedClip.Offset = compo.GetNumberInput("offset").GetValue();

            reloadClipList();
            onChanged();
            return true;
        }
    }
}
