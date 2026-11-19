using System;
using System.Drawing.Imaging;
using System.Net.NetworkInformation;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace Vintagestory.MovieMaker
{

    // Milestone 1: Place actors in the scenes and move them around with some sort of mouse tool
    // Milestone 2: Actor persistence
    // Milestone 3: Add keyframes to actors
    public class ModSystemMachinima : ModSystem
    {
        protected GuiDialogMachinima? dlg;
        public GuiDialogEditActor guiEditActor;
        protected MachinimaWorkSpace workSpace;
        protected GizmoRenderer? gizmoRenderer;
        protected ActorsRenderer? actorRenderer;
        protected ICoreClientAPI api;

        public SceneActorType[] actorTypes;


        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            this.api = api;

            api.ChatCommands
                .Create("machinima")
                .BeginSub("start")
                    .WithArgs(api.ChatCommands.Parsers.Int("sequenceIndex"))
                    .HandleWith(cmdStart)
                .EndSub()
                .BeginSub("stop")
                    .WithArgs(api.ChatCommands.Parsers.Int("sequenceIndex"))
                    .HandleWith(cmdStop)
                .EndSub()
                .BeginSub("toggle")
                    .WithArgs(api.ChatCommands.Parsers.Int("sequenceIndex"))
                    .HandleWith(cmdToggle)
                .EndSub()
            ;

            gizmoRenderer = new GizmoRenderer(api);
            gizmoRenderer.Enabled = false;
            gizmoRenderer.OnGizmoMoved += GizmoRenderer_OnGizmoMoved;
            gizmoRenderer.OnClickedWorld += args => dlg?.OnClickedWorld(args);
            api.Input.InWorldAction += Input_InWorldAction;
            api.Input.RegisterHotKey("machinimalEditor", "Open/Close Machinima Editor", GlKeys.F9, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("machinimalEditor", (kc) =>
            {
#if DEBUG
                if (dlg != null)
                {
                    dlg.TryClose();
                    dlg.Dispose();
                    dlg = null;
                }
#endif

                ensureWorkSpaceLoaded();

                if (dlg == null)
                {
                    dlg = new GuiDialogMachinima(api, workSpace);
                    dlg.OnActorSelected = onActorSelected;
                    dlg.OnClosed += () =>
                    {
                        saveWorkspace(workSpace);
                        if (gizmoRenderer != null)
                        {
                            gizmoRenderer.Enabled = false;
                        }
                    };
                }

                dlg.Toggle();

                return true;
            });
        }

        private TextCommandResult cmdToggle(TextCommandCallingArgs args)
        {
            return setRunState(!workSpace.SelectedSequence.Playing, (int)args[0]);
        }

        private TextCommandResult cmdStop(TextCommandCallingArgs args)
        {
            return setRunState(false, (int)args[0]);
        }

        private TextCommandResult cmdStart(TextCommandCallingArgs args)
        {
            return setRunState(true, (int)args[0]);
        }

        TextCommandResult setRunState(bool playing, int sequenceIndex)
        {
            if (playing) ensureWorkSpaceLoaded();
            if (sequenceIndex < 0 || sequenceIndex >= workSpace.Sequences.Count)
            {
                return TextCommandResult.Error("Invalid sequence index");
            }
            workSpace.selectedSequenceIndex = sequenceIndex;

            workSpace.SelectedSequence.Playing = playing;
            return TextCommandResult.Success();
        }


        private void onActorSelected(SceneActor actor)
        {
            if (actor == null)
            {
                if (gizmoRenderer != null) gizmoRenderer.Enabled = false;
                return;
            }

            gizmoRenderer.Enabled = true;
            gizmoRenderer.SetPosition(workSpace.SelectedSequence.GetActorPosition(actor));
        }

        private void ensureWorkSpaceLoaded()
        {
            if (actorTypes == null)
            {
                actorTypes = api.Assets.TryGet("config/sceneactortypes.json").ToObject<SceneActorType[]>();

                foreach (var actorType in actorTypes)
                {
                    if (actorType.Shape == null) continue;
                    var shape = api.Assets.TryGet(actorType.Shape.Base.Clone().WithLocationPrefixOnce("shapes/").WithPathAppendixOnce(".json"))?.ToObject<Shape>();

                    if (shape != null)
                    {
                        actorType.ShapeAnimations = shape.Animations;
                    }

                    if (actorType.Textures != null)
                    {
                        foreach (var texture in actorType.Textures.Values)
                        {
                            texture.Bake(api.Assets);
                        }
                    }
                    else
                    {
                        actorType.Textures = new System.Collections.Generic.Dictionary<string, CompositeTexture>();
                    }
                }
            }

            if (workSpace == null)
            {
                workSpace = api.LoadModConfig<MachinimaWorkSpace>("moviemakerworkspace.json") ?? new MachinimaWorkSpace();

                foreach (var sequence in workSpace.Sequences)
                {
                    foreach (var actor in sequence.Actors)
                    {
                        actor.Type = Array.Find(actorTypes, t => t.Code == actor.TypeCode);
                    }
                }
            }

            if (actorRenderer == null)
            {
                actorRenderer = new ActorsRenderer(api, workSpace);
            }
        }


        public void ReloadGizmoPosition()
        {
            var actor = workSpace.SelectedSequence?.SelectedActor;
            if (actor != null) gizmoRenderer?.SetPosition(workSpace.SelectedSequence.GetActorPosition(actor));
        }

        private void GizmoRenderer_OnGizmoMoved(Vec3d delta)
        {
            guiEditActor.MoveBy(delta);
        }

        private void Input_InWorldAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (action == EnumEntityAction.InWorldLeftMouseDown && dlg?.IsOpened() == true && !api.Input.KeyboardKeyStateRaw[(int)GlKeys.AltLeft]) handled = EnumHandling.PreventDefault;
        }

        public override void Dispose()
        {
            dlg?.Dispose();
            guiEditActor?.Dispose();
            gizmoRenderer?.Dispose();
            actorRenderer?.Dispose();
        }

        private void saveWorkspace(MachinimaWorkSpace workspace)
        {
            api.StoreModConfig(workspace, "moviemakerworkspace.json");
        }
    }
}
