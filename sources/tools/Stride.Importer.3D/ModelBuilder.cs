// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Diagnostics;
using Stride.Graphics;
using Stride.Rendering;

namespace Stride.Importer.ThreeD;

public static class ModelBuilder
{
    public static Model CreateFromFile(GraphicsDevice graphicsDevice, string pathToModel)
    {
        // convert model from sources to Stride
        var deduplicateMaterial = false;
        var log = GlobalLogger.GetLogger("Model builder");
        var meshConverter = new MeshConverter(log, graphicsDevice);
        var model = meshConverter.BuildRuntimeModel(pathToModel, deduplicateMaterial);

        // convert materials from sources to Stride
        var materials = meshConverter.BuildRuntimeMaterials(pathToModel);
        var count = materials.Count;
        for (int i = 0; i < count; i++)
        {
            model.Materials.Add(materials[i]);
        }
        return model;
    }
}
