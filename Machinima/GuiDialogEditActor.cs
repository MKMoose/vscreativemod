using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace Vintagestory.MovieMaker
{
    public class GuiDialogEditActor : GuiDialog
    {
        MachinimaWorkSpace workspace;
        ModelTransform currentTransform = new ModelTransform();
        DropDownEntry[] animationEntries = [];
        bool loading;
        public static DropDownEntry[] interpolationEntries = [new DropDownEntry("Linear", "Linear"), new DropDownEntry("Bezier", "Cubic"), new DropDownEntry("Step", "Constant")];

        public MovieSequenceElementKeyFrame? keyframe;
        public Action OnTrackChanged;

        public override bool PrefersUngrabbedMouse => false;
        public override bool UnregisterOnClose => true;

        public GuiDialogEditActor(ICoreClientAPI capi, MachinimaWorkSpace workspace, MovieSequenceElementKeyFrame kf) : base(capi)
        {
            this.keyframe = kf;
            this.workspace = workspace;
            Compose();
        }

        public override string ToggleKeyCombinationCode => null;

        public void Compose()
        {
            ElementBounds line = ElementBounds.Fixed(0, 28, 200, 20);
            ElementBounds inputBnds = ElementBounds.Fixed(0, 22, 150, 23);

            // 2. Around all that is 10 pixel padding
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding/2);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            // 3. Finally Dialog
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.LeftTop)
                .WithFixedAlignmentOffset(340 + GuiStyle.DialogToScreenPadding, GuiStyle.DialogToScreenPadding)
            ;

            var actor = workspace.SelectedSequence?.SelectedActor;

            SingleComposer = capi.Gui
                .CreateCompo("actor", dialogBounds.FlatCopy())
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar(actor?.Name, () => { TryClose(); })
                .BeginChildElements(bgBounds)
                    .AddStaticText("Translation X", CairoFont.WhiteDetailText(), line = line.FlatCopy().WithFixedWidth(150))
                    .AddNumberInput(inputBnds = inputBnds.BelowCopy(0, 0), OnTranslateX, CairoFont.WhiteDetailText(), "translatex")
                    .AddStaticText("Origin X", CairoFont.WhiteDetailText(), line.RightCopy(20))
                    .AddNumberInput(inputBnds.RightCopy(20), OnOriginX, CairoFont.WhiteDetailText(), "originx")

                    .AddStaticText("Translation Y", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25))
                    .AddNumberInput(inputBnds = inputBnds.BelowCopy(0, 22), OnTranslateY, CairoFont.WhiteDetailText(), "translatey")
                    .AddStaticText("Origin Y", CairoFont.WhiteDetailText(), line.RightCopy(20))
                    .AddNumberInput(inputBnds.RightCopy(20), OnOriginY, CairoFont.WhiteDetailText(), "originy")

                    .AddStaticText("Translation Z", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25))
                    .AddNumberInput(inputBnds = inputBnds.BelowCopy(0, 22), OnTranslateZ, CairoFont.WhiteDetailText(), "translatez")
                    .AddStaticText("Origin Z", CairoFont.WhiteDetailText(), line.RightCopy(20))
                    .AddNumberInput(inputBnds.RightCopy(20), OnOriginZ, CairoFont.WhiteDetailText(), "originz")

                    .AddStaticText("Rotation X", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25).WithFixedWidth(310))
                    .AddSlider(OnRotateX, inputBnds = inputBnds.BelowCopy(0, 22).WithFixedWidth(317), "rotatex")
                    .AddStaticText("Rotation Y", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25))
                    .AddSlider(OnRotateY, inputBnds = inputBnds.BelowCopy(0, 22), "rotatey")
                    .AddStaticText("Rotation Z", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25))
                    .AddSlider(OnRotateZ, inputBnds = inputBnds.BelowCopy(0, 22), "rotatez")

                    .AddStaticText("Scale", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25))
                    .AddSlider(OnScale, inputBnds = inputBnds.BelowCopy(0, 22), "scale")

                    // We also need
                    // - camera track editor

                    .AddButton("Animations...", onClickAnimations, line.BelowCopy(210, 45).WithFixedSize(100, 59).WithFixedPadding(5), EnumButtonStyle.Normal, "editanimations")

                    .AddStaticText("Static Animation", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25).WithFixedWidth(200))
                    .AddDropDown(animationEntries, 0, onAnimationSelected, inputBnds = inputBnds.BelowCopy(0, 25).WithFixedWidth(200), "animations")

                    .AddStaticText("Animation frame", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25).WithFixedWidth(200))
                    .AddSlider(OnFrame, inputBnds = inputBnds.BelowCopy(0, 22).WithFixedWidth(200), "animframe")

                    .AddStaticText("Keyframe interpolation", CairoFont.WhiteDetailText(), line = line.BelowCopy(0, 25).WithFixedWidth(200))
                    .AddDropDown(interpolationEntries, 0, onInterpolationSelected, inputBnds = inputBnds.BelowCopy(0, 25).WithFixedWidth(200), "interpolation")
                    .AddStaticText("Auto key", CairoFont.WhiteDetailText(), line.RightCopy(20).WithFixedWidth(100))
                    .AddSwitch(onAutoKey, inputBnds.RightCopy(20).WithFixedWidth(30), "autokey")
            ;

            var bounds = inputBnds.BelowCopy(0, 10).WithAlignment(EnumDialogArea.LeftFixed).WithFixedAlignmentOffset(0, 0).WithFixedPadding(5, 2).WithFixedWidth(145);

            SingleComposer
                .AddButton("Delete Keyframe", DeleteKeyFrame, bounds, CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), EnumButtonStyle.Small)
                .AddButton("Set Keyframe", CreateKeyFrame, bounds.FlatCopy().WithAlignment(EnumDialogArea.RightFixed), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), EnumButtonStyle.Small)
                .EndChildElements()
                .Compose()
            ;
        }

        GuiDialogActorAnimations dlgAnimations;
        private bool onClickAnimations()
        {
            OpenAnimations(null);
            return true;
        }

        public void OpenAnimations(AnimationClip clip)
        {
            dlgAnimations?.TryClose();
            dlgAnimations = new GuiDialogActorAnimations(capi, workspace.SelectedSequence, actor, getOrCreateTrack(), genAnimationEntries(actor), OnTrackChanged);
            dlgAnimations.TryOpen();
            if (clip != null) dlgAnimations.SelectClip(clip);
        }

        public void AddAnimation()
        {
            OpenAnimations(null);
            dlgAnimations.OnCreateClip();
        }

        private SequenceTrack getOrCreateTrack()
        {
            var track = workspace.SelectedSequence.Tracks.FirstOrDefault(t => t.ActorId == actor.Id);
            if (track == null) workspace.SelectedSequence.Tracks.Add(track = new SequenceTrack()
            {
                ActorId = actor.Id,
                Name = actor.Name,
                Type = EnumSequenceElementType.Actor
            });
            return track;
        }

        public void SetKeyFrame(MovieSequenceElementKeyFrame kf)
        {
            this.keyframe = kf;
            workspace.SelectedSequence.SelectedKeyframe = kf;
            loadValues(workspace.SelectedSequence.SelectedActor);
        }

        private void onInterpolationSelected(string code, bool selected)
        {
            if (keyframe == null) return;
            keyframe.Interpolation = Enum.Parse<EnumKeyFrameInterpolation>(code);
            OnTrackChanged?.Invoke();
        }

        private void onAutoKey(bool on)
        {
            workspace.AutoKey = on;
        }

        public void MoveBy(Vec3d delta)
        {
            currentTransform.Translation.Add((float)delta.X, (float)delta.Y, (float)delta.Z);
            updateActor();
            loadValues(actor);
        }

        public bool CreateKeyFrame()
        {
            if (keyframe == null)
            {
                var track = getOrCreateTrack();
                track.KeyFrames.Add(keyframe = new MovieSequenceElementKeyFrame()
                {
                    Time = workspace.SelectedSequence.CurrentFrame,
                    Transform = currentTransform.Clone(),
                    Interpolation = Enum.Parse<EnumKeyFrameInterpolation>(SingleComposer.GetDropDown("interpolation").SelectedValue)
                });
                track.KeyFrames.Sort((a, b) => a.Time.CompareTo(b.Time));

                SetKeyFrame(keyframe);
            }

            OnTrackChanged?.Invoke();


            return true;
        }


        public bool DeleteKeyFrame()
        {
            if (this.keyframe == null) return false;

            var track = workspace.SelectedSequence.Tracks.FirstOrDefault(t => t.ActorId == actor.Id);
            track?.KeyFrames.Remove(keyframe);

            SetKeyFrame(null);
            OnTrackChanged?.Invoke();
            return true;
        }



        private bool OnFrame(int t)
        {
            if (actor.Animator == null) return false;
            var animCode = SingleComposer.GetDropDown("animations").SelectedValue;
            var animstate = actor.Animator.GetAnimationState(animCode);
            if (animstate == null) return false;
            animstate.CurrentFrame = Math.Min(t, animstate.Animation.QuantityFrames - 0.003f);
            animstate.BlendedWeight = 1;

            return true;
        }

        private void onAnimationSelected(string code, bool selected)
        {
            actor.Animation = code;

            if (actor.Animation != null && actor.Animation != "")
            {
                var anim = actor.Type.ShapeAnimations.FirstOrDefault(anim => anim.Code == actor.Animation);
                SingleComposer.GetSlider("animframe").SetValues(actor.AnimationFrame, 0, anim.QuantityFrames, 1);
            }

            actor.activeAnimationsByAnimCode.Clear();
            actor.activeAnimationsByAnimCode[code] = new AnimationMetaData()
            {
                Animation = code,
                AnimationSpeed = 0f,
                StartFrameOnce = SingleComposer.GetSlider("animframe").GetValue()
            };

            foreach (var val in actor.Animator?.anims ?? [])
            {
                val.Stop();
            }

            OnFrame(SingleComposer.GetSlider("animframe").GetValue());
        }

        private void OnOriginX(string val)
        {
            currentTransform.Origin.X = val.ToFloat();
            updateActor();
        }

        private void OnOriginY(string val)
        {
            currentTransform.Origin.Y = val.ToFloat();
            updateActor();
        }

        private void OnOriginZ(string val)
        {
            currentTransform.Origin.Z = val.ToFloat();
            updateActor();
        }



        private bool OnScale(int val)
        {
            currentTransform.Scale = val / 100f;
            updateActor();
            return true;
        }

        private bool OnRotateX(int deg)
        {
            currentTransform.Rotation.X = deg;
            updateActor();
            return true;
        }

        private bool OnRotateY(int deg)
        {
            currentTransform.Rotation.Y = deg;
            updateActor();
            return true;
        }

        private bool OnRotateZ(int deg)
        {
            currentTransform.Rotation.Z = deg;
            updateActor();
            return true;
        }


        private void OnTranslateX(string val)
        {
            currentTransform.Translation.X = val.ToFloat();
            updateActor();
            capi.ModLoader.GetModSystem<ModSystemMachinima>()?.ReloadGizmoPosition();
        }

        private void OnTranslateY(string val)
        {
            currentTransform.Translation.Y = val.ToFloat();
            updateActor();
            capi.ModLoader.GetModSystem<ModSystemMachinima>()?.ReloadGizmoPosition();
        }

        private void OnTranslateZ(string val)
        {
            currentTransform.Translation.Z = val.ToFloat();
            updateActor();
            capi.ModLoader.GetModSystem<ModSystemMachinima>()?.ReloadGizmoPosition();
        }

        public void updateActor()
        {
            if (loading || keyframe != null) return;
            if (workspace.AutoKey && workspace.SelectedSequence.Tracks.FirstOrDefault(t => t.ActorId == actor.Id)?.KeyFrames.Count > 0)
            {
                CreateKeyFrame();
                return;
            }
            // With auto key off and no keyframe selected edits on a keyframe are preview only
            actor.SetTransform(currentTransform);
        }

        SceneActor actor;
        public void loadValues(SceneActor actor)
        {
            this.actor = actor;
            if (actor == null) return;

            loading = true;
            this.currentTransform = keyframe?.Transform ?? workspace.SelectedSequence.GetTransform(actor, workspace.SelectedSequence.CurrentFrame, out _).Clone();

            SingleComposer.GetTextInput("translatex").SetValue(currentTransform.Translation.X.ToString(GlobalConstants.DefaultCultureInfo));
            SingleComposer.GetTextInput("translatey").SetValue(currentTransform.Translation.Y.ToString(GlobalConstants.DefaultCultureInfo));
            SingleComposer.GetTextInput("translatez").SetValue(currentTransform.Translation.Z.ToString(GlobalConstants.DefaultCultureInfo));

            SingleComposer.GetTextInput("originx").SetValue(currentTransform.Origin.X.ToString(GlobalConstants.DefaultCultureInfo));
            SingleComposer.GetTextInput("originy").SetValue(currentTransform.Origin.Y.ToString(GlobalConstants.DefaultCultureInfo));
            SingleComposer.GetTextInput("originz").SetValue(currentTransform.Origin.Z.ToString(GlobalConstants.DefaultCultureInfo));

            SingleComposer.GetSlider("rotatex").SetValues((int)currentTransform.Rotation.X, -180, 180, 1);
            SingleComposer.GetSlider("rotatey").SetValues((int)currentTransform.Rotation.Y, -180, 180, 1);
            SingleComposer.GetSlider("rotatez").SetValues((int)currentTransform.Rotation.Z, -180, 180, 1);
            SingleComposer.GetSlider("scale").SetValues((int)Math.Abs(100 * currentTransform.ScaleXYZ.X), 5, 600, 1);
            SingleComposer.GetDropDown("interpolation").SetSelectedIndex((int)(keyframe?.Interpolation ?? EnumKeyFrameInterpolation.Bezier));
            SingleComposer.GetSwitch("autokey").On = workspace.AutoKey;

            this.animationEntries = genAnimationEntries(actor);
            SingleComposer.GetDropDown("animations").SetList(genAnimationEntries(actor));
            SingleComposer.GetDropDown("animations").Enabled = animationEntries.Length > 0;
            SingleComposer.GetButton("editanimations").Enabled = animationEntries.Length > 0;

            if (actor.Animation != null && actor.Animation != "")
            {
                if (animationEntries.Length > 0)
                {
                    SingleComposer.GetDropDown("animations").SetSelectedIndex(animationEntries.IndexOf((entry) => entry.Value == actor.Animation));
                }

                var anim = actor.Type.ShapeAnimations.FirstOrDefault(anim => anim.Code == actor.Animation);
                SingleComposer.GetSlider("animframe").SetValues(actor.AnimationFrame, 0, anim.QuantityFrames, 1);
            }
            loading = false;
        }

        public DropDownEntry[] genAnimationEntries(SceneActor actor)
        {
            var anims = actor.Type.ShapeAnimations;
            if (anims == null) return [];

            DropDownEntry[] entries = new DropDownEntry[anims.Length + 1];
            entries[0] = new DropDownEntry("", "-");

            for (int i = 0; i < anims.Length; i++)
            {
                entries[i + 1] = new DropDownEntry(anims[i].Code, anims[i].Name);
            }

            return entries;
        }

        public override void OnGuiClosed()
        {
            dlgAnimations?.TryClose();
            base.OnGuiClosed();
        }
    }
}
