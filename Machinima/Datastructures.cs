using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.ServerMods
{

    [JsonObject(MemberSerialization.OptIn)]
    public class MachinimaWorkSpace
    {
        [JsonProperty]
        public int NextActorId = 1;
        [JsonProperty]
        public List<Sequence> Sequences = new List<Sequence>();
        [JsonProperty]
        public int selectedSequenceIndex;
        [JsonProperty]
        public bool AutoKey = true;

        public Sequence SelectedSequence => Sequences.Count == 0 || selectedSequenceIndex < 0 ? null : Sequences[selectedSequenceIndex];

    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Sequence
    {
        [JsonProperty]
        public string Name;
        [JsonProperty]
        public List<SceneActor> Actors = new List<SceneActor>();
        [JsonProperty]
        public Vec3d Position;
        [JsonProperty]
        public List<SequenceTrack> Tracks = new List<SequenceTrack>();
        [JsonProperty]
        public float Duration = 10;
        [JsonProperty]
        public int SelectedActorIndex;
        [JsonProperty]
        public bool Visible = true;
        [JsonProperty]
        public bool Playing = false;
        [JsonProperty]
        public float CurrentFrame;

        public int Iteration;

        public SceneActor SelectedActor => Actors.Count == 0 || SelectedActorIndex < 0 ? null : Actors[SelectedActorIndex];
        public MovieSequenceElementKeyFrame SelectedKeyframe;
        public AnimationClip SelectedClip;

        public Vec3d GetActorPosition(int actorIndex)
        {
            if (actorIndex >= Actors.Count) return null;

            return GetActorPosition(Actors[actorIndex]);
        }

        public Vec3d GetActorPosition(SceneActor actor)
        {
            var tf = GetTransform(actor, CurrentFrame, out _).Translation;
            return Position.OffsetCopy(tf.X, tf.Y, tf.Z);
        }

        public static Matrixf GetMatrix(ModelTransform tf, Matrixf mat)
        {
            return mat
                .Identity()
                .Translate(tf.Origin.X + tf.Translation.X, tf.Origin.Y + tf.Translation.Y, tf.Origin.Z + tf.Translation.Z)
                .Scale(tf.ScaleXYZ.X, tf.ScaleXYZ.Y, tf.ScaleXYZ.Z)
                .RotateXDeg(tf.Rotation.X)
                .RotateYDeg(tf.Rotation.Y)
                .RotateZDeg(tf.Rotation.Z)
                .Translate(-tf.Origin.X, -tf.Origin.Y, -tf.Origin.Z);
        }

        public Cuboidd GetActorBounds(SceneActor actor)
        {
            return actor.Bounds.TransformedCopy(GetMatrix(GetTransform(actor, CurrentFrame, out _), new Matrixf()).Values).OffsetCopyDouble(Position);
        }

        double[] splineTimes = new double[4];
        double[] splinePoints = new double[4];
        ModelTransform[] splineTfs = new ModelTransform[4];

        public ModelTransform GetTransform(SceneActor actor, float time, out SequenceTrack track)
        {
            track = Tracks.FirstOrDefault(t => t.ActorId == actor.Id);
            var kfs = track?.KeyFrames;
            if (kfs == null || kfs.Count == 0) return actor.Transform;
            if (kfs.Count == 1) return kfs[0].Transform;

            int i1 = -1;
            while (i1 + 1 < kfs.Count && kfs[i1 + 1].Time <= time) i1++;

            for (int i = 0; i < 4; i++)
            {
                int idx = i1 - 1 + i;
                var kf = kfs[GameMath.Mod(idx, kfs.Count)];
                splineTimes[i] = Math.Min(kf.Time, Duration) + Math.Floor((double)idx / kfs.Count) * Duration;
                splineTfs[i] = kf.Transform;
            }

            if (splineTimes[1] == splineTimes[2]) return splineTfs[1];
            if (splineTimes[0] == splineTimes[1])
            {
                splineTfs[0] = splineTfs[1];
                splineTimes[0] = 2 * splineTimes[1] - splineTimes[2];
            }
            if (splineTimes[2] == splineTimes[3])
            {
                splineTfs[3] = splineTfs[2];
                splineTimes[3] = 2 * splineTimes[2] - splineTimes[1];
            }

            switch (kfs[GameMath.Mod(i1, kfs.Count)].Interpolation)
            {
                case EnumKeyFrameInterpolation.Step:
                    return splineTfs[1];
                case EnumKeyFrameInterpolation.Linear:
                    float l = (float)((time - splineTimes[1]) / (splineTimes[2] - splineTimes[1]));
                    return ModelTransform.Lerp(splineTfs[1], splineTfs[2], l);
            }

            return new ModelTransform()
            {
                Translation = new FastVec3f(spline(time, tf => tf.Translation.X), spline(time, tf => tf.Translation.Y), spline(time, tf => tf.Translation.Z)),
                Rotation = new FastVec3f(spline(time, tf => tf.Rotation.X), spline(time, tf => tf.Rotation.Y), spline(time, tf => tf.Rotation.Z)),
                Origin = new FastVec3f(spline(time, tf => tf.Origin.X), spline(time, tf => tf.Origin.Y), spline(time, tf => tf.Origin.Z)),
                ScaleXYZ = new FastVec3f(spline(time, tf => tf.ScaleXYZ.X), spline(time, tf => tf.ScaleXYZ.Y), spline(time, tf => tf.ScaleXYZ.Z))
            };
        }

        private float spline(double t, System.Func<ModelTransform, float> get)
        {
            for (int i = 0; i < 4; i++) splinePoints[i] = get(splineTfs[i]);
            return (float)GameMath.CPCatmullRomSplineLerp(t, splinePoints, splineTimes);
        }
    }


    public class SceneActorType
    {
        public string Name;
        /// <summary>
        /// Must be a unique identifier 
        /// </summary>
        public string Code;
        public CompositeShape Shape;
        public Dictionary<string, CompositeTexture> Textures;
        public string OutfitConfigFile;
        public Dictionary<string, string> Outfit;
        public string[] DisableElements;
        public int GlowLevel;

        /// <summary>
        /// Loaded from the shape file
        /// </summary>
        public Animation[] ShapeAnimations;

    }

    public class OutfitVariant
    {
        public AssetLocation Base;
        public Dictionary<string, AssetLocation> OverrideTextures;
        public string[] DisableElements;
    }

    public class OutfitConfig
    {
        public Dictionary<string, OutfitVariant> Variants;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class SceneActor
    {
        public SceneActorType Type;

        public MultiTextureMeshRef? MeshRef;

        [JsonProperty]
        public string TypeCode;

        [JsonProperty]
        public string Name;
        /// <summary>
        /// Unique number. Not reused.
        /// </summary>
        [JsonProperty]
        public int Id;
        [JsonProperty]
        public ModelTransform Transform;
        [JsonProperty]
        public string Animation;
        [JsonProperty]
        public int AnimationFrame;
        [JsonProperty]
        public bool Visible = true;
        [JsonProperty]
        public Dictionary<string, AnimationMetaData> activeAnimationsByAnimCode = new Dictionary<string, AnimationMetaData>();

        public ClientAnimator Animator;
        public Cuboidf Bounds;

        public ModelTransform GetTransform()
        {
            return Transform;
        }

        public void SetTransform(ModelTransform currentTransform)
        {
            Transform = currentTransform;
        }

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            if (Transform == null) Transform = new ModelTransform();
            Transform.EnsureDefaultValues();
        }
    }

    public enum EnumSequenceElementType
    {
        Camera,
        Actor
    }

    public enum EnumClipPart
    {
        None,
        Start,
        End,
        Body
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class MovieSequenceElementKeyFrame
    {
        [JsonProperty]
        public double Time;
        [JsonProperty]
        public ModelTransform Transform;
        [JsonProperty]
        public EnumKeyFrameInterpolation Interpolation = EnumKeyFrameInterpolation.Bezier;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class AnimationClip
    {
        [JsonProperty]
        public string Code = "";
        [JsonProperty]
        public double StartTime;
        [JsonProperty]
        public double EndTime;
        [JsonProperty]
        public float Speed = 1;
        [JsonProperty]
        public float Offset;

        public void Stop(SceneActor actor)
        {
            if (actor.activeAnimationsByAnimCode.Remove(Code)) actor.Animator.GetAnimationState(Code)?.Stop();
        }
    }


    [JsonObject(MemberSerialization.OptIn)]
    public class SequenceTrack
    {
        [JsonProperty]
        public EnumSequenceElementType Type;
        [JsonProperty]
        public List<MovieSequenceElementKeyFrame> KeyFrames = new List<MovieSequenceElementKeyFrame>();
        [JsonProperty]
        public List<AnimationClip> Animations = new List<AnimationClip>();
        [JsonProperty]
        public string Name { get; set; }
        [JsonProperty]
        public int ActorId;


        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            if (KeyFrames == null) KeyFrames = new List<MovieSequenceElementKeyFrame>();
        }
    }


}
