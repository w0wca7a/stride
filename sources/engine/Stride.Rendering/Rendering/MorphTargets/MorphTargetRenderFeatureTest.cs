using System;
using System.Collections.Generic;
using Stride.Core.Diagnostics;
using Stride.Graphics;
using Stride.Rendering.Materials;
using Stride.Core.Storage;
using Stride.Engine;
using Stride.Rendering;
using Stride.Core;

namespace Stride.Rendering;

public class MorphTargetRenderFeatureTest : SubRenderFeature
{
    private struct MorphInfo
    {
        public Texture PositionTexture;
        public bool Initialized;
    }

    public bool EnableTest = false;

    // Хранилище весов: извлекается в Extract, используется в Prepare
    private ObjectPropertyKey<float> _morphWeightKey;

    private readonly Dictionary<Mesh, MorphInfo> _infos = new();
    private StaticObjectPropertyKey<RenderEffect> _renderEffectKey;
    private ConstantBufferOffsetReference _weightOffset;
    private ConstantBufferOffsetReference _vertexCountOffset;
    private LogicalGroupReference _morphLogicalGroup;
    private float _testWeight = 0f;
    private float _weightDirection = 1f;

    private Logger Logger => GlobalLogger.GetLogger(nameof(MorphTargetRenderFeatureTest));

    protected override void InitializeCore()
    {
        // Создаём хранилище на объект — как в ObjectInfoRenderFeature
        _morphWeightKey = RootRenderFeature.RenderData.CreateObjectKey<float>();

        _morphLogicalGroup = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawLogicalGroup("MorphTargetsTest");
        _renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;
        _weightOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestWeight");
        _vertexCountOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestVertexCount");
    }

    public override void Extract()
    {
        var morphWeightHolder = RootRenderFeature.RenderData.GetData(_morphWeightKey);

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;
            if (mesh.MorphTargets.VertexCount > 16384) continue;

            // Устанавливаем флаг до PrepareEffectPermutations
            mesh.Parameters.Set(MaterialKeys.HasMorphTargets, true);

            float weight = 0f;
            if (!EnableTest && renderMesh.Source is ModelComponent modelComponent)
            {
                for (int i = 0; i < modelComponent.Model.Meshes.Count; i++)
                {
                    if (modelComponent.Model.Meshes[i] == mesh)
                    {
                        // MorphWeights[0] — первый таргет; позже можно суммировать все
                        var meshInfo = /* modelComponent.meshInfos[i] */ null; 
                        // weight = meshInfo.MorphWeights?[0] ?? 0f;
                        // Пока доступа к internal meshInfos нет — используем публичный API:
                        weight = modelComponent.GetMorphWeight(i, 0);
                        break;
                    }
                }
            }

            morphWeightHolder[objectNodeReference] = weight;
        }
    }

    public override void PrepareEffectPermutations(RenderDrawContext context)
    {
        var renderEffects = RootRenderFeature.RenderData.GetData(_renderEffectKey);
        int effectSlotCount = ((RootEffectRenderFeature)RootRenderFeature).EffectPermutationSlotCount;

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var staticObjectNode = renderMesh.StaticObjectNode;

            var mesh = renderMesh.Mesh;
            if (mesh?.MorphTargets == null) continue;
            if (mesh.MorphTargets.VertexCount > 16384) continue;

            var hasMorphTargets = mesh.Parameters.Get(MaterialKeys.HasMorphTargets);

            for (int i = 0; i < effectSlotCount; i++)
            {
                var staticEffectObjectNode = staticObjectNode * effectSlotCount + i;
                var renderEffect = renderEffects[staticEffectObjectNode];
                if (renderEffect == null || !renderEffect.IsUsedDuringThisFrame(RenderSystem))
                    continue;

                renderEffect.EffectValidator.ValidateParameter(MaterialKeys.HasMorphTargets, hasMorphTargets);

                //Logger.Info($"PrepareEffectPermutations: mesh='{mesh.Name}' HasMorphTargets={hasMorphTargets}");
            }
        }
    }

