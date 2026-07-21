using System;
using System.Collections.Generic;
using Stride.Core.Diagnostics;
using Stride.Graphics;
using Stride.Rendering.Materials;
using Stride.Core.Storage;
using Stride.Core;

namespace Stride.Rendering;

public class MorphTargetRenderFeatureTest : SubRenderFeature
{
    private struct MorphInfo
    {
        public Texture PositionTexture;
        public bool Initialized;
    }

    [DataMember] public bool EnableTest { get; set; } = true;

    private readonly Dictionary<Mesh, MorphInfo> _infos = [];
    private StaticObjectPropertyKey<RenderEffect> _renderEffectKey;
    private ConstantBufferOffsetReference _weightOffset;
    private ConstantBufferOffsetReference _vertexCountOffset;
    private LogicalGroupReference _morphLogicalGroup;
    private float _testWeight = 0f;
    private float _weightDirection = 1f;

    private ObjectPropertyKey<float> _morphWeightKey;

    private Logger Logger => GlobalLogger.GetLogger(nameof(MorphTargetRenderFeatureTest));

    protected override void InitializeCore()
    {
        _morphLogicalGroup = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawLogicalGroup("MorphTargetsTest");
        _renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;
        _weightOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestWeight");
        _vertexCountOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestVertexCount");

        _morphWeightKey = RootRenderFeature.RenderData.CreateObjectKey<float>();
    }

    public override void Extract()
    {
        var morphWeightData = RootRenderFeature.RenderData.GetData(_morphWeightKey);

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;
            if (mesh.MorphTargets.VertexCount > 16384) continue;

            float weight = EnableTest ? _testWeight : mesh.Parameters.Get(MorphTargetKeys.Weight);
            morphWeightData[objectNodeReference] = weight;
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
            }
        }
    }

    public override unsafe void Prepare(RenderDrawContext context)
    {
        var morphWeightData = RootRenderFeature.RenderData.GetData(_morphWeightKey);

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
           

            if (!_infos.TryGetValue(renderMesh.Mesh, out var info)) continue;
            var logicalGroup = perDrawLayout.GetLogicalGroup(_morphLogicalGroup);
            if (logicalGroup.Hash != ObjectId.Empty)
            {
                renderNode.Resources.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 0, info.PositionTexture);
                Logger.Info($"Prepare: texture bound at slot {logicalGroup.DescriptorEntryStart}");
            }
            else Logger.Warning("Prepare: logical group MorphTargets not found");

            var weightOff = perDrawLayout.GetConstantBufferOffset(_weightOffset);
            Logger.Info($"Prepare cbuffer: mesh='{renderMesh.Mesh.Name}' weightOff={weightOff}");

            if (weightOff != -1)
                *((float*)((byte*)renderNode.Resources.ConstantBuffer.Data + weightOff)) =
                    EnableTest ? _testWeight : morphWeightData[renderNode.RenderObject.ObjectNode];
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
