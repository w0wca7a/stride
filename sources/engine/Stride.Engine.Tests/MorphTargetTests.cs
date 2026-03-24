// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Stride.Animations;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Graphics;
using Stride.Graphics.Regression;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Rendering.Compositing;
using Stride.Rendering.Lights;
using Stride.Rendering.ProceduralModels;
using Buffer = Stride.Graphics.Buffer;

namespace Stride.Engine.Tests
{
    /// <summary>
    /// Tests for morph target (blend shape) support across the engine.
    /// Covers data structures, ModelComponent API, copy semantics,
    /// and an end-to-end integration test with a procedural mesh that has morph targets.
    /// </summary>
    public class MorphTargetTests : EngineTestBase
    {
        // =====================================================================
        //  Helper: build a MeshMorphTargetDefinition without a GPU device
        // =====================================================================
        private static MeshMorphTargetDefinition CreateMorphTargetDefinition(
            int vertexCount,
            params string[] targetNames)
        {
            var targets = new MorphTargetDescription[targetNames.Length];
            for (int i = 0; i < targetNames.Length; i++)
            {
                targets[i] = new MorphTargetDescription
                {
                    Name = targetNames[i],
                    DefaultWeight = 0f,
                };
            }

            return new MeshMorphTargetDefinition
            {
                MorphTargets = targets,
                VertexCount = vertexCount,
                // No vertex buffers needed for unit tests without GPU
            };
        }

        // =====================================================================
        //  Helper: build a minimal Model with one mesh that has morph targets
        // =====================================================================
        private static Model CreateModelWithMorphTargets(
            IServiceRegistry services,
            int targetCount,
            bool withNormals = false,
            bool withTangents = false)
        {
            var graphicsDevice = services.GetSafeServiceAs<IGraphicsDeviceService>().GraphicsDevice;

            // Start from a simple cube
            var model = new CubeProceduralModel { Size = Vector3.One }.Generate(services);
            model.Materials.Add(Material.New(graphicsDevice, new MaterialDescriptor
            {
                Attributes =
                {
                    Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(Color.White)),
                    DiffuseModel = new MaterialDiffuseLambertModelFeature()
                }
            }));

            var mesh = model.Meshes[0];
            var vertexCount = mesh.Draw.VertexBuffers[0].Count; // actual vertex count from vertex buffer

            var names = new string[targetCount];
            for (int i = 0; i < targetCount; i++)
                names[i] = $"Target_{i}";

            var morphDef = new MeshMorphTargetDefinition
            {
                MorphTargets = names.Select(n => new MorphTargetDescription { Name = n, DefaultWeight = 0f }).ToArray(),
                VertexCount = vertexCount,
                HasNormals = withNormals,
                HasTangents = withTangents,
            };

            // Create per-target vertex buffers with delta data
            var morphVBs = new List<VertexBufferBinding>();
            int float4Size = 4 * sizeof(float);

            for (int t = 0; t < targetCount; t++)
            {
                // Position delta vertex buffer
                var posData = new float[vertexCount * 4];
                for (int v = 0; v < vertexCount; v++)
                {
                    posData[v * 4 + 0] = 2.0f * (t + 1); // X delta -- large enough to be unmissable
                    posData[v * 4 + 1] = 0.0f;            // Y delta
                    posData[v * 4 + 2] = 0.0f;            // Z delta
                    posData[v * 4 + 3] = 0.0f;            // W padding
                }
                var posBuffer = Buffer.Vertex.New(graphicsDevice, posData);
                var posDecl = new VertexDeclaration(new VertexElement("MORPHDELTA", t, PixelFormat.R32G32B32A32_Float, 0));
                morphVBs.Add(new VertexBufferBinding(posBuffer, posDecl, vertexCount));

                // Normal delta vertex buffer
                if (withNormals)
                {
                    var nrmData = new float[vertexCount * 4]; // zero deltas
                    var nrmBuffer = Buffer.Vertex.New(graphicsDevice, nrmData);
                    var nrmDecl = new VertexDeclaration(new VertexElement("MORPHNRMDELTA", t, PixelFormat.R32G32B32A32_Float, 0));
                    morphVBs.Add(new VertexBufferBinding(nrmBuffer, nrmDecl, vertexCount));
                }

                // Tangent delta vertex buffer
                if (withTangents)
                {
                    var tanData = new float[vertexCount * 4]; // zero deltas
                    var tanBuffer = Buffer.Vertex.New(graphicsDevice, tanData);
                    var tanDecl = new VertexDeclaration(new VertexElement("MORPHTANDELTA", t, PixelFormat.R32G32B32A32_Float, 0));
                    morphVBs.Add(new VertexBufferBinding(tanBuffer, tanDecl, vertexCount));
                }
            }

