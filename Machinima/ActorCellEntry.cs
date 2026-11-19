using Cairo;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.MovieMaker
{
    public class ActorCellEntry : GuiElement, IGuiElementCell
    {
        public bool Visible => true;
        ElementBounds IGuiElementCell.Bounds => this.Bounds;
        public LoadedTexture hoverTexture;
        double unScaledCellHeight = 35;
        GuiElementRichtext nameTextElem;
        GuiElementRichtext detailTextElem;
        bool composed = false;
        public bool Selected;
        Action<int> onClick;

        public ActorCellEntry(ICoreClientAPI capi, ElementBounds bounds, string name, string detail, Action<int> onClick, float leftColWidth = 200, float rightColWidth = 300) : base(capi, bounds)
        {
            this.onClick = onClick;

            CairoFont font = CairoFont.WhiteDetailText();
            double offY = (unScaledCellHeight - font.UnscaledFontsize) / 2;

            var nameTextBounds = ElementBounds.Fixed(0, offY, leftColWidth, 25).WithParent(Bounds);
            var activitiesBounds = ElementBounds.Fixed(0, offY, rightColWidth, 25).WithParent(Bounds).FixedRightOf(nameTextBounds, 10);

            nameTextElem = new GuiElementRichtext(capi, VtmlUtil.Richtextify(capi, name, font), nameTextBounds);
            detailTextElem = new GuiElementRichtext(capi, VtmlUtil.Richtextify(capi, detail, font), activitiesBounds);

            hoverTexture = new LoadedTexture(capi);
        }

        public void Recompose()
        {
            composed = true;

            nameTextElem.Compose();
            detailTextElem.Compose();

            ImageSurface surface = new ImageSurface(Format.Argb32, (int)2, (int)2);
            Context ctx = genContext(surface);
            ctx.NewPath();
            ctx.LineTo(0, 0);
            ctx.LineTo(2, 0);
            ctx.LineTo(2, 2);
            ctx.LineTo(0, 2);
            ctx.ClosePath();
            ctx.SetSourceRGBA(0, 0, 0, 0.15);
            ctx.Fill();
            generateTexture(surface, ref hoverTexture);
            ctx.Dispose();
            surface.Dispose();
        }


        public void OnRenderInteractiveElements(ICoreClientAPI api, float deltaTime)
        {
            if (!composed) Recompose();

            nameTextElem.RenderInteractiveElements(deltaTime);
            detailTextElem.RenderInteractiveElements(deltaTime);

            // 5. Hover overlay
            int dx = api.Input.MouseX;
            int dy = api.Input.MouseY;
            Vec2d pos = Bounds.PositionInside(dx, dy);

            if (Selected || (pos != null && IsPositionInside(api.Input.MouseX, api.Input.MouseY)))
            {
                api.Render.Render2DTexturePremultipliedAlpha(hoverTexture.TextureId, Bounds.absX, Bounds.absY, Bounds.OuterWidth, Bounds.OuterHeight);
                if (Selected)
                {
                    api.Render.Render2DTexturePremultipliedAlpha(hoverTexture.TextureId, Bounds.absX, Bounds.absY, Bounds.OuterWidth, Bounds.OuterHeight);
                }
            }
        }


        public void UpdateCellHeight()
        {
            Bounds.CalcWorldBounds();
            nameTextElem.BeforeCalcBounds();
            detailTextElem.BeforeCalcBounds();
            Bounds.fixedHeight = unScaledCellHeight;
        }

        public void OnMouseUpOnElement(MouseEvent args, int elementIndex)
        {
            int x = api.Input.MouseX;
            int y = api.Input.MouseY;
            if (!args.Handled)
            {
                onClick?.Invoke(elementIndex);
            }
        }
        public override void Dispose()
        {
            nameTextElem.Dispose();
            detailTextElem.Dispose();
            hoverTexture?.Dispose();
        }

        public void OnMouseDownOnElement(MouseEvent args, int elementIndex) { }

        public void OnMouseMoveOnElement(MouseEvent args, int elementIndex) { }
    }
}
