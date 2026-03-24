using System;
using System.Linq;
using Stride.Animations;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.Profiling;
using Stride.Rendering;

namespace MorphTargetSample
{
    /// <summary>
    /// Morph target (blend shape) demo.
    /// Attach to an entity with a ModelComponent that has morph targets.
    /// Auto-plays a looping morph weight animation on Start.
    ///
    /// Controls:
    ///   A     - Toggle auto-animation
    ///   1 / 2 - Toggle morph target 0 / 1 manually
    ///   Space - Reset all weights to 0
    /// </summary>
    public class GltfMorphTestScript : SyncScript
    {
        private ModelComponent mc;
        private float[] weights;
        private AnimationComponent animComponent;
        private bool animPlaying;
        private bool playingImported;
        private AnimationClip importedClip;
        private DebugTextSystem debugText;

        public override void Start()
        {
            EnsureMorphTargetRenderFeature();

            mc = Entity.Get<ModelComponent>();
            if (mc?.Model == null || mc.Model.Meshes.Count == 0)
            {
                Log.Error("GltfMorphTestScript requires a ModelComponent with a model");
                return;
            }

            var mesh = mc.Model.Meshes[0];
            var targetCount = mesh.MorphTargets?.MorphTargetCount ?? 0;
            weights = new float[targetCount];

            if (mesh.MorphTargets != null)
            {
                Log.Info($"MorphTargets: {targetCount} targets, normals={mesh.MorphTargets.HasNormals}, tangents={mesh.MorphTargets.HasTangents}");
            }

            // Try loading the imported .sdanim (compiled from glTF)
            try
            {
                importedClip = Content.Load<AnimationClip>("Models/AnimatedMorphCube_Square");
                Log.Info($"Loaded imported clip: duration={importedClip.Duration}, repeat={importedClip.RepeatMode}");
                foreach (var channel in importedClip.Channels)
                    Log.Info($"  Channel: {channel.Value.PropertyName}");
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not load imported animation: {ex.Message}");
            }

            Log.Info($"Morph cube ready ({targetCount} targets). A=animate, I=imported, 1/2=toggle, Space=reset.");

            debugText = Services.GetService<DebugTextSystem>();
            if (debugText == null)
            {
                debugText = new DebugTextSystem(Services);
                Game.GameSystems.Add(debugText);
            }
            debugText.Visible = true;

            if (targetCount > 0)
                StartAnimation();
        }

        public override void Update()
        {
            if (mc == null) return;

            // A = toggle programmatic animation
            if (Input.IsKeyPressed(Keys.A))
            {
                if (animPlaying)
                    StopAnimation();
                else
                    StartAnimation();
            }

            // I = play imported .sdanim clip
            if (Input.IsKeyPressed(Keys.I) && importedClip != null)
            {
                if (animPlaying) StopAnimation();
                PlayImportedAnimation();
            }

            // Manual weight control
            bool changed = false;
            if (Input.IsKeyPressed(Keys.D1))
            {
                if (animPlaying) StopAnimation();
                weights[0] = weights[0] > 0.5f ? 0 : 1;
                changed = true;
            }
            if (Input.IsKeyPressed(Keys.D2) && weights.Length > 1)
            {
                if (animPlaying) StopAnimation();
                weights[1] = weights[1] > 0.5f ? 0 : 1;
                changed = true;
            }
            if (Input.IsKeyPressed(Keys.Space))
            {
                if (animPlaying) StopAnimation();
                for (int i = 0; i < weights.Length; i++) weights[i] = 0;
                changed = true;
            }

            if (changed)
            {
                for (int i = 0; i < weights.Length; i++)
                    mc.SetMorphWeight(0, i, weights[i]);
            }

            // HUD overlay
            if (debugText != null)
            {
                string mode = animPlaying
                    ? (playingImported ? "Imported .sdanim (4.2s)" : "Programmatic (2s)")
                    : "Manual";
                var w0 = mc.MeshInfos?.Count > 0 ? mc.MeshInfos[0].MorphWeights : null;
                string weightStr = w0 != null
                    ? string.Join("  ", Enumerable.Range(0, w0.Length).Select(i => $"[{i}]={w0[i]:F2}"))
                    : "none";
                debugText.Print($"Mode: {mode}   Weights: {weightStr}", new Int2(10, 10));
                debugText.Print("[A] Programmatic  [I] Imported  [1][2] Toggle  [Space] Reset", new Int2(10, 30));
            }
        }

        private void StartAnimation()
        {
            var targetCount = weights.Length;
            if (targetCount == 0) return;

            var clip = new AnimationClip();
            clip.RepeatMode = AnimationRepeatMode.LoopInfinite;
            clip.Duration = TimeSpan.FromSeconds(2.0);

            // Target 0: triangle wave 0 → 1 → 0
            var curve0 = new AnimationCurve<float>();
            curve0.InterpolationType = AnimationCurveInterpolationType.Linear;
            curve0.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.Zero, 0f));
            curve0.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.FromSeconds(1.0), 1f));
            curve0.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.FromSeconds(2.0), 0f));
            clip.AddCurve("[ModelComponent.Key].MeshInfos[0].MorphWeights[0]", curve0);

            if (targetCount > 1)
            {
                // Target 1: opposite phase 1 → 0 → 1
                var curve1 = new AnimationCurve<float>();
                curve1.InterpolationType = AnimationCurveInterpolationType.Linear;
                curve1.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.Zero, 1f));
                curve1.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.FromSeconds(1.0), 0f));
                curve1.KeyFrames.Add(new KeyFrameData<float>(CompressedTimeSpan.FromSeconds(2.0), 1f));
                clip.AddCurve("[ModelComponent.Key].MeshInfos[0].MorphWeights[1]", curve1);
            }

            animComponent = Entity.GetOrCreate<AnimationComponent>();
            animComponent.Animations["MorphLoop"] = clip;
            animComponent.Play("MorphLoop");
            animPlaying = true;
            playingImported = false;
        }

        private void PlayImportedAnimation()
        {
            animComponent = Entity.GetOrCreate<AnimationComponent>();
            animComponent.Animations["ImportedMorph"] = importedClip;
            animComponent.Play("ImportedMorph");
            animPlaying = true;
            playingImported = true;
            Log.Info("Playing imported .sdanim morph animation");
        }

        private void StopAnimation()
        {
            if (animComponent != null)
            {
                animComponent.PlayingAnimations.Clear();
                animPlaying = false;
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
