using System;
using System.Collections.Generic;
using Stride.Core.Diagnostics;
using Stride.Graphics;
using Stride.Rendering.Materials;
using Stride.Core.Storage;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Rendering.ComputeEffect;

namespace Stride.Rendering;

public class MorphTargetRenderFeatureTest : SubRenderFeature
{
    private struct MorphInfo
    {
        //public Texture PositionTexture;
        //public Texture NormalTexture;
        public Graphics.Buffer PositionDeltaBuffer;
        public Graphics.Buffer NormalDeltaBuffer;
        public Graphics.Buffer MorphedPositionsBuffer;
        public Graphics.Buffer MorphedNormalsBuffer;
        public bool Initialized;
    }

    private ComputeEffectShader _computeShader;
    private readonly Dictionary<Mesh, MorphInfo> _infos = [];
    private StaticObjectPropertyKey<RenderEffect> _renderEffectKey;

    private ConstantBufferOffsetReference _weightsOffset;

    private ConstantBufferOffsetReference _targetCountOffset;

    private ConstantBufferOffsetReference _vertexCountOffset;
    private LogicalGroupReference _morphLogicalGroup;

    private float[] _testWeights = new float[64];
    private Vector4[] _packedWeights = new Vector4[16];

    private float _weightDirection = 1f;

    private ObjectPropertyKey<float[]> _morphWeightKeys;

    private Logger Logger => GlobalLogger.GetLogger(nameof(MorphTargetRenderFeatureTest));

    protected override void InitializeCore()
    {
        _morphLogicalGroup = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawLogicalGroup("MorphTargetsTest");
        _renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;

        _weightsOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestWeights");

        _targetCountOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestTargetCount");

        _vertexCountOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargetsTest.MorphTestVertexCount");

        _morphWeightKeys = RootRenderFeature.RenderData.CreateObjectKey<float[]>();

        var renderContext = RenderContext.GetShared(RenderSystem.Services);
        _computeShader = new ComputeEffectShader(renderContext) { ShaderSourceName = "ComputeTransformationMorphTargets" };
    }

    public override void Extract()
    {
        var morphWeightDatas = RootRenderFeature.RenderData.GetData(_morphWeightKeys);

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;

            float[] weights = mesh.Parameters.Get(MorphTargetKeys.Weights);

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
        var morphWeightDatas = RootRenderFeature.RenderData.GetData(_morphWeightKeys);

        // Lazy creation
        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;
            if (mesh.MorphTargets.PositionDeltaData == null ||
                mesh.MorphTargets.PositionDeltaData.Length == 0) continue;

            if (!_infos.TryGetValue(mesh, out var info))
            {
                var def = mesh.MorphTargets;

                var posData = def.PositionDeltaData;
                var posVectors = new Vector4[posData.Length / 4];
                for (int k = 0; k < posVectors.Length; k++)
                    posVectors[k] = new Vector4(posData[k * 4], posData[k * 4 + 1], posData[k * 4 + 2], posData[k * 4 + 3]);

                info.PositionDeltaBuffer = Graphics.Buffer.New(
                    context.GraphicsDevice,
                    posVectors,
                    BufferFlags.StructuredBuffer | BufferFlags.ShaderResource,
                    GraphicsResourceUsage.Dynamic);

                // Normals
                if (def.NormalDeltaData != null && def.NormalDeltaData.Length > 0)
                {
                    var norData = def.NormalDeltaData;
                    var norVectors = new Vector4[norData.Length / 4];
                    for (int k = 0; k < norVectors.Length; k++)
                        norVectors[k] = new Vector4(norData[k * 4], norData[k * 4 + 1], norData[k * 4 + 2], norData[k * 4 + 3]);

                    info.NormalDeltaBuffer = Graphics.Buffer.New(
                            context.GraphicsDevice,
                            norVectors,
                            BufferFlags.StructuredBuffer | BufferFlags.ShaderResource,
                            GraphicsResourceUsage.Dynamic);
                }

                // Выходные буферы (UAV для compute, SRV для vertex shader)
                info.MorphedPositionsBuffer = Graphics.Buffer.New<Vector4>(
                    context.GraphicsDevice,
                    def.VertexCount,
                    BufferFlags.StructuredBuffer | BufferFlags.ShaderResource | BufferFlags.UnorderedAccess,
                    GraphicsResourceUsage.Default);

                info.MorphedNormalsBuffer = Graphics.Buffer.New<Vector4>(
                    context.GraphicsDevice,
                    def.VertexCount,
                    BufferFlags.StructuredBuffer | BufferFlags.ShaderResource | BufferFlags.UnorderedAccess,
                    GraphicsResourceUsage.Default);

                info.Initialized = true;
                _infos[mesh] = info;
            }
        }

        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;

            if (!_infos.TryGetValue(mesh, out var info)) continue;

