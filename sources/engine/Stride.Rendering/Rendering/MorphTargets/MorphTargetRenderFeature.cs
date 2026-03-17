// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Threading;
using Stride.Rendering.Materials;

namespace Stride.Rendering
{
    public class MorphTargetRenderFeature : SubRenderFeature
    {
        private StaticObjectPropertyKey<RenderEffect> renderEffectKey;
        private StaticObjectPropertyKey<bool> morphInfoKey;
        private ObjectPropertyKey<float[]> morphWeightsDataKey;
        private ConstantBufferOffsetReference morphWeightsOffset;
        private ConstantBufferOffsetReference morphTargetActiveCountOffset;

        protected override void InitializeCore()
        {
            morphWeightsDataKey = RootRenderFeature.RenderData.CreateObjectKey<float[]>();
            morphInfoKey = RootRenderFeature.RenderData.CreateStaticObjectKey<bool>();
            renderEffectKey = ((RootEffectRenderFeature)RootRenderFeature).RenderEffectKey;
            morphWeightsOffset = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawCBufferOffsetSlot(TransformationMorphTargetsKeys.MorphWeights.Name);
            morphTargetActiveCountOffset = ((RootEffectRenderFeature)RootRenderFeature).CreateDrawCBufferOffsetSlot(TransformationMorphTargetsKeys.MorphTargetActiveCount.Name);
        }

        public override void PrepareEffectPermutations(RenderDrawContext context)
        {
            var renderEffects = RootRenderFeature.RenderData.GetData(renderEffectKey);
            int effectSlotCount = ((RootEffectRenderFeature)RootRenderFeature).EffectPermutationSlotCount;

            Dispatcher.ForEach(RootRenderFeature.ObjectNodeReferences, objectNodeReference =>
            {
                var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
                var renderMesh = (RenderMesh)objectNode.RenderObject;
                var staticObjectNode = renderMesh.StaticObjectNode;

                if (renderMesh.Mesh.MorphTargets == null)
                    return;

                var parameters = renderMesh.Mesh.Parameters;
                var hasMorphTargets = parameters.Get(MaterialKeys.HasMorphTargets);
                var hasMorphTargetNormals = parameters.Get(MaterialKeys.HasMorphTargetNormals);
                var hasMorphTargetTangents = parameters.Get(MaterialKeys.HasMorphTargetTangents);
                var morphTargetMaxCount = parameters.Get(MaterialKeys.MorphTargetMaxCount);

                for (int i = 0; i < effectSlotCount; ++i)
                {
                    var staticEffectObjectNode = staticObjectNode * effectSlotCount + i;
                    var renderEffect = renderEffects[staticEffectObjectNode];
                    if (renderEffect == null || !renderEffect.IsUsedDuringThisFrame(RenderSystem))
                        continue;

                    renderEffect.EffectValidator.ValidateParameter(MaterialKeys.HasMorphTargets, hasMorphTargets);
                    renderEffect.EffectValidator.ValidateParameter(MaterialKeys.HasMorphTargetNormals, hasMorphTargetNormals);
                    renderEffect.EffectValidator.ValidateParameter(MaterialKeys.HasMorphTargetTangents, hasMorphTargetTangents);
                    renderEffect.EffectValidator.ValidateParameter(MaterialKeys.MorphTargetMaxCount, morphTargetMaxCount);
                }
            });
        }

        public override void Extract()
        {
            var morphWeightsData = RootRenderFeature.RenderData.GetData(morphWeightsDataKey);

            Dispatcher.ForEach(RootRenderFeature.ObjectNodeReferences, objectNodeReference =>
            {
                var objectNode = RootRenderFeature.GetObjectNode(objectNodeReference);
                var renderMesh = (RenderMesh)objectNode.RenderObject;
                morphWeightsData[objectNodeReference] = renderMesh.MorphWeights;
            });
        }

        public override unsafe void Prepare(RenderDrawContext context)
        {
            var morphWeightsData = RootRenderFeature.RenderData.GetData(morphWeightsDataKey);

            Dispatcher.ForBatched(RootRenderFeature.RenderNodes.Count, (from, toExclusive) =>
            {
                for (int i = from; i < toExclusive; i++)
                {
                    var renderNode = RootRenderFeature.RenderNodes[i];
                    var perDrawLayout = renderNode.RenderEffect.Reflection?.PerDrawLayout;
                    if (perDrawLayout == null)
                        continue;

                    var renderMesh = (RenderMesh)renderNode.RenderObject;
                    if (renderMesh.Mesh?.MorphTargets == null)
                        continue;

                    var morphWeights = morphWeightsData[renderNode.RenderObject.ObjectNode];
                    var activeCount = (morphWeights != null) ? morphWeights.Length : 0;

                    // Upload morph weights array — each float occupies a full 16-byte cbuffer register
                    var weightsOff = perDrawLayout.GetConstantBufferOffset(morphWeightsOffset);
                    if (weightsOff != -1)
                    {
                        var ptr = (byte*)renderNode.Resources.ConstantBuffer.Data + weightsOff;
                        for (int t = 0; t < activeCount; t++)
                        {
                            *(float*)(ptr + t * 16) = morphWeights[t];
                        }
                    }

                    // Upload active count
                    var countOff = perDrawLayout.GetConstantBufferOffset(morphTargetActiveCountOffset);
                    if (countOff != -1)
                    {
                        var ptr = (byte*)renderNode.Resources.ConstantBuffer.Data + countOff;
                        *(int*)ptr = activeCount;
                    }
                }
            });
        }
    }
}
