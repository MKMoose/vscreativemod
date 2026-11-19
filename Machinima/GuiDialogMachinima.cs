using Cairo;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace Vintagestory.MovieMaker
{
    // Concept:
    // Client side only, but persistent data structures.
    // - SceneActor: Just a shape and a texture, which can be animated.
    // - Sequence: A list of SequenceTracks, which can be either Camera or Actor tracks.
    // - SequenceTrack: A list of KeyFrames, which can be either Camera or Actor keyframes.

    // Hierarchy:
    // 1. Create a sequence. Actor and Track dialog disabled without sequence. The sequence also is the reference base position for actors and camera - which operate on offsets to the sequence position.
    // 2. Create an actor. Track dialog disbaled without actor. Actor only stores offsets to the base pos as a ModelTransform for static placement
    // 3. Actor editor is used to add/modify keyframes
    public class GuiDialogMachinima : GuiDialog
    {
        protected ModSystemMachinima modSystem => capi.ModLoader.GetModSystem<ModSystemMachinima>();
        protected GuiElementCellList<SceneActor> actorListElem;
        protected GuiElementCellList<SequenceTrack> trackListElem;
        protected MachinimaWorkSpace workspace;
        LoadedTexture playheadTex;

        DropDownEntry[] sequenceEntries;


        public Action<SceneActor> OnActorSelected;

        public override bool PrefersUngrabbedMouse => false;

        public GuiDialogMachinima(ICoreClientAPI capi, MachinimaWorkSpace workspace) : base(capi)
        {
            this.workspace = workspace;
            initGui(capi, workspace);
        }

        private void initGui(ICoreClientAPI capi, MachinimaWorkSpace workspace)
        {
            var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.LeftTop).WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, GuiStyle.DialogToScreenPadding);
            var leftButton = ElementBounds.Fixed(EnumDialogArea.LeftFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);
            var rightButton = ElementBounds.Fixed(EnumDialogArea.RightFixed, 0, 0, 0, 0).WithFixedPadding(8, 5);

            var inputBounds = ElementBounds.Fixed(0, 30, 300, 30);
            var durationBounds = inputBounds.BelowCopy(0, 10).WithFixedWidth(145);
            var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding / 2).WithSizing(ElementSizing.FitToChildren);
            workspace.selectedSequenceIndex = -1;
            loadSequenceDropdownEntries();

            Composers["sequences"] = capi.Gui
               .CreateCompo("sequences", dialogBounds.FlatCopy())
               .AddShadedDialogBG(bgBounds, true)
               .AddDialogTitleBar("Sequences", () => { TryClose(); })
               .BeginChildElements(bgBounds)
                   .AddDropDown(sequenceEntries, -1, onSequenceSelected, inputBounds, "sequencesList")
                   .AddStaticText("Duration (seconds)", CairoFont.WhiteDetailText(), durationBounds.FlatCopy().WithFixedOffset(0, 6))
                   .AddNumberInput(durationBounds.RightCopy(10), onDurationChanged, CairoFont.TextInput(), "duration")
                   .AddSmallButton(Lang.Get("Delete sequence"), OnDeleteSequence, leftButton.FlatCopy().FixedUnder(durationBounds, 15))
                   .AddSmallButton(Lang.Get("Create sequence"), OnCreateSequence, rightButton.FlatCopy().FixedUnder(durationBounds, 15), EnumButtonStyle.Normal, "create")
               .EndChildElements()
               .Compose()
            ;


            dialogBounds = ElementStdBounds.AutosizedMainDialog.WithFixedPosition(0, 10).WithAlignment(EnumDialogArea.LeftTop).WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, 180);
            bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding / 2).WithSizing(ElementSizing.FitToChildren);

            double cellListHeight = 250;
            var listBounds = ElementBounds.Fixed(0, 30, 270, cellListHeight);
            var clipBounds = listBounds.ForkBoundingParent();
            var insetBounds = listBounds.FlatCopy().FixedGrow(3).WithFixedOffset(0, 0);
            var scrollbarBounds = insetBounds.CopyOffsetedSibling(3 + listBounds.fixedWidth + 7).WithFixedWidth(20);


            Composers["actors"] = capi.Gui
                .CreateCompo("actors", dialogBounds.FlatCopy())
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar("Actors", () => { TryClose(); })
                .BeginChildElements(bgBounds)
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddCellList(listBounds, createActorCell, new List<SceneActor>(), "actorsList")
                    .EndClip()
                    .AddVerticalScrollbar((value) =>
                    {
                        actorListElem.Bounds.fixedY = 0 - value;
                        actorListElem.Bounds.CalcWorldBounds();
                    }, scrollbarBounds, "scrollbar")

                    .AddSmallButton(Lang.Get("Delete Actor"), OnDeleteActor, leftButton.FlatCopy().FixedUnder(clipBounds, 15))
                    .AddSmallButton(Lang.Get("Add actor"), OnAddActor, rightButton.FlatCopy().FixedUnder(clipBounds, 15), EnumButtonStyle.Normal, "create")
                .EndChildElements()
            ;
            actorListElem = Composers["actors"].GetCellList<SceneActor>("actorsList");
            actorListElem.BeforeCalcBounds();
            actorListElem.UnscaledCellVerPadding = 0;
            actorListElem.unscaledCellSpacing = 5;
            Composers["actors"].Compose();



            dialogBounds = dialogBounds.FlatCopy().WithAlignment(EnumDialogArea.LeftBottom).WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, -GuiStyle.DialogToScreenPadding - 100);
            bgBounds = ElementBounds.Fill.WithFixedPadding(0).WithSizing(ElementSizing.FitToChildren);
            listBounds = ElementBounds.Fixed(0, 30+35, 900, cellListHeight);
            clipBounds = listBounds.ForkBoundingParent();
            insetBounds = listBounds.FlatCopy().FixedGrow(3).WithFixedOffset(0, 0);
            scrollbarBounds = insetBounds.CopyOffsetedSibling(3 + listBounds.fixedWidth + 7).WithFixedWidth(20);

            var playBounds = ElementBounds.Fixed(5, 35, 25, 25);
            var pauseBounds = ElementBounds.Fixed(35, 35, 25, 25);

            var frameSliderBounds = ElementBounds.Fixed(140 - 7.5, 37, 760 + 14, 20).WithFixedMargin(3);

            var sequenceTracks = new List<SequenceTrack>();

            GuiElementSlider frameSliderElem = null;

            playheadTex = new LoadedTexture(capi);
            ImageSurface surface = new ImageSurface(Format.Argb32, 1, 1);
            Context ctx = new Context(surface);
            ctx.SetSourceRGBA(1, 0.35, 0.2, 1);
            ctx.Paint();
            capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref playheadTex);
            ctx.Dispose();
            surface.Dispose();

            if (!capi.Gui.Icons.CustomIcons.ContainsKey("play"))
            {
                capi.Gui.Icons.CustomIcons["play"] = capi.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/play.svg"));
                capi.Gui.Icons.CustomIcons["pause"] = capi.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/pause.svg"));
            }

            Composers["tracks"] = capi.Gui
                .CreateCompo("tracks", dialogBounds.FlatCopy())
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar("Tracks", () => { TryClose(); })
                .BeginChildElements(bgBounds)
                    .AddIconButton("play", onPlay, playBounds)
                    .AddIconButton("pause", onPause, pauseBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(70, 40, 65, 20), "time")
                    .AddSlider(onSetFrame, frameSliderBounds, "frameslider")

                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddCellList(listBounds, createTrackCell, sequenceTracks, "tracksList")
                    .EndClip()
                    .AddVerticalScrollbar((value) =>
                    {
                        trackListElem.Bounds.fixedY = 0 - value;
                        trackListElem.Bounds.CalcWorldBounds();
                    }, scrollbarBounds, "scrollbar")

                    .AddCustomRender(listBounds, (dt, bounds) =>
                    {
                        var seq = workspace.SelectedSequence;
                        if (seq == null) return;
                        double offx = GuiElement.scaled(SequenceTrackCellEntry.trackStartX);
                        capi.Render.Render2DTexture(playheadTex.TextureId, (float)(bounds.renderX + offx + seq.CurrentFrame / seq.Duration * (bounds.OuterWidth - offx)) - 1, (float)bounds.renderY, 3, (float)GuiElement.scaled(cellListHeight));
                    })
                .EndChildElements()
            ;
            trackListElem = Composers["tracks"].GetCellList<SequenceTrack>("tracksList");
            trackListElem.BeforeCalcBounds();
            trackListElem.UnscaledCellVerPadding = 0;
            trackListElem.unscaledCellSpacing = 5;

            frameSliderElem = Composers["tracks"].GetSlider("frameslider");
            frameSliderElem.ShowFillValue = false;

            Composers["tracks"].Compose();            
        }

        private void onPlay(bool obj)
        {
            if (workspace.SelectedSequence == null) return;
            workspace.SelectedSequence.Playing = true;
        }

        private void onPause(bool obj)
        {
            if (workspace.SelectedSequence == null) return;
            workspace.SelectedSequence.Playing = false;
        }

        private bool onSetFrame(int t)
        {
            if (workspace.SelectedSequence == null) return false;
            workspace.SelectedSequence.CurrentFrame = t / 1000f;

            modSystem.guiEditActor?.SetKeyFrame(null);
            workspace.SelectedSequence.SelectedKeyframe = null;
            modSystem.ReloadGizmoPosition();

            return true;
        }

        private void loadSequenceDropdownEntries()
        {
            sequenceEntries = new DropDownEntry[workspace.Sequences.Count];
            for (int i = 0; i < workspace.Sequences.Count; i++) sequenceEntries[i] = new DropDownEntry(i + "", workspace.Sequences[i].Name);
        }

        private void onSequenceSelected(string code, bool selected)
        {
            int index = code.ToInt();

            workspace.selectedSequenceIndex = index;

            actorListElem.ReloadCells(workspace.SelectedSequence.Actors);
            onActorSelected(-1);
            trackListElem.ReloadCells(workspace.SelectedSequence.Tracks);

            Composers["tracks"].GetSlider("frameslider").SetValues((int)(workspace.SelectedSequence.CurrentFrame * 1000), 0, (int)(workspace.SelectedSequence.Duration * 1000), 10, "ms");
            Composers["sequences"].GetNumberInput("duration").SetValue(workspace.SelectedSequence.Duration);

            workspace.SelectedSequence.SelectedKeyframe = null;
            workspace.SelectedSequence.SelectedClip = null;
        }

        private void onDurationChanged(string val)
        {
            var seq = workspace.SelectedSequence;
            float duration = val.ToFloat();
            if (seq == null || duration <= 0) return;

            seq.Duration = duration;
            seq.CurrentFrame = Math.Min(seq.CurrentFrame, seq.Duration);
            Composers["tracks"].GetSlider("frameslider").SetValues((int)(seq.CurrentFrame * 1000), 0, (int)(seq.Duration * 1000), 10, "ms");
            trackListElem.ReloadCells(seq.Tracks);
        }

        private bool OnCreateSequence()
        {
            var dlg = new GuiDialogCreateSequence(capi);
            dlg.TryOpen();
            dlg.OnClosed += () =>
            {
                if (!dlg.Saved) return;

                workspace.Sequences.Add(new Sequence()
                {
                    Name = dlg.SequenceName,
                    Position = capi.World.Player.Entity.Pos.XYZ,
                    Duration = dlg.Duration
                });

                loadSequenceDropdownEntries();
                Composers["sequences"].GetDropDown("sequencesList").SetList(sequenceEntries);

                Composers["sequences"].GetDropDown("sequencesList").SetSelectedIndex(workspace.Sequences.Count - 1);
                onSequenceSelected((workspace.Sequences.Count - 1) + "", true);
            };

            return true;
        }

        private bool OnDeleteSequence()
        {
            if (workspace.selectedSequenceIndex < 0) return false;

            new GuiDialogConfirm(capi, "Really delete this sequence?", (confirm) =>
            {
                if (!confirm) return;
                foreach (var actor in workspace.SelectedSequence.Actors) actor.MeshRef?.Dispose();
                workspace.Sequences.RemoveAt(workspace.selectedSequenceIndex);
                closeSequence();
                loadSequenceDropdownEntries();
                Composers["sequences"].GetDropDown("sequencesList").SetList(sequenceEntries);
            }).TryOpen();

            return true;
        }


        private void onActorSelected(int index)
        {
            workspace.SelectedSequence.SelectedActorIndex = index;
            workspace.SelectedSequence.SelectedKeyframe = null;
            var actor = workspace.SelectedSequence.SelectedActor;
            foreach (var val in actorListElem.elementCells) (val as ActorCellEntry).Selected = false;

            if (actor != null)
            {
                (actorListElem.elementCells[index] as ActorCellEntry).Selected = true;
            }

            OnActorSelected?.Invoke(actor);

            if (actor == null)
            {
                modSystem.guiEditActor?.TryClose();
                return;
            }

#if DEBUG
            modSystem.guiEditActor?.TryClose();
            modSystem.guiEditActor?.Dispose();
            modSystem.guiEditActor = null;
#endif

            if (modSystem.guiEditActor == null) 
            {
                modSystem.guiEditActor = new GuiDialogEditActor(capi, workspace, null);
                modSystem.guiEditActor.OnTrackChanged = onTrackChanged;
            }

            modSystem.guiEditActor.TryOpen();
            modSystem.guiEditActor.loadValues(actor);
        }

        private void onTrackChanged()
        {
            trackListElem.ReloadCells(workspace.SelectedSequence.Tracks);
        }

        private bool OnAddActor()
        {
            if (workspace.SelectedSequence == null)
            {
                capi.TriggerIngameError(this, "nosequence", Lang.Get("Create/Select a sequence first."));
                return false;
            }

            var dlg = new GuiDialogAddActor(capi, workspace);
            dlg.TryOpen();
            dlg.OnClosed += () =>
            {
                if (dlg.Saved)
                {
                    var pos = (capi.World.Player.CurrentBlockSelection?.Position.ToVec3d().Add(0, 2, 0) ?? capi.World.Player.Entity.Pos.AheadCopy(3).XYZ) - workspace.SelectedSequence.Position;

                    var actorType = dlg.SelectedActorType;
                    var newActor = new SceneActor() {
                        Id = workspace.NextActorId++,
                        Type = actorType,
                        TypeCode = actorType.Code,
                        Name = dlg.ActorName,
                        Transform = new ModelTransform() {
                            Translation = pos.ToVec3f()
                        },
                    };

                    workspace.SelectedSequence.Actors.Add(newActor);
                    actorListElem.ReloadCells(workspace.SelectedSequence.Actors);

                    SelectActor(workspace.SelectedSequence.Actors.Count - 1);
                }
            };

            return true;
        }

        private bool OnDeleteActor()
        {
            if (workspace.SelectedSequence?.SelectedActor == null) return true;

            new GuiDialogConfirm(capi, "Really delete this actor?", (confirm) =>
            {
                if (confirm)
                {
                    var actor = workspace.SelectedSequence.Actors[workspace.SelectedSequence.SelectedActorIndex];

                    workspace.SelectedSequence.Actors.RemoveAt(workspace.SelectedSequence.SelectedActorIndex);
                    actor.MeshRef?.Dispose();
                    workspace.SelectedSequence.SelectedActorIndex = -1;

                    workspace.SelectedSequence.Tracks.RemoveAll(track => track.ActorId == actor.Id);
                    workspace.SelectedSequence.SelectedKeyframe = null;
                    workspace.SelectedSequence.SelectedClip = null;

                    Composers["actors"].GetCellList<SceneActor>("actorsList").ReloadCells(workspace.SelectedSequence.Actors);
                    trackListElem.ReloadCells(workspace.SelectedSequence.Tracks);
                }
            }).TryOpen();
            
            return true;
        }

        private IGuiElementCell createActorCell(SceneActor actor, ElementBounds bounds)
        {
            bounds.fixedPaddingY = 0;
            var cellElem = new ActorCellEntry(capi, bounds, actor.Name, "", index => SelectActor(index == workspace.SelectedSequence.SelectedActorIndex ? -1 : index), 160, 200);
            return cellElem;
        }

        public void SelectActor(int index)
        {
            if (index >= workspace.SelectedSequence.Actors.Count) return;

            onActorSelected(index);
        }

        public void OnClickedWorld(MouseEvent args)
        {
            var seq = workspace.SelectedSequence;
            if (!IsOpened() || seq == null || args.Button != EnumMouseButton.Left || capi.Input.KeyboardKeyStateRaw[(int)GlKeys.AltLeft]) return;

            var ray = capi.Input.GetPickingRayFromMousePosition(args.X, args.Y);
            var actors = seq.Actors.Where(actor => actor.Bounds != null).ToArray();
            var boxes = actors.Select(seq.GetActorBounds).ToArray();
            int hit = GizmoRenderer.NearestHit(boxes, ray, new Vec3d[boxes.Length], new BlockFacing[boxes.Length]);
            SelectActor(hit < 0 ? -1 : seq.Actors.IndexOf(actors[hit]));
            args.Handled = true;
        }



        private IGuiElementCell createTrackCell(SequenceTrack mskeyframe, ElementBounds bounds)
        {
            bounds.fixedPaddingY = 0;
            var cellElem = new SequenceTrackCellEntry(capi, bounds, workspace.SelectedSequence, mskeyframe, onMouseDownOnKeyFrame, onMouseUpOnKeyFrame, onRightClick);
            return cellElem;
        }

        protected void onMouseDownOnKeyFrame(SequenceTrack track, double time, int keyframeindex)
        {
            SelectActor(workspace.SelectedSequence.Actors.IndexOf(a => a.Id == track.ActorId));

            workspace.SelectedSequence.SelectedKeyframe = keyframeindex < 0 ? null : track.KeyFrames[keyframeindex];
            workspace.SelectedSequence.SelectedClip = null;
            modSystem.guiEditActor?.SetKeyFrame(keyframeindex < 0 ? null : track.KeyFrames[keyframeindex]);
        }

        protected void onMouseUpOnKeyFrame(SequenceTrack track, double time, int keyframeindex, AnimationClip clip, bool wasDragging)
        {
            if (clip != null)
            {
                workspace.SelectedSequence.SelectedClip = clip;
                return;
            }
            if (wasDragging) return;
            if (time < 0 || time > workspace.SelectedSequence.Duration) return;

            if (keyframeindex < 0)
            {
                workspace.SelectedSequence.CurrentFrame = (float)time;
            } else
            {
                workspace.SelectedSequence.CurrentFrame = (float)track.KeyFrames[keyframeindex].Time;
            }
            modSystem.guiEditActor?.loadValues(workspace.SelectedSequence.SelectedActor);
            modSystem.ReloadGizmoPosition();
        }

        protected void onRightClick(SequenceTrack track, double time, int keyframeindex, AnimationClip clip, int x, int y)
        {
            onMouseDownOnKeyFrame(track, time, keyframeindex);
            var seq = workspace.SelectedSequence;
            var actor = seq.SelectedActor;
            var edit = modSystem.guiEditActor;
            var entries = new List<(string, Action)>();

            if (keyframeindex >= 0)
            {
                var kf = track.KeyFrames[keyframeindex];
                foreach (var entry in GuiDialogEditActor.interpolationEntries)
                {
                    var interp = Enum.Parse<EnumKeyFrameInterpolation>(entry.Value);
                    entries.Add((entry.Name + (kf.Interpolation == interp ? " (current)" : ""), () =>
                    {
                        kf.Interpolation = interp;
                        edit.loadValues(actor);
                        onTrackChanged();
                    }));
                }
                entries.Add(("Delete keyframe", () => edit.DeleteKeyFrame()));
            }
            else if (clip != null)
            {
                seq.SelectedClip = clip;
                double len = clip.EndTime - clip.StartTime;
                entries.Add(("Edit", () => edit.OpenAnimations(clip)));
                if (clip.EndTime < seq.Duration)
                {
                    entries.Add(("Duplicate", () => insertClip(track, clip, new AnimationClip()
                    {
                        Code = clip.Code,
                        Speed = clip.Speed,
                        Offset = clip.Offset,
                        StartTime = clip.EndTime,
                        EndTime = Math.Min(seq.Duration, clip.EndTime + len)
                    })));
                }
                float at = seq.CurrentFrame;
                if (at > clip.StartTime && at < clip.EndTime)
                {
                    entries.Add(("Split at playhead", () =>
                    {
                        insertClip(track, clip, new AnimationClip()
                        {
                            Code = clip.Code,
                            Speed = clip.Speed,
                            Offset = clip.Offset + at - (float)clip.StartTime,
                            StartTime = at,
                            EndTime = clip.EndTime
                        });
                        clip.EndTime = at;
                    }));
                }
                entries.Add(("Delete", () => deleteClip(track, clip)));
            }
            else
            {
                entries.Add(("Add keyframe here", () =>
                {
                    seq.CurrentFrame = (float)time;
                    edit.SetKeyFrame(null);
                    edit.CreateKeyFrame();
                }));
                if (actor.Type.ShapeAnimations?.Length > 0)
                {
                    entries.Add(("Add animation here", () =>
                    {
                        seq.CurrentFrame = (float)time;
                        edit.AddAnimation();
                    }));
                }
            }

            capi.Event.EnqueueMainThreadTask(() => new GuiDialogContextMenu(capi, x, y, entries.ToArray()).TryOpen(), "machinima-contextmenu");
        }

        private void insertClip(SequenceTrack track, AnimationClip after, AnimationClip clip)
        {
            track.Animations.Insert(track.Animations.IndexOf(after) + 1, clip);
            onTrackChanged();
        }

        private void deleteClip(SequenceTrack track, AnimationClip clip)
        {
            clip.Stop(workspace.SelectedSequence.Actors.First(a => a.Id == track.ActorId));
            track.Animations.Remove(clip);
            workspace.SelectedSequence.SelectedClip = null;
            onTrackChanged();
        }

        public override void OnKeyDown(KeyEvent args)
        {
            base.OnKeyDown(args);
            var seq = workspace.SelectedSequence;
            if (args.Handled || !focused || args.KeyCode != (int)GlKeys.Delete || seq == null) return;

            if (seq.SelectedClip != null) deleteClip(seq.Tracks.First(t => t.Animations.Contains(seq.SelectedClip)), seq.SelectedClip);
            else if (seq.SelectedKeyframe != null) modSystem.guiEditActor.DeleteKeyFrame();
            args.Handled = true;
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            closeSequence();
        }

        private void closeSequence()
        {
            workspace.selectedSequenceIndex = -1;
            OnActorSelected?.Invoke(null);
            modSystem.guiEditActor?.TryClose();
            actorListElem.ReloadCells([]);
            trackListElem.ReloadCells([]);
            Composers["sequences"].GetDropDown("sequencesList").SetSelectedIndex(-1);
        }

        public override void OnRenderGUI(float deltaTime)
        {
            if (workspace.SelectedSequence != null && IsOpened())
            {
                Composers["tracks"].GetSlider("frameslider").SetValue((int)(workspace.SelectedSequence.CurrentFrame * 1000));
                Composers["tracks"].GetDynamicText("time").SetNewText(workspace.SelectedSequence.CurrentFrame.ToString("0.00") + " s");
            }

            base.OnRenderGUI(deltaTime);
            if (focused && trackListElem.MouseOverCursor != null) MouseOverCursor = trackListElem.MouseOverCursor;
        }


        public override void OnKeyPress(KeyEvent args)
        {
            base.OnKeyPress(args);
            if (!args.Handled && IsOpened() && focused && args.KeyChar == ' ' && workspace.SelectedSequence != null)
            {
                workspace.SelectedSequence.Playing = !workspace.SelectedSequence.Playing;
                args.Handled = true;
            }
        }

        public override string ToggleKeyCombinationCode => null;
        public override bool UnregisterOnClose => true;

        public override void Dispose()
        {
            base.Dispose();
            playheadTex.Dispose();
        }
    }
}
