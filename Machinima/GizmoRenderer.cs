using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Vintagestory.MovieMaker
{
    public class GuiDialogMouseCapture : GuiDialog
    {
        public GuiDialogMouseCapture(ICoreClientAPI capi) : base(capi)
        {
        }

        public override string ToggleKeyCombinationCode => null;
        public override double InputOrder => 999; // Must be last

        public Action<MouseEvent> onMouseDown;
        public Action<MouseEvent> onMouseMove;
        public Action<MouseEvent> onMouseUp;

        public override void OnMouseDown(MouseEvent args)
        {
            onMouseDown(args);
        }

        public override void OnMouseMove(MouseEvent args)
        {
            onMouseMove(args);
        }

        public override void OnMouseUp(MouseEvent args)
        {
            onMouseUp(args);
        }

        public override bool ShouldReceiveMouseEvents() => true;
    }

    public class GizmoRenderer : IRenderer
    {
        protected ICoreClientAPI capi;
        protected int selectedAxis = -1; // -1: none, 0: X (red), 1: Y (green), 2: Z (blue)
        protected bool isDragging = false;
        protected float gizmoScale = 1f;
        protected float arrowLength = 0.5f;
        protected float coneSize = 0.25f;
        protected float arrowThickness = 0.04f;
        protected Matrixf mat = new Matrixf();
        protected MeshRef[] arrowMeshRefs = new MeshRef[3];
        protected Cuboidd[] arrowBoxes = new Cuboidd[3];
        protected Vec3d gizmoPos = Vec3d.Zero;
        public bool rebuildArrows = false;

        public double RenderOrder => 0.5;
        public int RenderRange => 999;

        public event Action<Vec3d>? OnGizmoMoved;
        public event Action<MouseEvent>? OnClickedWorld;

        public bool Enabled = true;

        protected Vec3d GizmoPosition {
            get { return gizmoPos; }
            set
            {
                gizmoPos = value;
                updateArrowBoxes();
            }
        }

        private void updateArrowBoxes()
        {
            arrowBoxes[0].Set(gizmoPos.X, gizmoPos.Y - arrowThickness, gizmoPos.Z - arrowThickness, gizmoPos.X + arrowLength + coneSize, gizmoPos.Y + arrowThickness, gizmoPos.Z + arrowThickness); 
            arrowBoxes[1].Set(gizmoPos.X - arrowThickness, gizmoPos.Y, gizmoPos.Z - arrowThickness, gizmoPos.X + arrowThickness, gizmoPos.Y + arrowLength + coneSize, gizmoPos.Z + arrowThickness);
            arrowBoxes[2].Set(gizmoPos.X - arrowThickness, gizmoPos.Y - arrowThickness, gizmoPos.Z, gizmoPos.X + arrowThickness, gizmoPos.Y + arrowThickness, gizmoPos.Z + arrowLength + coneSize);
        }

        

        public GizmoRenderer(ICoreClientAPI api, float arrowLength = 0.5f, float coneSize = 0.2f, float arrowThickness = 0.04f)
        {
            this.arrowLength = arrowLength;
            this.coneSize = coneSize;
            this.arrowThickness = arrowThickness;

            capi = api;
            arrowBoxes[0] = new Cuboidd();
            arrowBoxes[1] = new Cuboidd();
            arrowBoxes[2] = new Cuboidd();
            genArrows();

            // draws over everything, except rifts
            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT, "gizmo-renderer");

            // Doing it this way ensures that guis have priority over the gizmo
            var guiMouseCapture = new GuiDialogMouseCapture(capi);
            guiMouseCapture.onMouseUp = OnMouseUp;
            guiMouseCapture.onMouseMove = OnMouseMove;
            guiMouseCapture.onMouseDown = OnMouseDown;
            capi.Gui.RegisterDialog(guiMouseCapture);
        }

        public void OnMouseDown(MouseEvent args)
        {
            selectedAxis = Enabled ? getSelectedAxis(args.X, args.Y) : -1;
            isDragging = selectedAxis >= 0;

            if (isDragging)
            {
                args.Handled = true;
            }
            else
            {
                OnClickedWorld?.Invoke(args);
            }
        }

        public void OnMouseUp(MouseEvent args)
        {
            isDragging = false;
        }

        public void OnMouseMove(MouseEvent args)
        {
            if (!Enabled) return;
            if (!capi.Input.MouseButton.Left) return;

            if (isDragging && selectedAxis >= 0)
            {
                var arrowBox = arrowBoxes[selectedAxis];
                Ray ray = capi.Input.GetPickingRayFromMousePosition(args.X, args.Y);
                var hitPosInfinite = new Vec3d();
                if (AABBIntersectionTest.TestintersectionInfiniteFace(arrowBox, ray, hitOnBlockFace[selectedAxis], ref hitPosInfinite))
                {
                    var delta = hitPosInfinite - axisHitPosition[selectedAxis];
                    axisHitPosition[selectedAxis] = hitPosInfinite;

                    switch (selectedAxis)
                    {
                        case 0: delta.Y = 0; delta.Z = 0; break;
                        case 1: delta.X = 0; delta.Z = 0; break;
                        case 2: delta.X = 0; delta.Y = 0; break;
                    }

                    gizmoPos.Add(delta);
                    OnGizmoMoved?.Invoke(delta);

                    updateArrowBoxes();
                }
                args.Handled = true;
            }
        }


        private void genArrows()
        {
            arrowMeshRefs[0] = capi.Render.UploadMesh(genArrow(8, EnumAxis.X, arrowLength, arrowThickness, ColorUtil.ToRgba(255, 255, 0, 0)));
            arrowMeshRefs[1] = capi.Render.UploadMesh(genArrow(8, EnumAxis.Y, arrowLength, arrowThickness, ColorUtil.ToRgba(255, 0, 255, 0)));
            arrowMeshRefs[2] = capi.Render.UploadMesh(genArrow(8, EnumAxis.Z, arrowLength, arrowThickness, ColorUtil.ToRgba(255, 0, 0, 255)));
        }

        public void SetPosition(Vec3d position)
        {
            GizmoPosition.Set(position);
            updateArrowBoxes();
        }



        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (!Enabled) return;
            if (capi.HideGuis) return;

            var player = capi.World.Player;
            if (player == null) return;

            if (rebuildArrows)
            {
                genArrows();
                rebuildArrows = false;
            }

            if (!capi.Input.MouseButton.Left)
            {
                selectedAxis = getSelectedAxis(capi.Input.MouseX, capi.Input.MouseY);
            }

            var oldProg = capi.Render.CurrentActiveShader;
            oldProg?.Stop();

            IRenderAPI rapi = capi.Render;
            IStandardShaderProgram prog = (IStandardShaderProgram)rapi.GetEngineShader(EnumShaderProgram.Standard);
            prog.Use();

            prog.ApplyTexture= 0;

            rapi.GlDisableCullFace();
            rapi.GLDisableDepthTest();

            Vec3d camPos = player.Entity.CameraPos;

            mat.Identity();
            mat.Translate(GizmoPosition.X - camPos.X, GizmoPosition.Y - camPos.Y, GizmoPosition.Z - camPos.Z);
            mat.Scale(gizmoScale, gizmoScale, gizmoScale);

            prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
            prog.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);
            prog.UniformMatrix("modelMatrix", mat.Values);

            for (int i = 0; i < arrowMeshRefs.Length; i++)
            {
                prog.ExtraGlow = selectedAxis == i ? 255 : 0;
                rapi.RenderMesh(arrowMeshRefs[i]);
            }

            rapi.GLEnableDepthTest();
            rapi.GlEnableCullFace();

            prog.ApplyTexture = 1;

            prog.Stop();


            /*bool renderBoxes = true;
            if (renderBoxes)
            {
                GizmoPosition = GizmoPosition;
                renderBox(xArrowBox);
                renderBox(yArrowBox);
                renderBox(zArrowBox);
            }*/

            oldProg?.Use();
        }

        /*private void renderBox(Cuboidd box)
        {
            cubeWireFrame.Render(capi, box.MidX, box.MidY, box.MidZ, (float)box.Width, (float)box.Height, (float)box.Length, 1.6f, ColorUtil.WhiteArgbVec);
        }*/


        private MeshData genArrow(int segments, EnumAxis axis, float length, float radius, int color)
        {
            // Generate arrow mesh along Z-axis, then apply axis rotation
            MeshData meshData = new MeshData(40, 30);
            int baseIndex = 0;
            // Cylinder body
            for (int i = 0; i < segments; i++)
            {
                float angle1 = (float)(2 * Math.PI * i / segments);
                float angle2 = (float)(2 * Math.PI * (i + 1) / segments);

                float x1a = radius * (float)Math.Cos(angle1);
                float y1a = radius * (float)Math.Sin(angle1);

                float x1b = radius * (float)Math.Cos(angle2);
                float y1b = radius * (float)Math.Sin(angle2);

                meshData.AddVertex(x1a, y1a, 0, 0, 0, color);
                meshData.AddVertex(x1a, y1a, length, 0, 1, color);
                meshData.AddVertex(x1b, y1b, length, 1, 0, color);
                meshData.AddVertex(x1b, y1b, 0, 1, 1, color);

                int idx = i * 4;
                meshData.AddIndices(idx, idx + 1, idx + 2, idx, idx + 2, idx + 3);

                baseIndex = idx + 3;
            }

            baseIndex++;

            // Cone (arrow head)
                        
            // Add cone tip
            int tipIndex = baseIndex;
            meshData.AddVertex(0, 0, length + coneSize, 0.5f, 0.5f, color);
            
            // Create base circle and connect to tip
            for (int i = 0; i < segments; i++)
            {
                float angle1 = (float)(2 * Math.PI * i / segments);
                float angle2 = (float)(2 * Math.PI * (i + 1) / segments);

                float x1a = radius * (float)Math.Cos(angle1);
                float y1a = radius * (float)Math.Sin(angle1);

                float x1b = radius * (float)Math.Cos(angle2);
                float y1b = radius * (float)Math.Sin(angle2);

                meshData.AddVertex(x1a, y1a, length, 0, 0, color);
                meshData.AddVertex(x1b, y1b, length, 1, 0, color);

                int idx = baseIndex + 1 + i * 2;
                meshData.AddIndices(new int[] { tipIndex, idx, idx + 1 });
            }
            
            Matrixf m = new Matrixf();
            m.Identity();
            if (axis == EnumAxis.Y) m.RotateXDeg(-90);
            if (axis == EnumAxis.X) m.RotateYDeg(90);
            meshData.MatrixTransform(m.Values);

            return meshData;
        }

        Vec3d[] axisHitPosition = new Vec3d[3];
        BlockFacing[] hitOnBlockFace = new BlockFacing[3];

        public static int NearestHit(Cuboidd[] boxes, Ray ray, Vec3d[] hitPositions, BlockFacing[] faces)
        {
            int nearest = -1;
            double nearestDist = double.MaxValue;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (!AABBIntersectionTest.Testintersection(boxes[i], ray, ref hitPositions[i], out faces[i])) continue;
                double dist = hitPositions[i].SquareDistanceTo(ray.origin);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = i;
                }
            }
            return nearest;
        }

        private int getSelectedAxis(int mouseX, int mouseY)
        {
            if (capi.World.Player == null) return -1;
            return NearestHit(arrowBoxes, capi.Input.GetPickingRayFromMousePosition(mouseX, mouseY), axisHitPosition, hitOnBlockFace);
        }
        

        public void Dispose()
        {
            foreach (var meshRef in arrowMeshRefs)
            {
                meshRef?.Dispose();
            }
        }
    }
}
