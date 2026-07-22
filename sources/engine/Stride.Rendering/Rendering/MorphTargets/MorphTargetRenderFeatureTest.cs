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
        public Texture NormalTexture;
        public bool Initialized;
    }

    [DataMember] public bool EnableTest { get; set; } = true;

    private readonly Dictionary<Mesh, MorphInfo> _infos = [];
    private StaticObjectPropertyKey<RenderEffect> _renderEffectKey;

    private ConstantBufferOffsetReference _weightOffset;
    private ConstantBufferOffsetReference _weightsOffset;

    private ConstantBufferOffsetReference _targetCountOffset;

    private ConstantBufferOffsetReference _vertexCountOffset;
    private LogicalGroupReference _morphLogicalGroup;

    private float _testWeight = 0f;
    private float[] _testWeights = new float[64];

    private float _weightDirection = 1f;

    private ObjectPropertyKey<float> _morphWeightKey;
    private ObjectPropertyKey<float[]> _morphWeightKeys;

    private Logger Logger => GlobalLogger.GetLogger(nameof(MorphTargetRenderFeatureTest));

    protected override void InitializeCore()
    {
        _morphLogicalGroup = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawLogicalGroup("MorphTargetsTest");
        _renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;

        _weightOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestWeight");
        _weightsOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestWeights");

        _targetCountOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestTargetCount");

        _vertexCountOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestVertexCount");

        _morphWeightKey = RootRenderFeature.RenderData.CreateObjectKey<float>();
        _morphWeightKeys = RootRenderFeature.RenderData.CreateObjectKey<float[]>();
    }

    public override void Extract()
    {
        var morphWeightData = RootRenderFeature.RenderData.GetData(_morphWeightKey);
        var morphWeightDatas = RootRenderFeature.RenderData.GetData(_morphWeightKeys);

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;
            if (mesh.MorphTargets.VertexCount > 16384) continue;

            float weight = EnableTest ? _testWeight : mesh.Parameters.Get(MorphTargetKeys.Weight);
            float[] weights = EnableTest ? _testWeights : mesh.Parameters.Get(MorphTargetKeys.Weights);

            morphWeightData[objectNodeReference] = weight;
            morphWeightDatas[objectNodeReference] = weights;
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
        var morphWeightDatas = RootRenderFeature.RenderData.GetData(_morphWeightKeys);

        if (EnableTest)
        {
            float dt = (float)context.RenderContext.Time.Elapsed.TotalSeconds;
            _testWeight += dt * _weightDirection * 0.5f;

            if (_testWeight >= 1f) 
            { 
                _testWeight = 1f; 
                _weightDirection = -1f; 
            }

            else if (_testWeight <= 0f) 
            {
                _testWeight = 0f; 
                _weightDirection = 1f; 
            }
            //Logger.Info($"Prepare: TestWeight={_testWeight:F3}");

            // for all targets
            for (int i = 0; i < _testWeights.Length; i++)
                _testWeights[i] = _testWeight;
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
                //Array.Copy(def.PositionDeltaData, 0, firstTargetData, 0, floatCount); // this for one test point 

                info.PositionTexture = Texture.New2D(
                    context.GraphicsDevice,
                    def.VertexCount,
                    //1, // one shape count
                    def.MorphTargetCount, // bit of shapes
                    PixelFormat.R32G32B32A32_Float,
                    TextureFlags.ShaderResource,
                    1,
                    GraphicsResourceUsage.Default);

                //info.PositionTexture.SetData(context.CommandList, firstTargetData, 0, 0); // copying only one target
                info.PositionTexture.SetData(context.CommandList, def.PositionDeltaData); // copying only one target

                // Нормали — если есть данные
                if (def.NormalDeltaData != null && def.NormalDeltaData.Length > 0)
                {
                    info.NormalTexture = Texture.New2D(
                        context.GraphicsDevice,
                        def.VertexCount, 
                        def.MorphTargetCount,
                        PixelFormat.R32G32B32A32_Float,
                        TextureFlags.ShaderResource, 
                        1,
                        GraphicsResourceUsage.Default);
                    
                    info.NormalTexture.SetData(context.CommandList, def.NormalDeltaData);
                }

                info.Initialized = true;
                _infos[mesh] = info;
            }
        }

        // Запись в cbuffer
        for (int i = 0; i < RootRenderFeature.RenderNodes.Count; i++)
        {
            var renderNode = RootRenderFeature.RenderNodes[i];

            if (renderNode.RenderEffect == null) continue;
            if (!renderNode.RenderEffect.IsUsedDuringThisFrame(RenderSystem)) continue;
            var perDrawLayout = renderNode.RenderEffect.Reflection?.PerDrawLayout;
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
                //Logger.Info($"Prepare: texture bound at slot {logicalGroup.DescriptorEntryStart}");

                if (info.NormalTexture != null)
                    renderNode.Resources.DescriptorSet
                        .SetShaderResourceView(logicalGroup.DescriptorEntryStart + 1, info.NormalTexture);
            }
            else Logger.Warning("Prepare: logical group MorphTargets not found");

            var weightOff = perDrawLayout.GetConstantBufferOffset(_weightOffset);
            //Logger.Info($"Prepare cbuffer: mesh='{renderMesh.Mesh.Name}' weightOff={weightOff}");

            if (weightOff != -1)
            //for one float morph target 
            //*((float*)((byte*)renderNode.Resources.ConstantBuffer.Data + weightOff)) =
            //    EnableTest ? _testWeight : morphWeightData[renderNode.RenderObject.ObjectNode]; 

            // for array of float(float[]) morph targets
            {

                var weights = EnableTest
                    ? _testWeights
                    : morphWeightDatas[renderNode.RenderObject.ObjectNode];

                    float* dst = (float*)((byte*)renderNode.Resources.ConstantBuffer.Data + weightOff);

                    fixed (float* src = weights)
                    {
                        System.Buffer.MemoryCopy(
                            src,
                            dst,
                            64 * sizeof(float),
                            renderMesh.Mesh.MorphTargets.MorphTargetCount * sizeof(float));
                    }
            }

            // vertex count
            var countOff = perDrawLayout.GetConstantBufferOffset(_vertexCountOffset);
            //Logger.Info($"Prepare cbuffer: countOff={countOff}");

            if (countOff != -1)
            {
                var count = renderMesh.Mesh.MorphTargets.VertexCount;

                *((int*)((byte*)renderNode.Resources.ConstantBuffer.Data + countOff)) = count;
                //Logger.Info($"Prepare cbuffer: writing MorphVertexCount={count}");
            }

            // target count
            var targetCountOff = perDrawLayout.GetConstantBufferOffset(_targetCountOffset);

            if (targetCountOff != -1)
            {
                *((int*)((byte*)renderNode.Resources.ConstantBuffer.Data + targetCountOff)) =
                    renderMesh.Mesh.MorphTargets.MorphTargetCount;
            }
        }
    }
}
