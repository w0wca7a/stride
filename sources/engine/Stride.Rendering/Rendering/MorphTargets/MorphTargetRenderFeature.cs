// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using Stride.Core.Diagnostics;
using Stride.Graphics;
using Stride.Rendering.Materials;
using Stride.Core.Storage;

namespace Stride.Rendering;

public class MorphTargetRenderFeature : SubRenderFeature
{
    private struct MorphInfo
    {
        public Texture PositionTexture;
        public bool Initialized;
    }

    private readonly Dictionary<Mesh, MorphInfo> _infos = [];
    private StaticObjectPropertyKey<RenderEffect> _renderEffectKey;
    private ConstantBufferOffsetReference _weightOffset;
    private ConstantBufferOffsetReference _vertexCountOffset;
    private ConstantBufferOffsetReference _targetCountOffset;
    private ConstantBufferOffsetReference _slicesPerTargetOffset;
    private LogicalGroupReference _morphLogicalGroup;
    private float _testWeight = 0f;
    private float _weightDirection = 1f;
    private long _lastUpdateFrame = -1;

    private Logger Logger => GlobalLogger.GetLogger(nameof(MorphTargetRenderFeature));

    protected override void InitializeCore()
    {
        _morphLogicalGroup = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawLogicalGroup("MorphTargets");

        _renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;

        _weightOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargets.MorphWeight");

        _vertexCountOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargets.MorphVertexCount");

        _targetCountOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargets.MorphTargetCount");

        _slicesPerTargetOffset = ((RootEffectRenderFeature)RootRenderFeature)
            .CreateDrawCBufferOffsetSlot("TransformationMorphTargets.MorphSlicesPerTarget");
    }

    public override void Extract()
    {
        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;
            //if (mesh.MorphTargets.VertexCount > 16384) continue;

            // Устанавливаем флаг до PrepareEffectPermutations
            mesh.Parameters.Set(MaterialKeys.HasMorphTargets, true);
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
            //if (mesh.MorphTargets.VertexCount > 16384) continue;

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

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        long currentFrame = context.RenderContext.Time.FrameCount;
        if (currentFrame == _lastUpdateFrame) return;
        _lastUpdateFrame = currentFrame;

        float dt = (float)context.RenderContext.Time.Elapsed.TotalSeconds;
        _testWeight += dt * _weightDirection * 0.5f;
        if (_testWeight >= 1f) { _testWeight = 1f; _weightDirection = -1f; }
        else if (_testWeight <= 0f) { _testWeight = 0f; _weightDirection = 1f; }

        //Logger.Info($"Draw: TestWeight={_testWeight:F3}");
    }

    public override unsafe void Prepare(RenderDrawContext context)
    {
        //Logger.Info($"Prepare: TestWeight={_testWeight:F3}");

        // Ленивое создание текстур
        foreach (var objectNodeReference in RootRenderFeature.ObjectNodeReferences)
        {
            var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
            var renderMesh = (RenderMesh)objectNode.RenderObject;
            var mesh = renderMesh?.Mesh;
            if (mesh?.MorphTargets == null) continue;

            if (mesh.MorphTargets.PositionDeltas == null || mesh.MorphTargets.PositionDeltas.Length == 0) continue;

            if (!_infos.TryGetValue(mesh, out var info))
            {
                var def = mesh.MorphTargets;
                int numVertices = def.VertexCount;
                int numTargets = def.MorphTargetCount;
                /*
                info.PositionTexture = Texture.New2D(
                    context.GraphicsDevice,     //device
                    //width,                      //width
                    16384,                      //width
                    1,                          //heigt
                    PixelFormat.R32G32B32A32_Float, //PixelFormat
                    TextureFlags.ShaderResource,    //TextureFlags.ShaderResource
                    totalSlices,                    //int arraySize = 1
                    GraphicsResourceUsage.Default); //GraphicsResourceUsage usage = GraphicsResourceUsage.Default, TextureOptions options = TextureOptions.None

                // Заливаем все таргеты по слайсам
                for (int t = 0; t < numTargets; t++)
                {
                    for (int s = 0; s < slicesPerTarget; s++)
                    {
                        int sliceIndex = t * slicesPerTarget + s;
                        int vertexStart = s * 16384;
                        int vertexCount = Math.Min(16384, numVertices - vertexStart);

                        // Паддинг до 16384 чтобы не было проблем с неполным последним слайсом
                        var sliceData = new float[16384 * 4];
                        Array.Copy(
                            def.PositionDeltas,
                            t * numVertices * 4 + vertexStart * 4,
                            sliceData, 0,
                            vertexCount * 4);

                        info.PositionTexture.SetData(context.CommandList, sliceData, sliceIndex, 0);
                    }
                }
                */
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

            if (!_infos.TryGetValue(renderMesh.Mesh, out var info)) continue;
    
            var logicalGroup = perDrawLayout.GetLogicalGroup(_morphLogicalGroup);
            if (logicalGroup.Hash != ObjectId.Empty)
            {
                renderNode.Resources.DescriptorSet.SetShaderResourceView(
                    logicalGroup.DescriptorEntryStart + 0, info.PositionTexture);
                //Logger.Info($"Prepare: texture bound at slot {logicalGroup.DescriptorEntryStart}");
            }
            else Logger.Warning("Prepare: logical group MorphTargets not found");

            var def = renderMesh.Mesh.MorphTargets;

            // MorphWeights — float4[16] = 64 float
            var weightsOff = perDrawLayout.GetConstantBufferOffset(_weightOffset);
            //Logger.Info($"Prepare cbuffer: mesh='{renderMesh.Mesh.Name}' weightsOff={weightsOff}");
            if (weightsOff != -1)
            {
                float* weightsPtr = (float*)((byte*)renderNode.Resources.ConstantBuffer.Data + weightsOff);
                for (int t = 0; t < def.MorphTargetCount && t < 64; t++)
                    weightsPtr[t] = _testWeight;
            }

            var targetCountOff = perDrawLayout.GetConstantBufferOffset(_targetCountOffset);
            if (targetCountOff != -1)
                *((int*)((byte*)renderNode.Resources.ConstantBuffer.Data + targetCountOff)) = def.MorphTargetCount;

            var vertexCountOff = perDrawLayout.GetConstantBufferOffset(_vertexCountOffset);
            if (vertexCountOff != -1)
                *((int*)((byte*)renderNode.Resources.ConstantBuffer.Data + vertexCountOff)) = def.VertexCount;

            //Logger.Info($"Prepare cbuffer: MorphTargetCount={def.MorphTargetCount} MorphVertexCount={def.VertexCount} SlicesPerTarget={def.SlicesPerTarget}");
        }
    }
}
