// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Silk.NET.Assimp;
using Stride.Importer.ThreeD.Material;
using Stride.Core.Mathematics;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Texture = Stride.Graphics.Texture;

namespace Stride.Importer.ThreeD;

public partial class MeshConverter
{
    internal unsafe List<Rendering.Material> BuildRuntimeMaterials(string pathToFile)
    {
        uint importFlags = 0;
        var scene = Initialize(pathToFile, null, importFlags, 0);

        if (scene == null)
        {
            var error = assimp.GetErrorStringS();
            if (error.Length > 0)
            {
                Logger.Error($"Assimp: {error}");
            }

            return null;
        }

        var materials = ExtractMaterials(scene);
        return materials;
    }

    private unsafe List<Rendering.Material> ExtractMaterials(Scene* scene)
    {
        var materials = new List<Rendering.Material>();
        for (uint i = 0; i < scene->MNumMaterials; i++)
        {
            var pMaterial = scene->MMaterials[i];
            var descriptor = new MaterialDescriptor
            {
                Attributes = new MaterialAttributes()
            };

            // Diffuse
            var diffuseStack = Materials.ConvertAssimpStackCppToCs(assimp, scene, pMaterial, TextureType.Diffuse, Logger);
            var diffuseNode = BuildRuntimeComputeColor(diffuseStack, scene);
            if (diffuseNode != null)
            {
                descriptor.Attributes.Diffuse = new MaterialDiffuseMapFeature(diffuseNode);
                descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
            }

            // Surface/Normals
            var normalStack = Materials.ConvertAssimpStackCppToCs(assimp, scene, pMaterial, TextureType.Normals, Logger);
            var normalNode = BuildRuntimeComputeColor(normalStack, scene);
            if (normalNode != null)
                descriptor.Attributes.Surface = new MaterialNormalMapFeature(normalNode);

            // Specular
            // TODO: Metallness
            var specularStack = Materials.ConvertAssimpStackCppToCs(assimp, scene, pMaterial, TextureType.Specular, Logger);
            var specularNode = BuildRuntimeComputeColor(specularStack, scene);
            if (specularNode != null)
            {
                descriptor.Attributes.Specular = new MaterialSpecularMapFeature { SpecularMap = specularNode };
                descriptor.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature
                {
                    Fresnel = new MaterialSpecularMicrofacetFresnelSchlick(),
                    Visibility = new MaterialSpecularMicrofacetVisibilityImplicit(),
                    NormalDistribution = new MaterialSpecularMicrofacetNormalDistributionBlinnPhong()
                };
            }

            // TODO: Microsurface/Gloss
            /*
            var glossStack = Materials.ConvertAssimpStackCppToCs(assimp, scene, pMaterial, TextureType.Shininess, Logger);
            var glossNode = BuildRuntimeComputeColor(glossStack, scene);
            if (glossNode != null)
            {
                descriptor.Attributes.MicroSurface = new MaterialGlossinessMapFeature { GlossinessMap = };
            }
            */

            var material = Rendering.Material.New(graphicsDevice, descriptor);
            materials.Add(material);
        }

        return materials;
    }

    private unsafe IComputeColor BuildRuntimeComputeColor(MaterialStack stack, Scene* scene)
    {
        if (stack.IsEmpty) return null;

        var top = stack.Pop();

        return top switch
        {
            StackColor c => new ComputeColor(
                new Color4(c.Color.R, c.Color.G, c.Color.B, c.Alpha)
            ),

            StackTexture t => LoadRuntimeTexture(t.TexturePath, scene) is { } texture
                ? new ComputeTextureColor(texture)
                {
                    AddressModeU = ConvertTextureMode(t.MappingModeU),
                    AddressModeV = ConvertTextureMode(t.MappingModeV),
                    TexcoordIndex = t.Channel == 0
                        ? TextureCoordinate.Texcoord0
                        : TextureCoordinate.Texcoord1
                }
                : null,

            _ => null
        };
    }

    private unsafe Texture LoadRuntimeTexture(string texturePath, Scene* scene)
    {
        try
        {
            // Embedded texture (*0, *1, ...)
            if (texturePath.StartsWith("*") && int.TryParse(texturePath.Substring(1), out int texIndex))
            {
                var tex = scene->MTextures[texIndex];
                var bytes = new byte[tex->MWidth];
                fixed (byte* dst = bytes)
                    System.Buffer.MemoryCopy(tex->PcData, dst, bytes.Length, bytes.Length);

                using var ms = new MemoryStream(bytes);
                var image = Graphics.Image.Load(ms);
                return Texture.New(graphicsDevice, image);
            }

            var dir = Path.GetDirectoryName(vfsInputFilename);
            var fullPath = Path.Combine(dir, texturePath);
            if (System.IO.File.Exists(fullPath))
            {
                using var fs = System.IO.File.OpenRead(fullPath);
                var image = Graphics.Image.Load(fs);
                return Texture.New(graphicsDevice, image);
            }

            Logger.Warning($"Texture not found: {fullPath}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load texture '{texturePath}': {ex.Message}");
            return null;
        }
    }
}
