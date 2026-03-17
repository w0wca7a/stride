using System;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Buffer = Stride.Graphics.Buffer;

namespace MorphTargetSample
{
    /// <summary>
    /// Two-morph-target demo.
    /// Press 1 to toggle target 0 (shift X), press 2 to toggle target 1 (shift Y).
    /// +/- adjusts the last toggled target.
    /// </summary>
    public class MorphTargetScript : SyncScript
    {
        private ModelComponent mc;
        private float weight0;
        private float weight1;
        private int lastToggled;
        private bool ready;

        public override void Start()
        {
            mc = Entity.Get<ModelComponent>();
            if (mc?.Model == null || mc.Model.Meshes.Count == 0) return;

            EnsureMorphTargetRenderFeature();

            var mesh = mc.Model.Meshes[0];
            var n = mesh.Draw.VertexBuffers[0].Count;

            // Target 0: shift +0.3 on X
            var d0 = new float[n * 4];
            for (int v = 0; v < n; v++) d0[v * 4] = 0.3f;

            // Target 1: shift +0.3 on Y
            var d1 = new float[n * 4];
            for (int v = 0; v < n; v++) d1[v * 4 + 1] = 0.3f;

            var buf0 = Buffer.Vertex.New(GraphicsDevice, d0);
            var buf1 = Buffer.Vertex.New(GraphicsDevice, d1);
            var decl0 = new VertexDeclaration(new VertexElement("MORPHDELTA", 0, PixelFormat.R32G32B32A32_Float, 0));
            var decl1 = new VertexDeclaration(new VertexElement("MORPHDELTA", 1, PixelFormat.R32G32B32A32_Float, 0));

            var vbs = mesh.Draw.VertexBuffers;
            var newVbs = new VertexBufferBinding[vbs.Length + 2];
            Array.Copy(vbs, newVbs, vbs.Length);
            newVbs[vbs.Length] = new VertexBufferBinding(buf0, decl0, n);
            newVbs[vbs.Length + 1] = new VertexBufferBinding(buf1, decl1, n);
            mesh.Draw.VertexBuffers = newVbs;

            mesh.MorphTargets = new MeshMorphTargetDefinition
            {
                MorphTargets = new[]
                {
                    new MorphTargetDescription { Name = "Target_0" },
                    new MorphTargetDescription { Name = "Target_1" },
                },
                VertexCount = n,
                HasNormals = false,
                HasTangents = false,
            };
            mesh.Parameters.Set(MaterialKeys.HasMorphTargets, true);
            mesh.Parameters.Set(MaterialKeys.MorphTargetMaxCount, 8);
            ready = true;

            Log.Info($"2 morph targets added: {n} verts, {mesh.Draw.VertexBuffers.Length} VBs");
            Log.Info("Press 1 = toggle target 0 (X), 2 = toggle target 1 (Y), +/- adjust.");
        }

        public override void Update()
        {
            if (!ready) return;

            bool changed = false;
            if (Input.IsKeyPressed(Keys.D1))
            {
                weight0 = weight0 > 0.5f ? 0 : 1;
                lastToggled = 0;
                changed = true;
            }
            if (Input.IsKeyPressed(Keys.D2))
            {
                weight1 = weight1 > 0.5f ? 0 : 1;
                lastToggled = 1;
                changed = true;
            }
            if (Input.IsKeyDown(Keys.OemPlus) || Input.IsKeyDown(Keys.Add))
            {
                if (lastToggled == 0) weight0 = Math.Clamp(weight0 + 0.02f, 0, 2);
                else weight1 = Math.Clamp(weight1 + 0.02f, 0, 2);
                changed = true;
            }
            if (Input.IsKeyDown(Keys.OemMinus) || Input.IsKeyDown(Keys.Subtract))
            {
                if (lastToggled == 0) weight0 = Math.Clamp(weight0 - 0.02f, 0, 2);
                else weight1 = Math.Clamp(weight1 - 0.02f, 0, 2);
                changed = true;
            }
            if (Input.IsKeyPressed(Keys.Space))
            {
                weight0 = 0;
                weight1 = 0;
                changed = true;
            }

            if (changed)
            {
                mc.SetMorphWeight(0, 0, weight0);
                mc.SetMorphWeight(0, 1, weight1);
                Log.Info($"w0={weight0:F2} w1={weight1:F2}");
            }
        }

        private void EnsureMorphTargetRenderFeature()
        {
            var compositor = SceneSystem.GraphicsCompositor;
            if (compositor == null) return;
            foreach (var rf in compositor.RenderFeatures)
            {
                if (rf is MeshRenderFeature meshRF)
                {
                    foreach (var sub in meshRF.RenderFeatures)
                        if (sub is MorphTargetRenderFeature) return;
                    meshRF.RenderFeatures.Insert(1, new MorphTargetRenderFeature());
                    Log.Info("Injected MorphTargetRenderFeature into compositor");
                    return;
                }
            }
        }
    }
}
