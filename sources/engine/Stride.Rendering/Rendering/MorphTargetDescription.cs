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
        /// <summary>
        /// The name of this morph target (e.g., "smile", "blink_left").
        /// </summary>
        public string Name;

        /// <summary>
        /// The default weight for this morph target (usually 0).
        /// </summary>
        public float DefaultWeight;
    }
}