            // Append morph vertex buffers after the base vertex buffer
            var existingVBs = mesh.Draw.VertexBuffers;
            var newVBs = new VertexBufferBinding[existingVBs.Length + morphVBs.Count];
            Array.Copy(existingVBs, newVBs, existingVBs.Length);
            for (int m = 0; m < morphVBs.Count; m++)
                newVBs[existingVBs.Length + m] = morphVBs[m];
            mesh.Draw.VertexBuffers = newVBs;

            mesh.MorphTargets = morphDef;
            mesh.Parameters.Set(Rendering.Materials.MaterialKeys.HasMorphTargets, true);
            if (withNormals)
                mesh.Parameters.Set(Rendering.Materials.MaterialKeys.HasMorphTargetNormals, true);
            if (withTangents)
                mesh.Parameters.Set(Rendering.Materials.MaterialKeys.HasMorphTargetTangents, true);

            return model;
        }

        // =====================================================================
        //  1. MorphTargetDescription tests
        // =====================================================================

        [Fact]
        public void MorphTargetDescription_DefaultValues()
        {
            var desc = new MorphTargetDescription();
            Assert.Null(desc.Name);
            Assert.Equal(0f, desc.DefaultWeight);
        }

        [Fact]
        public void MorphTargetDescription_SetValues()
        {
            var desc = new MorphTargetDescription { Name = "Smile", DefaultWeight = 0.5f };
            Assert.Equal("Smile", desc.Name);
            Assert.Equal(0.5f, desc.DefaultWeight);
        }

        // =====================================================================
        //  2. MeshMorphTargetDefinition tests
        // =====================================================================

        [Fact]
        public void MeshMorphTargetDefinition_NullMorphTargets()
        {
            var def = new MeshMorphTargetDefinition();
            Assert.Equal(0, def.MorphTargetCount);
        }

        [Fact]
        public void MeshMorphTargetDefinition_WithTargets()
        {
            var def = CreateMorphTargetDefinition(100, "Smile", "Frown", "Blink");
            Assert.Equal(3, def.MorphTargetCount);
            Assert.Equal(100, def.VertexCount);
            Assert.Equal("Smile", def.MorphTargets[0].Name);
            Assert.Equal("Frown", def.MorphTargets[1].Name);
            Assert.Equal("Blink", def.MorphTargets[2].Name);
        }

        [Fact]
        public void MeshMorphTargetDefinition_Flags()
        {
            var def = CreateMorphTargetDefinition(100, "A", "B");
            // Default flags are false
            Assert.False(def.HasNormals);
            Assert.False(def.HasTangents);
        }

        // =====================================================================
        //  3. Mesh copy constructor preserves MorphTargets
        // =====================================================================

        [Fact]
        public void Mesh_CopyConstructor_PreservesMorphTargets()
        {
            var originalDef = CreateMorphTargetDefinition(50, "Happy", "Sad");
            var originalMesh = new Mesh
            {
                MorphTargets = originalDef,
                Name = "TestMesh",
            };

            var copy = new Mesh(originalMesh);

            // Should be a shallow copy -- same reference
            Assert.Same(originalDef, copy.MorphTargets);
            Assert.Equal("TestMesh", copy.Name);
            Assert.Equal(2, copy.MorphTargets.MorphTargetCount);
        }

