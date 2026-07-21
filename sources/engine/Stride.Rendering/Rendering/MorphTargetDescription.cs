// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;

namespace Stride.Rendering
{
    /// <summary>
    /// Describes a single morph target (blend shape) within a mesh.
    /// </summary>
    [DataContract]
    public class MorphTargetDescription
    {
        /// <summary>Name of the morph target as exported from DCC tool.</summary>
        public string Name;
 
        /// <summary>Default weight [0..1].</summary>
        public float Weight;
    }

    /// <summary>
    /// Defines morph target (blend shape) data for a <see cref="Mesh"/>.
    ///
    /// Delta data is stored as two flat float arrays that are uploaded to
    /// <c>Texture2D&lt;float4&gt;</c> at runtime:
    ///   width  = VertexCount  (one column per vertex)
    ///   height = MorphTargetCount (one row per target)
    ///   format = R32G32B32A32_Float  (XYZ = delta, W = unused / reserved)
    ///
    /// This layout matches the SDSL shader <c>TransformationMorphTargets</c>
    /// which samples via <c>Texture.Load(int3(vertexId, targetIndex, 0))</c>.
    /// </summary>
    [DataContract]
    public class MeshMorphTargetDefinition
    {
        /// <summary>
        /// Descriptions of each morph target (name, default weight, etc.).
        /// Ordered identically to the texture rows.
        /// </summary>
        public MorphTargetDescription[] MorphTargets;
 
        /// <summary>Number of vertices in the base mesh (texture width).</summary>
        public int VertexCount;
 
        /// <summary>Whether normal deltas are stored in <see cref="NormalDeltaData"/>.</summary>
        public bool HasNormals;
 
        /// <summary>Whether tangent deltas are stored (reserved for future use).</summary>
        public bool HasTangents;
 
        // ----------------------------------------------------------------
        //  Texture source data  (serialised into the asset)
        //  Layout: [targetIndex * VertexCount + vertexIndex] → float4
        //  i.e. row-major with rows = targets, columns = vertices.
        // ----------------------------------------------------------------
 public int SlicesPerTarget;
        /// <summary>
        /// Flat position-delta data for all targets × vertices.
        /// Each element is XYZW where W is unused (always 0).
        /// Length == MorphTargetCount * VertexCount * 4.
        /// </summary>
        public float[] PositionDeltaData;
        //public Core.Mathematics.Half[] PositionDeltaData;
 
        /// <summary>
        /// Flat normal-delta data. Same layout as <see cref="PositionDeltaData"/>.
        /// Null when <see cref="HasNormals"/> is false.
        /// </summary>
        public float[] NormalDeltaData;
        //public Core.Mathematics.Half[] NormalDeltaData;
 
        /// <summary>Gets the number of morph targets.</summary>
        public int MorphTargetCount => MorphTargets?.Length ?? 0;
    }
}
