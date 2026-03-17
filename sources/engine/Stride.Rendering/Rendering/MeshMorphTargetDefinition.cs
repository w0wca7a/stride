// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Core.Serialization;
using Stride.Graphics;

namespace Stride.Rendering
{
    /// <summary>
    /// Defines morph target (blend shape) data for a <see cref="Mesh"/>.
    /// Delta data is stored as per-target vertex buffers bound with MORPHDELTA, MORPHNRMDELTA,
    /// and MORPHTANDELTA semantics, appended to the mesh's vertex buffer array.
    /// </summary>
    [DataContract]
    public class MeshMorphTargetDefinition
    {
        /// <summary>
        /// Descriptions of each morph target (name, default weight, etc.).
        /// </summary>
        public MorphTargetDescription[] MorphTargets;

        /// <summary>
        /// The number of vertices in the base mesh.
        /// </summary>
        public int VertexCount;

        /// <summary>
        /// Whether normal deltas are available (stored as vertex buffers with MORPHNRMDELTA semantics).
        /// </summary>
        public bool HasNormals;

        /// <summary>
        /// Whether tangent deltas are available (stored as vertex buffers with MORPHTANDELTA semantics).
        /// </summary>
        public bool HasTangents;

        /// <summary>
        /// Gets the number of morph targets.
        /// </summary>
        public int MorphTargetCount => MorphTargets?.Length ?? 0;
    }
}