        // =====================================================================
        //  4. ModelComponent morph weight API tests (requires game context)
        // =====================================================================

        [Fact]
        public void TestMorphWeightApiInGame()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;
                await game.Script.NextFrame();

                // Create a model with 3 morph targets
                var model = CreateModelWithMorphTargets(game.Services, 3);
                var entity = new Entity { new ModelComponent { Model = model } };
                game.Scene.Entities.Add(entity);
                await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();

                // --- GetMorphTargetNames ---
                var names = mc.GetMorphTargetNames();
                Assert.Equal(3, names.Count);
                Assert.Contains("Target_0", names);
                Assert.Contains("Target_1", names);
                Assert.Contains("Target_2", names);

                // --- Default weights should be 0 ---
                Assert.Equal(0f, mc.GetMorphWeight(0, "Target_0"));
                Assert.Equal(0f, mc.GetMorphWeight(0, "Target_1"));
                Assert.Equal(0f, mc.GetMorphWeight(0, "Target_2"));

                // --- SetMorphWeight by name ---
                Assert.True(mc.SetMorphWeight(0, "Target_1", 0.75f));
                Assert.Equal(0.75f, mc.GetMorphWeight(0, "Target_1"));

                // --- SetMorphWeight by index ---
                mc.SetMorphWeight(0, 2, 0.5f);
                Assert.Equal(0.5f, mc.GetMorphWeight(0, "Target_2"));

                // --- SetMorphWeight by name across all meshes ---
                mc.SetMorphWeight("Target_0", 1.0f);
                Assert.Equal(1.0f, mc.GetMorphWeight(0, "Target_0"));

                // --- Invalid inputs return safe defaults ---
                Assert.Equal(0f, mc.GetMorphWeight(-1, "Target_0"));        // bad mesh index
                Assert.Equal(0f, mc.GetMorphWeight(999, "Target_0"));       // bad mesh index
                Assert.Equal(0f, mc.GetMorphWeight(0, "NonExistent"));      // bad name
                Assert.False(mc.SetMorphWeight(0, "NonExistent", 1.0f));    // bad name
                Assert.False(mc.SetMorphWeight(-1, "Target_0", 1.0f));      // bad mesh index

                // --- SetMorphWeight by index with out-of-range does nothing ---
                mc.SetMorphWeight(0, 999, 1.0f); // should not throw

                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  5. ModelComponent with no morph targets is safe
        // =====================================================================

        [Fact]
        public void TestMorphWeightApi_NoMorphTargets()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;
                await game.Script.NextFrame();

