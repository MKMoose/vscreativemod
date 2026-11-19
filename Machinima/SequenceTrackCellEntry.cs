using Cairo;
using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace Vintagestory.MovieMaker
{
    public class SequenceTrackCellEntry : GuiElement, IGuiElementCell
    {
        protected SequenceTrack track;
        protected int selectedKeyframeIndex = -1;
        protected int hoveringOnKeyframeIndex = -1;

        protected bool isDraggingKeyframe = false;
        protected bool didDragKeyframe = false;
        protected AnimationClip draggingClip;
        protected EnumClipPart draggingClipPart;
        protected double draggingClipOffset;

        protected const int keyframeSize = 12;
        protected const int trackHeight = 30;
        public const int trackStartX = 140;

        protected LoadedTexture keyFrameIcon;
        protected double unScaledCellHeight = 50;
        protected bool composed = false;
        protected LoadedTexture bgTexture;
        protected LoadedTexture hoverTexture;
        protected double hoverTextureTime = -1;
        protected AnimationClip drawnSelectedClip;
        protected GuiElementRichtext nameTextElem;

        protected Sequence sequence;


        protected float iconOffsy = 2;
        protected Action<SequenceTrack, double, int> mouseDownOnKeyframe;
        protected Action<SequenceTrack, double, int, AnimationClip, bool> mouseUpOnKeyframe;
        protected Action<SequenceTrack, double, int, AnimationClip, int, int> rightClick;
        ElementBounds IGuiElementCell.Bounds => Bounds;

        public SequenceTrackCellEntry(ICoreClientAPI capi, ElementBounds bounds, Sequence sequence, SequenceTrack trackData, Action<SequenceTrack, double, int> mouseDownOnKeyframe, Action<SequenceTrack, double, int, AnimationClip, bool> mouseUpOnKeyFrame, Action<SequenceTrack, double, int, AnimationClip, int, int> rightClick) : base(capi, bounds)
        {
            this.track = trackData;
            this.sequence = sequence;
            this.mouseDownOnKeyframe = mouseDownOnKeyframe;
            this.mouseUpOnKeyframe = mouseUpOnKeyFrame;
            this.rightClick = rightClick;

            keyFrameIcon = capi.Gui.LoadSvg(new AssetLocation("textures/icons/keyframe.svg"), 24, 24, 24, 24);

            bgTexture = new LoadedTexture(capi);
            hoverTexture = new LoadedTexture(capi);

            CairoFont font = CairoFont.WhiteDetailText();
            double offY = (trackHeight - font.UnscaledFontsize) / 2 + iconOffsy;
            var nameTextBounds = ElementBounds.Fixed(0, offY, 150, 25).WithParent(Bounds);
            nameTextElem = new GuiElementRichtext(capi, VtmlUtil.Richtextify(capi, trackData.Name, CairoFont.WhiteDetailText().WithFontSize(12)), nameTextBounds);
        }

        private double TimeToPixelPosX(double time)
        {
            var offx = scaled(trackStartX);
            return Bounds.renderX + offx + time / sequence.Duration * (Bounds.OuterWidth-offx);
        }

        private double PixelPosXToTime(double pixelX)
        {
            var offx = scaled(trackStartX);
            return (pixelX - Bounds.renderX - offx) / (Bounds.InnerWidth - offx) * sequence.Duration;
        }

        private bool IsPointInKeyframe(double pixelX, double pixelY, double keyframeTime)
        {
            double kfX = TimeToPixelPosX(keyframeTime);
            double kfY = Bounds.renderY + scaled(trackHeight) / 2.0 + scaled(iconOffsy);
            double distance = Math.Sqrt(Math.Pow(pixelX - kfX, 2) + Math.Pow(pixelY - kfY, 2));
            return distance <= keyframeSize;
        }


        public void Recompose()
        {
            composed = true;
            drawnSelectedClip = sequence.SelectedClip;
            ImageSurface surface = new ImageSurface(Format.Argb32, (int)Bounds.InnerWidth, (int)Bounds.InnerHeight);
            Context ctx = genContext(surface);
            
            ctx.Rectangle(scaled(trackStartX), scaled(trackHeight / 2.0 + iconOffsy - 3), Bounds.InnerWidth, scaled(6));
            ctx.SetSourceColor(new Cairo.Color(0.1, 0.1, 0.1, 1));
            ctx.Fill();

            CairoFont.WhiteDetailText().WithFontSize(10).SetupContext(ctx);
            var anims = sequence.Actors.First(a => a.Id == track.ActorId).Type.ShapeAnimations;
            double barY = scaled(trackHeight + 2);
            double barHeight = scaled(14);
            foreach (var clip in track.Animations)
            {
                double x1 = TimeToPixelPosX(clip.StartTime) - Bounds.renderX;
                double x2 = TimeToPixelPosX(clip.EndTime) - Bounds.renderX;
                ctx.Save();
                ctx.Rectangle(x1, barY, x2 - x1, barHeight);
                ctx.Clip();
                ctx.SetSourceColor(new Cairo.Color(0.25, 0.45, 0.8, 0.6));
                ctx.Paint();
                var anim = anims?.FirstOrDefault(a => a.Code == clip.Code);
                double loopLen = anim == null ? 0 : anim.QuantityFrames / 30.0 / clip.Speed;
                ctx.SetSourceColor(new Cairo.Color(0, 0, 0, 0.5));
                for (double t = clip.StartTime + loopLen - clip.Offset % loopLen; loopLen > 0 && t < clip.EndTime; t += loopLen)
                {
                    ctx.Rectangle(TimeToPixelPosX(t) - Bounds.renderX, barY, scaled(1), barHeight);
                    ctx.Fill();
                }
                ctx.SetSourceColor(new Cairo.Color(1, 1, 1, 1));
                ctx.MoveTo(x1 + scaled(3), barY + barHeight - scaled(3));
                ctx.ShowText(clip.Code);
                if (clip == sequence.SelectedClip)
                {
                    ctx.LineWidth = scaled(2);
                    ctx.Rectangle(x1 + scaled(1), barY + scaled(1), x2 - x1 - scaled(2), barHeight - scaled(2));
                    ctx.Stroke();
                }
                ctx.Restore();
            }

            generateTexture(surface, ref bgTexture);
            ctx.Dispose();
            surface.Dispose();

            nameTextElem.Compose();
        }


        protected Vec4f selectedColor = new Vec4f(0, 1, 0, 1);
        protected Vec4f linearColor = new Vec4f(1, 0.8f, 0.2f, 1);
        protected Vec4f constantColor = new Vec4f(1, 0.3f, 0.3f, 1);

        public void OnRenderInteractiveElements(ICoreClientAPI api, float deltaTime)
        {
            if (!composed || drawnSelectedClip != sequence.SelectedClip) Recompose();

            var part = draggingClip != null ? draggingClipPart : clipPartAt(api.Input.MouseX, api.Input.MouseY, out _);
            if (part == EnumClipPart.None) MouseOverCursor = null;
            else if (part == EnumClipPart.Body) MouseOverCursor = "move";
            else MouseOverCursor = "trim";

            nameTextElem.RenderColor = sequence.SelectedActor?.Id == track.ActorId ? selectedColor : null;
            nameTextElem.RenderInteractiveElements(deltaTime);

            api.Render.Render2DTexturePremultipliedAlpha(bgTexture.TextureId, Bounds.renderX, Bounds.renderY, Bounds.OuterWidth, Bounds.OuterHeight);

            if (track.KeyFrames == null) return;

            for (int i = 0; i < track.KeyFrames.Count; i++)
            {
                var kf = track.KeyFrames[i];
                double kfX = TimeToPixelPosX(kf.Time) + scaled(1);
                double kfY = Bounds.renderY + scaled(trackHeight) / 2.0 + scaled(iconOffsy);
                Vec4f? col = null;
                if (sequence.SelectedKeyframe == kf) col = selectedColor;
                else if (kf.Interpolation == EnumKeyFrameInterpolation.Linear) col = linearColor;
                else if (kf.Interpolation == EnumKeyFrameInterpolation.Step) col = constantColor;

                if (i == hoveringOnKeyframeIndex)
                {
                    var zoom = (int)scaled(4);
                    api.Render.Render2DTexture(keyFrameIcon.TextureId, (float)kfX - keyFrameIcon.Width / 2 - zoom / 2, (float)kfY - keyFrameIcon.Height / 2 - zoom / 2, keyFrameIcon.Width + zoom, keyFrameIcon.Height + zoom, 50, col);
                }
                else
                {
                    api.Render.Render2DTexture(keyFrameIcon.TextureId, (float)kfX - keyFrameIcon.Width / 2, (float)kfY - keyFrameIcon.Height / 2, keyFrameIcon.Width, keyFrameIcon.Height, 50, col);
                }
            }

            if (hoveringOnKeyframeIndex >= 0)
            {
                var kf = track.KeyFrames[hoveringOnKeyframeIndex];
                if (kf.Time != hoverTextureTime)
                {
                    hoverTextureTime = kf.Time;
                    api.Gui.TextTexture.GenOrUpdateTextTexture(kf.Time.ToString("0.00") + "s", CairoFont.WhiteDetailText(), ref hoverTexture, new TextBackground() { FillColor = GuiStyle.DialogDefaultBgColor, Padding = 3 });
                }
                double kfY = Bounds.renderY + scaled(trackHeight) / 2.0 + scaled(iconOffsy);
                api.Render.Render2DTexturePremultipliedAlpha(hoverTexture.TextureId, TimeToPixelPosX(kf.Time) + scaled(14), kfY - hoverTexture.Height / 2.0, hoverTexture.Width, hoverTexture.Height, 60);
            }
        }

        public void UpdateCellHeight()
        {
            nameTextElem.BeforeCalcBounds();

            Bounds.CalcWorldBounds();
            Bounds.fixedHeight = unScaledCellHeight;
        }

        public void OnMouseUpOnElement(MouseEvent args, int elementIndex)
        {
            mouseUpOnKeyframe?.Invoke(track, PixelPosXToTime(args.X), selectedKeyframeIndex, draggingClip, didDragKeyframe);
            isDraggingKeyframe = false;
            draggingClip = null;
            didDragKeyframe = false;
        }

        public void OnMouseDownOnElement(MouseEvent args, int elementIndex)
        {
            if (args.Button == EnumMouseButton.Right)
            {
                if (args.X < Bounds.renderX + scaled(trackStartX)) return;
                clipPartAt(args.X, args.Y, out var clip);
                rightClick?.Invoke(track, PixelPosXToTime(args.X), GetKeyFrameAtPixelPosition(args.X, args.Y), clip, args.X, args.Y);
                return;
            }

            selectedKeyframeIndex = GetKeyFrameAtPixelPosition(args.X, args.Y);
            isDraggingKeyframe = selectedKeyframeIndex >= 0;

            if (selectedKeyframeIndex < 0)
            {
                draggingClipPart = clipPartAt(args.X, args.Y, out draggingClip);
                if (draggingClip != null) draggingClipOffset = PixelPosXToTime(args.X) - draggingClip.StartTime;
            }

            mouseDownOnKeyframe?.Invoke(track, PixelPosXToTime(args.X), selectedKeyframeIndex);
        }

        public void OnMouseMoveOnElement(MouseEvent args, int elementIndex)
        {
            hoveringOnKeyframeIndex = GetKeyFrameAtPixelPosition(args.X, args.Y);
            if (!api.Input.MouseButton.Left)
            {
                draggingClip = null;
                isDraggingKeyframe = false;
            }

            if (draggingClip != null)
            {
                didDragKeyframe = true;
                double newTime = Math.Max(0, Math.Min(sequence.Duration, PixelPosXToTime(args.X)));
                if (draggingClipPart == EnumClipPart.Start) draggingClip.StartTime = Math.Min(draggingClip.EndTime, newTime);
                if (draggingClipPart == EnumClipPart.End) draggingClip.EndTime = Math.Max(draggingClip.StartTime, newTime);
                if (draggingClipPart == EnumClipPart.Body)
                {
                    double len = draggingClip.EndTime - draggingClip.StartTime;
                    draggingClip.StartTime = Math.Max(0, Math.Min(sequence.Duration - len, newTime - draggingClipOffset));
                    draggingClip.EndTime = draggingClip.StartTime + len;
                }
                composed = false;
            }

            if (isDraggingKeyframe && selectedKeyframeIndex >= 0)
            {
                didDragKeyframe = true;
                double newTime = PixelPosXToTime(args.X);
                newTime = Math.Max(0, Math.Min(sequence.Duration, newTime));
                track.KeyFrames[selectedKeyframeIndex].Time = newTime;
                track.KeyFrames.Sort((a, b) => a.Time.CompareTo(b.Time));

                for (int i = 0; i < track.KeyFrames.Count; i++)
                {
                    if (Math.Abs(track.KeyFrames[i].Time - newTime) < 0.01)
                    {
                        selectedKeyframeIndex = i;
                        break;
                    }
                }
            }
        }

        protected EnumClipPart clipPartAt(int x, int y, out AnimationClip clip)
        {
            clip = null;
            if (!Bounds.PointInside(x, y) || y < Bounds.renderY + scaled(trackHeight)) return EnumClipPart.None;

            foreach (var c in track.Animations)
            {
                double startX = TimeToPixelPosX(c.StartTime);
                double endX = TimeToPixelPosX(c.EndTime);
                EnumClipPart part;
                if (Math.Abs(x - startX) <= scaled(8)) part = EnumClipPart.Start;
                else if (Math.Abs(x - endX) <= scaled(8)) part = EnumClipPart.End;
                else if (x > startX && x < endX) part = EnumClipPart.Body;
                else continue;

                clip = c;
                return part;
            }

            return EnumClipPart.None;
        }

        protected int GetKeyFrameAtPixelPosition(int x, int y)
        {
            for (int i = 0; i < track.KeyFrames.Count; i++)
            {
                if (IsPointInKeyframe(x, y, track.KeyFrames[i].Time))
                {
                    return i;
                }
            }

            return -1;
        }

        public override void Dispose()
        {
            base.Dispose();
            bgTexture?.Dispose();
            hoverTexture.Dispose();
            keyFrameIcon.Dispose();
            nameTextElem.Dispose();
        }
    }
}
