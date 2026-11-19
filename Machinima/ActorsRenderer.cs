using Cairo;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace Vintagestory.MovieMaker
{
    public class ActorsRenderer : IRenderer, ITexPositionSource
    {
        protected ICoreClientAPI api;
        protected MachinimaWorkSpace workspace;
        public bool Enabled = true;

        public ActorsRenderer(ICoreClientAPI api, MachinimaWorkSpace workspace)
        {
            this.api = api;
            this.workspace = workspace;
            api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "gizmo-renderer");
            api.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "machinima-rifts");
            riftMesh = api.Render.UploadMesh(QuadMeshUtil.GetQuad());
            api.Event.ReloadShader += loadRiftShader;
            loadRiftShader();
        }

        MeshRef riftMesh;
        IShaderProgram riftProg;
        float riftCounter;

        private bool loadRiftShader()
        {
            riftProg = api.Shader.NewShaderProgram();
            riftProg.VertexShader = api.Shader.NewShader(EnumShaderType.VertexShader);
            riftProg.FragmentShader = api.Shader.NewShader(EnumShaderType.FragmentShader);
            api.Shader.RegisterFileShaderProgram("rift", riftProg);
            return riftProg.Compile();
        }

        public double RenderOrder => 1;

        public int RenderRange => 1;

        public Size2i? AtlasSize => api.EntityTextureAtlas.Size;

        SceneActor nowTesselatingActor;
        Shape nowTesselatingShape;
        public TextureAtlasPosition? this[string textureCode]
        {
            get
            {
                IDictionary<string, CompositeTexture> textures = nowTesselatingActor.Type.Textures;
                AssetLocation texturePath = null;

                // Prio 1: Get from collectible textures
                if (textures != null && textures.TryGetValue(textureCode, out CompositeTexture tex))
                {
                    texturePath = tex.Baked.BakedName;
                }

                // Prio 2: Get from collectible textures, use "all" code
                if (textures != null && texturePath == null && textures.TryGetValue("all", out tex))
                {
                    texturePath = tex.Baked.BakedName;
                }

                // Prio 3: Get from currently tesselating shape
                if (texturePath == null)
                {
                    nowTesselatingShape?.Textures.TryGetValue(textureCode, out texturePath);
                }

                // Prio 4: The code is the path
                if (texturePath == null)
                {
                    texturePath = new AssetLocation(textureCode);
                }

                return getOrCreateTexPos(texturePath);
            }
        }

        protected TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
        {
            TextureAtlasPosition texpos = api.EntityTextureAtlas[texturePath];

            if (texpos == null)
            {
                bool ok = api.EntityTextureAtlas.GetOrInsertTexture(texturePath, out _, out texpos, null);

                if (!ok)
                {
                    api.World.Logger.Warning("For actor with shape {0}, no such texture {1} found.", nowTesselatingActor.Type.Shape, texturePath);
                    return api.BlockTextureAtlas.UnknownTexturePosition;
                }
            }

            return texpos;
        }

        public void Dispose()
        {
            riftMesh.Dispose();
            foreach (var seq in workspace.Sequences)
            {
                foreach (var actor in seq.Actors)
                {
                    actor.MeshRef?.Dispose();
                    actor.MeshRef = null;
                }
            }            
        }

        Matrixf mat = new Matrixf();

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (!Enabled) return;
            if (stage == EnumRenderStage.AfterBlit)
            {
                renderRifts(deltaTime);
                return;
            }
            
            if (workspace != null)
            {
                foreach (var seq in workspace.Sequences)
                {
                    if (seq.Playing)
                    {
                        if (seq.CurrentFrame + deltaTime > seq.Duration) seq.Iteration++;

                        seq.CurrentFrame = GameMath.Mod(seq.CurrentFrame + deltaTime, seq.Duration);
                    }
                }
            }

            IRenderAPI rapi = api.Render;
            var player = api.World.Player;
            IShaderProgram prog = rapi.GetEngineShader(EnumShaderProgram.Entityanimated);
            prog.Use();

            prog.Uniform("rgbaAmbientIn", rapi.AmbientColor);
            prog.Uniform("rgbaFogIn", rapi.FogColor);
            prog.Uniform("fogMinIn", rapi.FogMin);
            prog.Uniform("fogDensityIn", rapi.FogDensity);
            prog.Uniform("alphaTest", 0.05f);
            prog.Uniform("addRenderFlags", 0);
            prog.Uniform("entityId", 0);
            prog.Uniform("glitchFlicker", 0);
            prog.Uniform("frostAlpha", 0f);
            

            rapi.GlDisableCullFace();

            Vec3d camPos = player.Entity.CameraPos;
            prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
            prog.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);

            foreach (var sequence in workspace.Sequences)
            {
                if (!sequence.Visible) continue;
                if (camPos.SquareDistanceTo(sequence.Position) > 500 * 500) continue;

                var currentFrame = sequence.CurrentFrame;

                foreach (var actor in sequence.Actors)
                {
                    if (actor.Type.Shape == null) continue;
                    if (actor.MeshRef == null)
                    {
                        loadMeshRef(rapi, actor);
                    }
                    if (actor.MeshRef == null) continue;


                    renderActor(deltaTime, rapi, prog, sequence, currentFrame, actor);
                }
            }


            rapi.GlEnableCullFace();
            prog.Stop();
        }

        /// <summary>
        /// Mirrors RiftRenderer.OnRenderFrame in VSSurvivalMod. Avoids circular dependencies.
        /// </summary>
        private void renderRifts(float dt)
        {
            riftCounter = GameMath.Mod(riftCounter + dt, GameMath.TWOPI * 100f);
            IRenderAPI rapi = api.Render;
            Vec3d camPos = api.World.Player.Entity.CameraPos;

            rapi.GLDepthMask(false);
            riftProg.Use();
            riftProg.Uniform("rgbaFogIn", rapi.FogColor);
            riftProg.Uniform("fogMinIn", rapi.FogMin);
            riftProg.Uniform("fogDensityIn", rapi.FogDensity);
            riftProg.Uniform("rgbaAmbientIn", rapi.AmbientColor);
            riftProg.Uniform("rgbaLightIn", new Vec4f(1, 1, 1, 1));
            riftProg.BindTexture2D("primaryFb", rapi.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0], 0);
            riftProg.BindTexture2D("depthTex", rapi.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId, 1);
            riftProg.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
            riftProg.Uniform("counter", riftCounter);
            riftProg.Uniform("counterSmooth", 200 + (float)GameMath.Sin(api.InWorldEllapsedMilliseconds / 24000.0) * 100);
            riftProg.Uniform("invFrameSize", 1f / rapi.FrameWidth, 1f / rapi.FrameHeight);

            int riftIndex = 0;
            foreach (var sequence in workspace.Sequences)
            {
                if (!sequence.Visible) continue;
                if (camPos.SquareDistanceTo(sequence.Position) > 500 * 500) continue;

                foreach (var actor in sequence.Actors)
                {
                    if (actor.Type.Shape != null) continue;

                    var tf = sequence.GetTransform(actor, sequence.CurrentFrame, out _);
                    float dx = (float)(sequence.Position.X + tf.Translation.X - camPos.X);
                    float dy = (float)(sequence.Position.Y + tf.Translation.Y - camPos.Y);
                    float dz = (float)(sequence.Position.Z + tf.Translation.Z - camPos.Z);

                    mat.Identity();
                    mat.Translate(dx, dy, dz);
                    mat.ReverseMul(rapi.CameraMatrixOriginf);
                    mat.Values[0] = 1f;
                    mat.Values[1] = 0f;
                    mat.Values[2] = 0f;
                    mat.Values[8] = 0f;
                    mat.Values[9] = 0f;
                    mat.Values[10] = 1f;
                    mat.Scale(tf.ScaleXYZ.X, tf.ScaleXYZ.X, tf.ScaleXYZ.X);

                    riftProg.UniformMatrix("modelViewMatrix", mat.Values);
                    riftProg.Uniform("worldPos", new Vec4f(dx, dy, dz, 0));
                    riftProg.Uniform("riftIndex", ++riftIndex);
                    rapi.RenderMesh(riftMesh);

                    if (dx * dx + dy * dy + dz * dz < 40 * 40)
                    {
                        Vec3d ppos = sequence.Position.OffsetCopy(tf.Translation.X, tf.Translation.Y, tf.Translation.Z);
                        api.World.SpawnParticles(0.1f, ColorUtil.ColorFromRgba(21 / 2, 70 / 2, 116 / 2, 128), ppos, ppos, new Vec3f(-0.125f, -0.125f, -0.125f), new Vec3f(0.125f, 0.125f, 0.125f), 5, 0, (0.125f / 2 + (float)api.World.Rand.NextDouble() * 0.25f) / 2);
                    }
                }
            }

            riftProg.Stop();
            rapi.GLDepthMask(true);
        }

        private void renderActor(float dt, IRenderAPI rapi, IShaderProgram prog, Sequence sequence, float currentFrame, SceneActor actor)
        {
            Vec3d camPos = api.World.Player.Entity.CameraPos;
            var tf = sequence.GetTransform(actor, currentFrame, out var track);
            if (track != null) playClips(track, actor, currentFrame);

            var lightrgbs = api.World.BlockAccessor.GetLightRGBs(
                (int)Math.Floor(sequence.Position.X + tf.Translation.X),
                (int)Math.Floor(sequence.Position.Y + tf.Translation.Y),
                (int)Math.Floor(sequence.Position.Z + tf.Translation.Z)
            );
            prog.Uniform("rgbaLightIn", lightrgbs);

            if (actor.activeAnimationsByAnimCode.Count > 0 || actor.Animator.ActiveAnimationCount > 0)
            {
                actor.Animator.OnFrame(actor.activeAnimationsByAnimCode, dt);
            }


            Sequence.GetMatrix(tf, mat);
            mat.Values[12] += (float)(sequence.Position.X - camPos.X);
            mat.Values[13] += (float)(sequence.Position.Y - camPos.Y);
            mat.Values[14] += (float)(sequence.Position.Z - camPos.Z);
            prog.UniformMatrix("modelMatrix", mat.Values);

            bool isSelected = workspace.SelectedSequence != null && actor == workspace.SelectedSequence?.SelectedActor;

            prog.Uniform("extraGlow", isSelected ? 196 : actor.Type.GlowLevel);
            prog.Uniform("renderColor", isSelected ? new Vec4f(0, 1, 0, 1) : new Vec4f(1, 1, 1, 1));

            prog.UBOs["Animation"].Update(actor.Animator.Matrices, 0, actor.Animator.MaxJointId * 16 * 4);

            rapi.RenderMultiTextureMesh(actor.MeshRef, "entityTex");
        }



        protected void loadMeshRef(IRenderAPI rapi, SceneActor actor)
        {
            var shape = api.Assets.TryGet(actor.Type.Shape.Base.Clone().WithLocationPrefixOnce("shapes/").WithPathAppendixOnce(".json"))?.ToObject<Shape>();
            if (shape != null)
            {
                applyOverlaysAndOutfit(shape, actor.Type);
                var elementsByName = shape.CollectAndResolveReferences(api.World.Logger, "actor shape " + actor.Type.Shape.Base);
                shape.CacheInvTransforms();
                shape.ResolveAndFindJoints(api.World.Logger, "actor shape " + actor.Type.Shape.Base, elementsByName);

                nowTesselatingActor = actor;
                nowTesselatingShape = shape;

                TesselationMetaData meta = new TesselationMetaData()
                {
                    TexSource = this,
                    WithJointIds = true,
                    WithDamageEffect = true, // Required in the shader
                    TypeForLogging = "actor shape file " + actor.Type.Shape.Base,
                    Rotation = new Vec3f(0, 0, 0)
                };
                api.Tesselator.TesselateShape(meta, shape, out var meshdata);

                var xyz = meshdata.xyz;
                actor.Bounds = new Cuboidf(xyz[0], xyz[1], xyz[2], xyz[0], xyz[1], xyz[2]);
                for (int i = 3; i < meshdata.VerticesCount * 3; i += 3)
                {
                    actor.Bounds.X1 = Math.Min(actor.Bounds.X1, xyz[i]);
                    actor.Bounds.Y1 = Math.Min(actor.Bounds.Y1, xyz[i + 1]);
                    actor.Bounds.Z1 = Math.Min(actor.Bounds.Z1, xyz[i + 2]);
                    actor.Bounds.X2 = Math.Max(actor.Bounds.X2, xyz[i]);
                    actor.Bounds.Y2 = Math.Max(actor.Bounds.Y2, xyz[i + 1]);
                    actor.Bounds.Z2 = Math.Max(actor.Bounds.Z2, xyz[i + 2]);
                }

                for (int i = 0; i < meshdata.FlagsCount; i++)
                {
                    meshdata.Flags[i] &= ~VertexFlags.WindModeBitsMask;
                }

                actor.MeshRef = rapi.UploadMultiTextureMesh(meshdata);


                if (shape.Animations is Animation[] Animations)
                {
                    for (int i = 0; i < Animations.Length; i++)
                    {
                        Animations[i].GenerateAllFrames(shape.Elements, shape.JointsById, "actor shape " + actor.Type.Shape.Base);
                    }
                }

                actor.Animator = new ClientAnimator(() => 1, shape.Animations, shape.Elements, shape.JointsById, null, null);
            }
        }

        private void playClips(SequenceTrack track, SceneActor actor, float currentFrame)
        {
            var clips = track.Animations;
            bool inWindow(AnimationClip c) => currentFrame >= c.StartTime && currentFrame < c.EndTime;
            bool active(int i) => inWindow(clips[i]) && !clips.Take(i).Any(c => c.Code == clips[i].Code && inWindow(c));

            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                if (clips.Any(c => c.Code == clip.Code && inWindow(c))) continue;
                clip.Stop(actor);
            }

            float weightSum = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                if (active(i)) weightSum += clipWeight(clips, clips[i], currentFrame);
            }

            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                if (!active(i)) continue;

                var animState = actor.Animator.GetAnimationState(clip.Code);
                if (animState == null) continue;

                float frame = (float)(currentFrame - clip.StartTime + clip.Offset) * clip.Speed * 30;
                int quantityFrames = animState.Animation.QuantityFrames;
                if (animState.Animation.OnAnimationEnd == EnumEntityAnimationEndHandling.Repeat) animState.CurrentFrame = GameMath.Mod(frame, quantityFrames);
                else animState.CurrentFrame = GameMath.Clamp(frame, 0, quantityFrames - 0.003f);
                animState.BlendedWeight = weightSum > 0 ? clipWeight(clips, clip, currentFrame) / weightSum : 1;

                if (!actor.activeAnimationsByAnimCode.ContainsKey(clip.Code))
                {
                    actor.activeAnimationsByAnimCode[clip.Code] = new AnimationMetaData()
                    {
                        Code = clip.Code,
                        Animation = clip.Code,
                        AnimationSpeed = 0,
                        StartFrameOnce = animState.CurrentFrame
                    };
                }
            }
        }

        private float clipWeight(List<AnimationClip> clips, AnimationClip clip, float t)
        {
            double fadeIn = 0, fadeOut = 0;
            foreach (var other in clips)
            {
                if (other == clip) continue;
                if (other.StartTime < clip.StartTime && other.EndTime > clip.StartTime) fadeIn = Math.Max(fadeIn, Math.Min(other.EndTime, clip.EndTime) - clip.StartTime);
                if (other.StartTime < clip.EndTime && other.EndTime > clip.EndTime) fadeOut = Math.Max(fadeOut, clip.EndTime - Math.Max(other.StartTime, clip.StartTime));
            }

            float w = 1;
            if (fadeIn > 0) w = Math.Min(w, (float)((t - clip.StartTime) / fadeIn));
            if (fadeOut > 0) w = Math.Min(w, (float)((clip.EndTime - t) / fadeOut));
            return w;
        }

        private void applyOverlaysAndOutfit(Shape shape, SceneActorType type)
        {
            foreach (var overlay in type.Shape.Overlays ?? [])
            {
                stepParent(shape, overlay.Base, "");
            }

            if (type.Outfit != null)
            {
                var variants = api.Assets.TryGet("config/" + type.OutfitConfigFile + ".json").ToObject<OutfitConfig>().Variants;

                foreach (var val in type.Outfit)
                {
                    var variant = variants[val.Value];
                    if (variant.Base != null) stepParent(shape, variant.Base, val.Key + "-");
                    if (variant.DisableElements != null) shape.RemoveElements(variant.DisableElements);
                    if (variant.OverrideTextures != null)
                    {
                        foreach (var tex in variant.OverrideTextures)
                        {
                            shape.Textures[tex.Key] = tex.Value;
                        }
                    }
                }
            }

            if (type.DisableElements != null) shape.RemoveElements(type.DisableElements);
        }

        private void stepParent(Shape shape, AssetLocation loc, string prefix)
        {
            var sloc = loc.Clone().WithLocationPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            var shapefile = api.Assets.TryGet(sloc);
            if (shapefile == null)
            {
                api.Logger.Warning("Machinima tool: Step parent Shape file not found " + sloc);
                return;
            }
            var childShape = shapefile.ToObject<Shape>();
            shape.StepParentShape(childShape, prefix, loc.ToShortString(), "", api.World.Logger, (code, texLoc) => shape.Textures.TryAdd(prefix + code, texLoc));
        }
    }
}