/*
    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        float dt = (float)context.RenderContext.Time.Elapsed.TotalSeconds;
        _testWeight += dt * _weightDirection * 0.5f;

        if (_testWeight >= 1f) { _testWeight = 1f; _weightDirection = -1f; }
        else if (_testWeight <= 0f) { _testWeight = 0f; _weightDirection = 1f; }

        Logger.Info($"Draw: TestWeight={_testWeight:F3}");
    }
*/
    public override unsafe void Prepare(RenderDrawContext context)
    {
        if (EnableTest)
        {
            float dt = (float)context.RenderContext.Time.Elapsed.TotalSeconds;
            _testWeight += dt * _weightDirection * 0.5f;

            if (_testWeight >= 1f) { _testWeight = 1f; _weightDirection = -1f; }
            else if (_testWeight <= 0f) { _testWeight = 0f; _weightDirection = 1f; }
            Logger.Info($"Prepare: TestWeight={_testWeight:F3}");
        }

        // Ленивое создание текстур
        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;
            if (mesh.MorphTargets.VertexCount > 16384) continue;

            if (mesh.MorphTargets.PositionDeltaData == null || mesh.MorphTargets.PositionDeltaData.Length == 0) continue;

            if (!_infos.TryGetValue(mesh, out var info))
            {
                var def = mesh.MorphTargets;
                int floatCount = def.VertexCount * 4;
                var firstTargetData = new float[floatCount];
                Array.Copy(def.PositionDeltaData, 0, firstTargetData, 0, floatCount);

                info.PositionTexture = Texture.New2D(
                    context.GraphicsDevice,
                    def.VertexCount,
                    1,
                    PixelFormat.R32G32B32A32_Float,
                    TextureFlags.ShaderResource,
                    1,
                    GraphicsResourceUsage.Default);

                info.PositionTexture.SetData(context.CommandList, firstTargetData, 0, 0);
                info.Initialized = true;
                _infos[mesh] = info;
            }
        }

        // Запись в cbuffer
        for (int i = 0; i < RootRenderFeature.RenderNodes.Count; i++)
        {
            var renderNode = RootRenderFeature.RenderNodes[i];
            var perDrawLayout = renderNode.RenderEffect?.Reflection?.PerDrawLayout;
            if (perDrawLayout == null)
            {
                //Logger.Warning($"Prepare: RenderNode[{i}] perDrawLayout is null");
                continue;
            }

            var renderMesh = (RenderMesh)renderNode.RenderObject;
            if (renderMesh.Mesh?.MorphTargets == null) continue;
            if (renderMesh.Mesh.MorphTargets.VertexCount > 16384) continue;
            #region First
            if (!_infos.TryGetValue(renderMesh.Mesh, out var info)) continue;
            var logicalGroup = perDrawLayout.GetLogicalGroup(_morphLogicalGroup);
            if (logicalGroup.Hash != ObjectId.Empty)
            {
                renderNode.Resources.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 0, info.PositionTexture);
                Logger.Info($"Prepare: texture bound at slot {logicalGroup.DescriptorEntryStart}");
            }
            else Logger.Warning("Prepare: logical group MorphTargets not found");
            #endregion
            var weightOff = perDrawLayout.GetConstantBufferOffset(_weightOffset);
            Logger.Info($"Prepare cbuffer: mesh='{renderMesh.Mesh.Name}' weightOff={weightOff}");

            if (weightOff != -1)
/*
                *((float*)((byte*)renderNode.Resources.ConstantBuffer.Data + weightOff)) = _testWeight;
*/
            {
                float weight;

                if (EnableTest)
                {
                    weight = _testWeight;
                }
                else
                {
                    weight = 0f;
                    var modelComponent = renderMesh.Source as ModelComponent;
                    if (modelComponent != null)
                    {
                        var meshes = modelComponent.Model?.Meshes;
                        if (meshes != null)
                        {
                            int meshIndex = meshes.IndexOf(renderMesh.Mesh);
                            if (meshIndex >= 0)
                                weight = modelComponent.GetMorphWeight(meshIndex, 0);
                        }
                    }
                }

                *((float*)((byte*)renderNode.Resources.ConstantBuffer.Data + weightOff)) = weight;
            }
            var countOff = perDrawLayout.GetConstantBufferOffset(_vertexCountOffset);
            Logger.Info($"Prepare cbuffer: countOff={countOff}");

            if (countOff != -1)
            {
                var count = renderMesh.Mesh.MorphTargets.VertexCount;
                *((int*)((byte*)renderNode.Resources.ConstantBuffer.Data + countOff)) = count;
                Logger.Info($"Prepare cbuffer: writing MorphVertexCount={count}");
            }
        }
    }
}