            var weights = morphWeightDatas[objectNodeReference];

            // Packing weigths to float4[16]
            for (int i = 0; i < 16; i++)
            {
                _packedWeights[i] = new Vector4(
                    i * 4 + 0 < weights.Length ? weights[i * 4 + 0] : 0f,
                    i * 4 + 1 < weights.Length ? weights[i * 4 + 1] : 0f,
                    i * 4 + 2 < weights.Length ? weights[i * 4 + 2] : 0f,
                    i * 4 + 3 < weights.Length ? weights[i * 4 + 3] : 0f);
            }
            
            _computeShader.Parameters.Set(ComputeTransformationMorphTargetsKeys.MorphWeights, _packedWeights);
            _computeShader.Parameters.Set(ComputeTransformationMorphTargetsKeys.MorphTargetCount, ((uint)mesh.MorphTargets.MorphTargetCount));
            _computeShader.Parameters.Set(ComputeTransformationMorphTargetsKeys.VertexCount, ((uint)mesh.MorphTargets.VertexCount));
            _computeShader.Parameters.Set(ComputeTransformationMorphTargetsKeys.MorphPositionDeltas, info.PositionDeltaBuffer);
            _computeShader.Parameters.Set(ComputeTransformationMorphTargetsKeys.MorphNormalDeltas, info.NormalDeltaBuffer);
            _computeShader.Parameters.Set(ComputeTransformationMorphTargetsKeys.MorphedPositions, info.MorphedPositionsBuffer);
            _computeShader.Parameters.Set(ComputeTransformationMorphTargetsKeys.MorphedNormals, info.MorphedNormalsBuffer);
            
            int groups = (mesh.MorphTargets.VertexCount + 63) / 64;
            _computeShader.ThreadNumbers = new Int3(64, 1, 1);
            _computeShader.ThreadGroupCounts = new Int3(groups, 1, 1);
            _computeShader.Draw(context);
        }

        for (int i = 0; i < RootRenderFeature.RenderNodes.Count; i++)
        {
            var renderNode = RootRenderFeature.RenderNodes[i];

            if (renderNode.RenderEffect == null) continue;
            if (!renderNode.RenderEffect.IsUsedDuringThisFrame(RenderSystem)) continue;
            var perDrawLayout = renderNode.RenderEffect.Reflection?.PerDrawLayout;
            if (perDrawLayout == null)continue;

            var renderMesh = (RenderMesh)renderNode.RenderObject;
            if (renderMesh.Mesh?.MorphTargets == null) continue;
            
            if (!_infos.TryGetValue(renderMesh.Mesh, out var info)) continue;
            var logicalGroup = perDrawLayout.GetLogicalGroup(_morphLogicalGroup);
            if (logicalGroup.Hash != ObjectId.Empty)
            {
                renderNode.Resources.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 0, info.MorphedPositionsBuffer);
                renderNode.Resources.DescriptorSet.SetShaderResourceView(logicalGroup.DescriptorEntryStart + 1, info.MorphedNormalsBuffer);            
            }
            else Logger.Warning("Prepare: logical group MorphTargets not found");

            var weightOffs = perDrawLayout.GetConstantBufferOffset(_weightsOffset);

            if (weightOffs != -1)
            {
                var weights = morphWeightDatas[renderNode.RenderObject.ObjectNode];
                for (int m = 0; m < 16; m++)
                {
                    _packedWeights[m] = new Vector4(
                        m * 4 + 0 < weights.Length ? weights[m * 4 + 0] : 0f,
                        m * 4 + 1 < weights.Length ? weights[m * 4 + 1] : 0f,
                        m * 4 + 2 < weights.Length ? weights[m * 4 + 2] : 0f,
                        m * 4 + 3 < weights.Length ? weights[m * 4 + 3] : 0f
                    );
                }

                float* dst = (float*)((byte*)renderNode.Resources.ConstantBuffer.Data + weightOffs);
                fixed (Vector4* src = _packedWeights)
                {
                    System.Buffer.MemoryCopy(
                        src,
                        dst,
                        16 * sizeof(Vector4),
                        16 * sizeof(Vector4));
                }
            }

            // vertex count
            var countOff = perDrawLayout.GetConstantBufferOffset(_vertexCountOffset);
            
            if (countOff != -1)
            {
                var count = renderMesh.Mesh.MorphTargets.VertexCount;

                *((int*)((byte*)renderNode.Resources.ConstantBuffer.Data + countOff)) = count;                
            }

            var targetCountOff = perDrawLayout.GetConstantBufferOffset(_targetCountOffset);

            if (targetCountOff != -1)
            {
                *((int*)((byte*)renderNode.Resources.ConstantBuffer.Data + targetCountOff)) =
                    renderMesh.Mesh.MorphTargets.MorphTargetCount;
            }
        }
    }
}
