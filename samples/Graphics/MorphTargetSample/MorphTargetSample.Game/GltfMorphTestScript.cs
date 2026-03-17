using System;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.Rendering;

namespace MorphTargetSample
{
    /// <summary>
    /// Loads the Khronos AnimatedMorphCube glTF model and verifies morph target import.
    /// Press G to load/toggle the glTF morph cube. Press 1/2 to set weights.
    /// </summary>
    public class GltfMorphTestScript : SyncScript
    {
        private ModelComponent mc;
        private bool loaded;
        private float[] weights;

        public override void Start()
        {
            EnsureMorphTargetRenderFeature();
        }

        public override void Update()
        {
            if (Input.IsKeyPressed(Keys.G))
            {
                if (!loaded)
                    LoadGltfModel();
                else
                    ToggleVisibility();
            }

            if (!loaded || mc == null) return;

            bool changed = false;
            if (Input.IsKeyPressed(Keys.D1))
            {
                weights[0] = weights[0] > 0.5f ? 0 : 1;
                changed = true;
            }
            if (Input.IsKeyPressed(Keys.D2) && weights.Length > 1)
            {
                weights[1] = weights[1] > 0.5f ? 0 : 1;
                changed = true;
            }
            if (Input.IsKeyPressed(Keys.Space))
            {
                for (int i = 0; i < weights.Length; i++) weights[i] = 0;
                changed = true;
            }

            if (changed)
            {
                for (int i = 0; i < weights.Length; i++)
                    mc.SetMorphWeight(0, i, weights[i]);
                Log.Info($"glTF weights: {string.Join(", ", weights)}");
            }
        }

        private void LoadGltfModel()
        {
            try
            {
                var model = Content.Load<Model>("Models/AnimatedMorphCube");
                if (model == null)
                {
                    Log.Error("Failed to load AnimatedMorphCube model");
                    return;
                }

                Log.Info($"Loaded AnimatedMorphCube: {model.Meshes.Count} meshes");

                for (int m = 0; m < model.Meshes.Count; m++)
                {
                    var mesh = model.Meshes[m];
                    Log.Info($"  Mesh[{m}] '{mesh.Name}': {mesh.Draw.VertexBuffers.Length} VBs, {mesh.Draw.DrawCount} indices");

                    if (mesh.MorphTargets != null)
                    {
                        Log.Info($"  MorphTargets: {mesh.MorphTargets.MorphTargetCount} targets, " +
                                 $"normals={mesh.MorphTargets.HasNormals}, tangents={mesh.MorphTargets.HasTangents}");
                        for (int t = 0; t < mesh.MorphTargets.MorphTargetCount; t++)
                        {
                            var desc = mesh.MorphTargets.MorphTargets[t];
                            Log.Info($"    [{t}] '{desc.Name}' defaultWeight={desc.DefaultWeight}");
                        }
                    }
                    else
                    {
                        Log.Warning($"  No MorphTargets on mesh[{m}]!");
                    }
                }

                // Attach to a new entity
                mc = new ModelComponent { Model = model };
                var entity = new Entity("GltfMorphCube") { mc };
                entity.Transform.Position = new Vector3(-0.5f, 0.8f, 0f);
                entity.Transform.Scale = new Vector3(0.15f);
                entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(
                    MathUtil.DegreesToRadians(30), MathUtil.DegreesToRadians(20), 0);
                Entity.Scene.Entities.Add(entity);

                var targetCount = model.Meshes[0].MorphTargets?.MorphTargetCount ?? 0;
                weights = new float[targetCount];
                loaded = true;

                Log.Info($"glTF morph cube spawned with {targetCount} targets. Press 1/2 to toggle, Space to reset.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading glTF model: {ex.Message}");
            }
        }

        private void ToggleVisibility()
        {
            var entity = Entity.Scene.Entities.FirstOrDefault(e => e.Name == "GltfMorphCube");
            if (entity != null)
            {
                entity.Get<ModelComponent>().Enabled = !entity.Get<ModelComponent>().Enabled;
                Log.Info($"glTF morph cube visibility: {entity.Get<ModelComponent>().Enabled}");
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
                    return;
                }
            }
        }
    }
}