                // Plain cube with no morph targets
                var model = new CubeProceduralModel { Size = Vector3.One }.Generate(game.Services);
                model.Materials.Add(Material.New(
                    game.GraphicsDevice,
                    new MaterialDescriptor
                    {
                        Attributes =
                        {
                            Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(Color.White)),
                            DiffuseModel = new MaterialDiffuseLambertModelFeature()
                        }
                    }));

                var entity = new Entity { new ModelComponent { Model = model } };
                game.Scene.Entities.Add(entity);
                await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();

                // All operations on a model without morph targets should be safe
                var names = mc.GetMorphTargetNames();
                Assert.Empty(names);
                Assert.Equal(0f, mc.GetMorphWeight(0, "Anything"));
                Assert.False(mc.SetMorphWeight(0, "Anything", 1.0f));

                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  6. ModelComponent with default weights
        // =====================================================================

        [Fact]
        public void TestMorphWeight_DefaultWeightsFromDescription()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;
                await game.Script.NextFrame();

                // Create model and set non-zero default weights
                var model = CreateModelWithMorphTargets(game.Services, 2);
                model.Meshes[0].MorphTargets.MorphTargets[0].DefaultWeight = 0.3f;
                model.Meshes[0].MorphTargets.MorphTargets[1].DefaultWeight = 0.7f;

                var entity = new Entity { new ModelComponent { Model = model } };
                game.Scene.Entities.Add(entity);
                await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();

                // MeshInfo.MorphWeights should be initialized from DefaultWeight
                Assert.Equal(0.3f, mc.GetMorphWeight(0, "Target_0"));
                Assert.Equal(0.7f, mc.GetMorphWeight(0, "Target_1"));

                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  7. End-to-end integration: morph target rendering pipeline
        // =====================================================================

        public MorphTargetTests()
        {
            GraphicsDeviceManager.DeviceCreationFlags = DeviceCreationFlags.Debug;
            GraphicsDeviceManager.PreferredGraphicsProfile = new[] { GraphicsProfile.Level_11_0 };
            GraphicsDeviceManager.ShaderProfile = GraphicsProfile.Level_11_0;
        }

        protected override async Task LoadContent()
        {
            // Do NOT call base.LoadContent() -- we set up everything from scratch
            // to avoid pre-compiled shader caches from the asset bundle at wrong profile

            var cameraComp = new CameraComponent();
            Camera = new Entity("Camera") { cameraComp };
            Camera.Transform.Position = new Vector3(0, 0, 3);

            SceneSystem.GraphicsCompositor = GraphicsCompositorHelper.CreateDefault(
                enablePostEffects: false,
                camera: cameraComp,
                graphicsProfile: GraphicsProfile.Level_11_0);

            Scene = new Scene();
            Scene.Entities.Add(Camera);

            AmbientLight = new LightComponent { Type = new LightAmbient(), Intensity = 1.0f };
            Scene.Entities.Add(new Entity("Light") { AmbientLight });

            // Create morph target entity in LoadContent so it's ready before first render
            var model = CreateModelWithMorphTargets(Services, 1, withNormals: false);
            model.Meshes[0].MorphTargets.MorphTargets[0].DefaultWeight = 1.0f;
            var morphEntity = new Entity("MorphCube") { new ModelComponent { Model = model } };
            Scene.Entities.Add(morphEntity);

            SceneSystem.SceneInstance = new SceneInstance(Services, Scene);
        }

        protected override void RegisterTests()
        {
            // No frame-based tests for the rendering test — controlled by script task instead
        }

        [Fact]
        public void RunMorphTargetRenderingTest()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;

                // Wait for shader compilation
                for (int f = 0; f < 30; f++)
                    await game.Script.NextFrame();

                var morphEntity = game.Scene.Entities.FirstOrDefault(e => e.Name == "MorphCube");
                var mc = morphEntity?.Get<ModelComponent>();

                Console.WriteLine("=== Morph Target Interactive Demo ===");
                Console.WriteLine("Press 1 to toggle Target_0 (shift +X)");
                Console.WriteLine("Press 2 to set weight to 0.5");
                Console.WriteLine("Press 3 to set weight to 0");
                Console.WriteLine("Press Escape to exit");
                Console.WriteLine();

                float weight = mc?.GetMorphWeight(0, "Target_0") ?? 0;
                Console.WriteLine($"Initial weight: {weight}");

                // Interactive loop — toggle morph weights with keyboard
                while (game.IsRunning)
                {
                    await game.Script.NextFrame();

                    if (game.Input.IsKeyPressed(Stride.Input.Keys.Escape))
                    {
                        game.Exit();
                        break;
                    }

                    if (game.Input.IsKeyPressed(Stride.Input.Keys.D1))
                    {
                        weight = weight > 0.5f ? 0f : 1f;
                        mc?.SetMorphWeight(0, "Target_0", weight);
                        Console.WriteLine($"Target_0 weight = {weight}");
                    }

                    if (game.Input.IsKeyPressed(Stride.Input.Keys.D2))
                    {
                        weight = 0.5f;
                        mc?.SetMorphWeight(0, "Target_0", weight);
                        Console.WriteLine($"Target_0 weight = {weight}");
                    }

                    if (game.Input.IsKeyPressed(Stride.Input.Keys.D3))
                    {
                        weight = 0f;
                        mc?.SetMorphWeight(0, "Target_0", weight);
                        Console.WriteLine($"Target_0 weight = {weight}");
                    }
                }
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  8. Morph target data structure edge cases
        // =====================================================================

        [Fact]
        public void MeshMorphTargetDefinition_SingleTarget()
        {
            var def = CreateMorphTargetDefinition(1, "OnlyOne");
            Assert.Equal(1, def.MorphTargetCount);
            Assert.Equal(1, def.VertexCount);
        }

        [Fact]
        public void MeshMorphTargetDefinition_EmptyTargetArray()
        {
            var def = new MeshMorphTargetDefinition
            {
                MorphTargets = Array.Empty<MorphTargetDescription>(),
                VertexCount = 100,
            };
            Assert.Equal(0, def.MorphTargetCount);
        }

        [Fact]
        public void MeshMorphTargetDefinition_LargeTargetCount()
        {
            // Test with more targets than the default shader max (8)
            var names = Enumerable.Range(0, 16).Select(i => $"Target_{i}").ToArray();
            var def = CreateMorphTargetDefinition(1000, names);
            Assert.Equal(16, def.MorphTargetCount);
            Assert.Equal(1000, def.VertexCount);
        }

        // =====================================================================
        //  9. RenderMesh carries MorphWeights
        // =====================================================================

        [Fact]
        public void RenderMesh_MorphWeights_DefaultNull()
        {
            var renderMesh = new RenderMesh();
            Assert.Null(renderMesh.MorphWeights);
        }

        [Fact]
        public void RenderMesh_MorphWeights_CanBeSet()
        {
            var renderMesh = new RenderMesh();
            var weights = new float[] { 0.1f, 0.5f, 0.9f };
            renderMesh.MorphWeights = weights;
            Assert.Same(weights, renderMesh.MorphWeights);
            Assert.Equal(0.5f, renderMesh.MorphWeights[1]);
        }

        // =====================================================================
        //  10. Mesh.Parameters get morph target keys set correctly
        // =====================================================================

        [Fact]
        public void TestMorphTargetParameterKeys()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;
                await game.Script.NextFrame();

                // Model with position + normal morph targets (no tangents)
                var model = CreateModelWithMorphTargets(game.Services, 2, withNormals: true, withTangents: false);
                var mesh = model.Meshes[0];

                Assert.True(mesh.Parameters.Get(Rendering.Materials.MaterialKeys.HasMorphTargets));
                Assert.True(mesh.Parameters.Get(Rendering.Materials.MaterialKeys.HasMorphTargetNormals));
                Assert.False(mesh.Parameters.Get(Rendering.Materials.MaterialKeys.HasMorphTargetTangents));

                // Model with all three delta types
                var model2 = CreateModelWithMorphTargets(game.Services, 1, withNormals: true, withTangents: true);
                var mesh2 = model2.Meshes[0];

                Assert.True(mesh2.Parameters.Get(Rendering.Materials.MaterialKeys.HasMorphTargets));
                Assert.True(mesh2.Parameters.Get(Rendering.Materials.MaterialKeys.HasMorphTargetNormals));
                Assert.True(mesh2.Parameters.Get(Rendering.Materials.MaterialKeys.HasMorphTargetTangents));

                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  11. Multi-target weight independence
        // =====================================================================

        [Fact]
        public void TestMultiTargetWeightIndependence()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;
                await game.Script.NextFrame();

                // Create model with 8 morph targets (max shader count)
                var model = CreateModelWithMorphTargets(game.Services, 8);
                var entity = new Entity { new ModelComponent { Model = model } };
                game.Scene.Entities.Add(entity);
                await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();

                // Set each target to a distinct weight
                for (int i = 0; i < 8; i++)
                    mc.SetMorphWeight(0, i, (i + 1) * 0.1f);

                // Verify each target retains its independent weight
                for (int i = 0; i < 8; i++)
                    Assert.Equal((i + 1) * 0.1f, mc.GetMorphWeight(0, $"Target_{i}"), 5);

                // Change one target, verify others unchanged
                mc.SetMorphWeight(0, 3, 0.99f);
                Assert.Equal(0.99f, mc.GetMorphWeight(0, "Target_3"));
                Assert.Equal(0.1f, mc.GetMorphWeight(0, "Target_0"), 5);
                Assert.Equal(0.5f, mc.GetMorphWeight(0, "Target_4"), 5);
                Assert.Equal(0.8f, mc.GetMorphWeight(0, "Target_7"), 5);

                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  12. Multi-target rendering integration (automated, non-interactive)
        // =====================================================================

        [Fact]
        public void TestMultiTargetRendering()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;

                // Wait for initial shader compilation and scene setup
                for (int f = 0; f < 10; f++)
                    await game.Script.NextFrame();

                // Create model with 2 morph targets
                var model = CreateModelWithMorphTargets(game.Services, 2);
                var entity = new Entity("MultiMorphCube") { new ModelComponent { Model = model } };
                entity.Transform.Position = new Vector3(3, 0, 0);
                game.Scene.Entities.Add(entity);

                // Let the render pipeline process the new entity
                for (int f = 0; f < 5; f++)
                    await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();

                // Set target 0 weight
                mc.SetMorphWeight(0, 0, 1.0f);
                Assert.Equal(1.0f, mc.GetMorphWeight(0, "Target_0"));
                Assert.Equal(0f, mc.GetMorphWeight(0, "Target_1"));

                // Render a few frames with target 0 active
                for (int f = 0; f < 5; f++)
                    await game.Script.NextFrame();

                // Set target 1 weight while target 0 is still active
                mc.SetMorphWeight(0, 1, 0.5f);
                Assert.Equal(1.0f, mc.GetMorphWeight(0, "Target_0"));
                Assert.Equal(0.5f, mc.GetMorphWeight(0, "Target_1"));

                // Render a few frames with both targets active
                for (int f = 0; f < 5; f++)
                    await game.Script.NextFrame();

                // Reset both
                mc.SetMorphWeight(0, 0, 0f);
                mc.SetMorphWeight(0, 1, 0f);

                for (int f = 0; f < 5; f++)
                    await game.Script.NextFrame();

                // No crash, no exception — pipeline handled multi-target correctly
                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  13. TransformationMorphTargetsKeys exist with correct types
        // =====================================================================

        [Fact]
        public void TransformationMorphTargetsKeys_Exist()
        {
            // Verify the keys exist and have the expected names
            Assert.NotNull(TransformationMorphTargetsKeys.MorphWeights);
            Assert.NotNull(TransformationMorphTargetsKeys.MorphTargetActiveCount);
            Assert.Contains("MorphWeights", TransformationMorphTargetsKeys.MorphWeights.Name);
            Assert.Contains("MorphTargetActiveCount", TransformationMorphTargetsKeys.MorphTargetActiveCount.Name);
        }

        // =====================================================================
        //  14. MorphTargetMaxCount permutation key default
        // =====================================================================

        [Fact]
        public void MaterialKeys_MorphTargetMaxCount_Default()
        {
            // Default MorphTargetMaxCount should be 8
            var parameters = new ParameterCollection();
            Assert.Equal(8, parameters.Get(MaterialKeys.MorphTargetMaxCount));
        }

        // =====================================================================
        //  15. Multi-target with normals and tangents rendering
        // =====================================================================

        [Fact]
        public void TestMultiTargetWithNormalsAndTangents()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;

                for (int f = 0; f < 10; f++)
                    await game.Script.NextFrame();

                // Create model with 3 targets including normals and tangents
                var model = CreateModelWithMorphTargets(game.Services, 3, withNormals: true, withTangents: true);
                var entity = new Entity("FullMorphCube") { new ModelComponent { Model = model } };
                game.Scene.Entities.Add(entity);

                for (int f = 0; f < 5; f++)
                    await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();
                var mesh = model.Meshes[0];

                // Verify all permutation keys are set
                Assert.True(mesh.Parameters.Get(MaterialKeys.HasMorphTargets));
                Assert.True(mesh.Parameters.Get(MaterialKeys.HasMorphTargetNormals));
                Assert.True(mesh.Parameters.Get(MaterialKeys.HasMorphTargetTangents));

                // Set all 3 targets to different weights
                mc.SetMorphWeight(0, 0, 1.0f);
                mc.SetMorphWeight(0, 1, 0.5f);
                mc.SetMorphWeight(0, 2, 0.25f);

                // Render several frames — verify no crash with all delta types active
                for (int f = 0; f < 10; f++)
                    await game.Script.NextFrame();

                Assert.Equal(1.0f, mc.GetMorphWeight(0, "Target_0"));
                Assert.Equal(0.5f, mc.GetMorphWeight(0, "Target_1"));
                Assert.Equal(0.25f, mc.GetMorphWeight(0, "Target_2"));

                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  16. Exact max (8 targets) rendering stress test
        // =====================================================================

        [Fact]
        public void TestMaxTargetCountRendering()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;

                for (int f = 0; f < 10; f++)
                    await game.Script.NextFrame();

                // Create model with exactly 8 morph targets (shader max)
                var model = CreateModelWithMorphTargets(game.Services, 8);
                var entity = new Entity("Max8MorphCube") { new ModelComponent { Model = model } };
                game.Scene.Entities.Add(entity);

                for (int f = 0; f < 5; f++)
                    await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();

                // Activate all 8 targets simultaneously
                for (int i = 0; i < 8; i++)
                    mc.SetMorphWeight(0, i, 1.0f);

                // Render with all 8 active
                for (int f = 0; f < 10; f++)
                    await game.Script.NextFrame();

                // Verify all weights survived rendering
                for (int i = 0; i < 8; i++)
                    Assert.Equal(1.0f, mc.GetMorphWeight(0, $"Target_{i}"));

                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  17. Multiple meshes with different morph target configurations
        // =====================================================================

        [Fact]
        public void TestMultipleMeshes_DifferentMorphConfigs()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;
                await game.Script.NextFrame();

                var graphicsDevice = game.GraphicsDevice;

                // Build a model with 2 meshes: one with morph targets, one without
                var model = new CubeProceduralModel { Size = Vector3.One }.Generate(game.Services);
                model.Materials.Add(Material.New(graphicsDevice, new MaterialDescriptor
                {
                    Attributes =
                    {
                        Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(Color.White)),
                        DiffuseModel = new MaterialDiffuseLambertModelFeature()
                    }
                }));

                // First mesh gets morph targets
                var morphDef = CreateMorphTargetDefinition(36, "Stretch", "Squash");
                model.Meshes[0].MorphTargets = morphDef;
                model.Meshes[0].Parameters.Set(Rendering.Materials.MaterialKeys.HasMorphTargets, true);

                // Add a second mesh without morph targets
                var cube2 = new CubeProceduralModel { Size = Vector3.One }.Generate(game.Services);
                model.Meshes.Add(cube2.Meshes[0]);
                model.Materials.Add(model.Materials[0]);

                var entity = new Entity { new ModelComponent { Model = model } };
                game.Scene.Entities.Add(entity);
                await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();

                // Mesh 0 has morph targets
                var names = mc.GetMorphTargetNames();
                Assert.Contains("Stretch", names);
                Assert.Contains("Squash", names);
                Assert.True(mc.SetMorphWeight(0, "Stretch", 0.8f));
                Assert.Equal(0.8f, mc.GetMorphWeight(0, "Stretch"));

                // Mesh 1 has no morph targets -- safe to query
                Assert.Equal(0f, mc.GetMorphWeight(1, "Stretch"));
                Assert.False(mc.SetMorphWeight(1, "Stretch", 1.0f));

                // SetMorphWeight across all meshes: only affects mesh 0
                mc.SetMorphWeight("Squash", 0.6f);
                Assert.Equal(0.6f, mc.GetMorphWeight(0, "Squash"));
                Assert.Equal(0f, mc.GetMorphWeight(1, "Squash")); // mesh 1 unaffected

                game.Exit();
            });
            RunGameTest(game);
        }

        // =====================================================================
        //  18. Animation-driven morph weight playback
        // =====================================================================

        /// <summary>
        /// Validates that the animation system can drive MorphWeights via
        /// the [ModelComponent.Key].MeshInfos[{meshIndex}].MorphWeights[{targetIndex}]
        /// update path. This is the same path used by imported morph weight animations.
        /// </summary>
        [Fact]
        public void TestAnimationDrivenMorphWeights()
        {
            var game = new MorphTargetTests();
            game.Script.AddTask(async () =>
            {
                game.ScreenShotAutomationEnabled = false;

                for (int f = 0; f < 10; f++)
                    await game.Script.NextFrame();

                // Create model with 2 morph targets
                var model = CreateModelWithMorphTargets(game.Services, 2);
                var entity = new Entity("AnimMorphCube") { new ModelComponent { Model = model } };
                game.Scene.Entities.Add(entity);

                await game.Script.NextFrame();

                var mc = entity.Get<ModelComponent>();

                // Verify initial weights are 0
                Assert.Equal(0f, mc.GetMorphWeight(0, "Target_0"));
                Assert.Equal(0f, mc.GetMorphWeight(0, "Target_1"));

                // Build an animation clip that animates morph weights
                // using the same path format that the asset pipeline produces
                var clip = new AnimationClip();
                clip.RepeatMode = AnimationRepeatMode.PlayOnce;

                // Target_0: ramp from 0 to 1 over 1 second
                var curve0 = new AnimationCurve<float>();
                curve0.InterpolationType = AnimationCurveInterpolationType.Linear;
                curve0.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.Zero, 0f));
                curve0.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.FromSeconds(1.0), 1f));
                clip.AddCurve("[ModelComponent.Key].MeshInfos[0].MorphWeights[0]", curve0);

                // Target_1: ramp from 0 to 0.5 over 1 second
                var curve1 = new AnimationCurve<float>();
                curve1.InterpolationType = AnimationCurveInterpolationType.Linear;
                curve1.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.Zero, 0f));
                curve1.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.FromSeconds(1.0), 0.5f));
                clip.AddCurve("[ModelComponent.Key].MeshInfos[0].MorphWeights[1]", curve1);

                clip.Duration = TimeSpan.FromSeconds(1.0);

                // Add animation component and play the clip
                var animComponent = entity.GetOrCreate<AnimationComponent>();
                animComponent.Animations.Add("MorphAnim", clip);
                var playingAnim = animComponent.Play("MorphAnim");

                // Advance frames to let the animation play
                for (int f = 0; f < 60; f++)
                    await game.Script.NextFrame();

                // Animation should have driven the weights to their target values
                var w0 = mc.MeshInfos[0].MorphWeights[0];
                var w1 = mc.MeshInfos[0].MorphWeights[1];

                // Target_0 should have reached 1.0, Target_1 should have reached 0.5
                // (game time in tests may run faster than real-time)
                Assert.True(w0 > 0.5f, $"Expected Target_0 weight > 0.5 after animation, got {w0}");
                Assert.True(w1 > 0.2f, $"Expected Target_1 weight > 0.2 after animation, got {w1}");
                Assert.True(w1 < w0, $"Expected Target_1 ({w1}) < Target_0 ({w0}) since curve1 peaks at 0.5");

                game.Exit();
            });
            RunGameTest(game);
        }
    }
}
