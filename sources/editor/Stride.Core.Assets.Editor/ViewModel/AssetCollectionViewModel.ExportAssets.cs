// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Stride.Assets.Entities;
using Stride.Assets.Materials;
using Stride.Core.Assets.Analysis;
using Stride.Core.Assets.Editor.Components.Status;
using Stride.Core.IO;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Services;
using Stride.Core.Presentation.ViewModels;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace Stride.Core.Assets.Editor.ViewModel
{
    public partial class AssetCollectionViewModel
    {

        public ICommandBase ExportAssetsCommand { get; private set; }

        private void InitializeExportCommand(IViewModelServiceProvider serviceProvider)
        {
            ExportAssetsCommand = new AnonymousTaskCommand(serviceProvider, ExecuteExportAssets);
        }

        private async Task ExecuteExportAssets()
        {
            var assets = SelectedAssets.ToList();
            if (assets.Count == 0)
                return;

            var dialogService = ServiceProvider.Get<IDialogService>();

            var initialDir = assets[0].AssetItem.FullPath.GetFullDirectory();
            var filePath = await dialogService.SaveFilePickerAsync(
                initialPath: initialDir,
                filters: [new FilePickerFilter("Stride asset package") { Patterns = ["*.zip"] }],
                defaultExtension: ".zip",
                defaultFileName: assets[0].AssetItem.Location.GetFileNameWithoutExtension() + ".zip");

            if (filePath is null)
                return;

            var warnings = new List<string>();
            //var status = Session. Status;
            var status = EditorViewModel.Instance.Status;

            // Запускаем индикатор — indeterminate пока не знаем сколько файлов
            var jobToken = status.NotifyBackgroundJobStarted("Exporting assets... {0}", JobPriority.Background);
            var statusToken = status.PushStatus("Exporting assets...");
            try
            {
                await Task.Run(() =>
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);

                    using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);

                    var allAssets = new HashSet<AssetItem>();
                    foreach (var assetViewModel in assets)
                    {
                        var root = assetViewModel.AssetItem;
                        if (root != null)
                            CollectDependencies(root, allAssets, warnings);
                    }

                    var packageRoot = assets[0].AssetItem.Package?.RootDirectory.ToOSPath();

                    var totalSteps = allAssets.Count;
                    status.NotifyBackgroundJobProgress(jobToken, 0, true);

                    WriteManifest(zip, assets, allAssets);

                    var current = 0;
                    var processedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var asset in allAssets)
                    {
                        // .sdtex / .sdm3d / .sdasset и т.д.
                        var assetPath = asset.FullPath.ToOSPath();
                        if (File.Exists(assetPath))
                        {
                            var rel = Path.GetRelativePath(packageRoot, assetPath);
                            zip.CreateEntryFromFile(assetPath, rel);
                        }

                        // Исходные файлы (FBX, PNG, WAV...)
                        foreach (var (fullPath, relPath) in GetSourceFiles(asset, packageRoot, warnings))
                        {
                            if (!processedSources.Add(fullPath)) continue;
                            zip.CreateEntryFromFile(fullPath, relPath);
                        }    

                        status.NotifyBackgroundJobProgress(jobToken, ++current, true);
                    }

                    // get scripts
                    var processedScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (scriptFile, scriptRoot) in GetScriptFiles(allAssets, Session, warnings))
                    {
                        if (!processedScripts.Add(scriptFile)) continue;
                        // Путь внутри архива — всегда относительно корня того пакета откуда скрипт
                        var rel = Path.GetRelativePath(scriptRoot, scriptFile);
                        zip.CreateEntryFromFile(scriptFile, Path.Combine("Scripts", rel));
                    }

                    // get shaders
                    var processedShaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var shaderFile in GetShaderFiles(allAssets, Session, warnings))
                    {
                        if (!processedShaders.Add(shaderFile)) continue;
                        zip.CreateEntryFromFile(shaderFile, Path.Combine("Effects", Path.GetFileName(shaderFile)));
                    }
                });
            }
            finally
            {
                status.NotifyBackgroundJobFinished(jobToken);
                status.PopStatus(statusToken);
            }
            if (warnings.Count > 0)
            {
                await dialogService.MessageBoxAsync(
    "Export completed with warnings:\n\n" + string.Join("\n", warnings),
    MessageBoxButton.OK,
    MessageBoxImage.Warning);
            }
        }
        // -------------------------------------------------------------------------
        // Зависимости
        // -------------------------------------------------------------------------

        private void CollectDependencies(
            AssetItem root,
            HashSet<AssetItem> visited,
            List<string> warnings)
        {
            if (!visited.Add(root))
                return;

            var deps = dependencyManager.ComputeDependencies(
                root.Id,
                AssetDependencySearchOptions.Out,
                ContentLinkType.Reference);

            if (deps == null)
                return;

            foreach (var link in deps.LinksOut)
            {
                var item = Session.AllAssets.FirstOrDefault(a => a.Id == link.Item.Id)?.AssetItem;
                if (item != null)
                    CollectDependencies(item, visited, warnings);
            }

            foreach (var broken in deps.BrokenLinksOut)
                warnings.Add($"Broken reference in '{root.Location}': {broken.Element.Id}");
        }

        // -------------------------------------------------------------------------
        // Исходные файлы ассетов (FBX, PNG, WAV...)
        // -------------------------------------------------------------------------

        private static IEnumerable<(string fullPath, string relPath)> GetSourceFiles(
            AssetItem asset,
            string packageRoot,
            List<string> warnings)
        {
            UFile? source = asset.Asset switch
            {
                AssetWithSource aws => aws.Source,
                IAssetWithSource iaws => iaws.Source,
                _ => null
            };

            if (source == null || UPath.IsNullOrEmpty(source))
                yield break;

            var fullPath = UPath.Combine(asset.Package.RootDirectory, source).ToOSPath();

            if (!File.Exists(fullPath))
            {
                warnings.Add($"Source file not found: {fullPath}");
                yield break;
            }

            // Файл внутри проекта — сохраняем относительный путь
            if (fullPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield return (fullPath, Path.GetRelativePath(packageRoot, fullPath));
                yield break;
            }

            // Файл вне проекта — кладём в Resources/External/ по имени файла
            warnings.Add($"External file included: {fullPath}");
            //yield return (fullPath, Path.Combine("Resources", "External", Path.GetFileName(fullPath)));
            var hash = Math.Abs(fullPath.GetHashCode()).ToString("x8");
            yield return (fullPath, Path.Combine("Resources", "External",
                $"{Path.GetFileNameWithoutExtension(fullPath)}_{hash}{Path.GetExtension(fullPath)}"));
        }

        // -------------------------------------------------------------------------
        // Скрипты
        // -------------------------------------------------------------------------

        private static IEnumerable<(string scriptFile, string scriptRoot)> GetScriptFiles(
    IEnumerable<AssetItem> allAssets,
    SessionViewModel session,
    List<string> warnings)
        {
            // Все пакеты с их корневыми папками
            var packageDirs = session.AllPackages
                .Select(p => p.Package.RootDirectory.ToOSPath())
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var scriptTypes = new HashSet<Type>();
            foreach (var asset in allAssets)
            {
                IEnumerable<EntityDesign> parts = asset.Asset switch
                {
                    SceneAsset scene => scene.Hierarchy.Parts.Values,
                    PrefabAsset prefab => prefab.Hierarchy.Parts.Values,
                    _ => []
                };

                foreach (var part in parts)
                    foreach (var component in part.Entity.Components)
                    {
                        var type = component.GetType();
                        if (!type.Assembly.GetName().Name!.StartsWith("Stride.") && IsScriptComponent(type))
                            scriptTypes.Add(type);
                    }
            }
            /*
            foreach (var type in scriptTypes)
            {
                var simpleName = type.Name.Split('.').Last();

                // Ищем в каждом пакете отдельно — чтобы знать корень
                var found = false;
                foreach (var dir in packageDirs)
                {
                    var files = Directory.GetFiles(dir, $"{simpleName}*.cs", SearchOption.AllDirectories);
                    if (files.Length == 0) continue;

                    found = true;
                    foreach (var file in files)
                        yield return (file, dir); // возвращаем файл + корень пакета
                }

                if (!found)
                    warnings.Add($"Script source not found: {type.FullName}");
            }
            */
            foreach (var type in scriptTypes)
            {
                var simpleName = type.Name.Split('.').Last();

                // Точный путь к сборке где скомпилирован тип
                var assemblyLocation = type.Assembly.Location;

                // Ищем только в том packageDir который является родительским для этой сборки
                var targetDir = packageDirs.FirstOrDefault(dir =>
                    assemblyLocation.StartsWith(dir, StringComparison.OrdinalIgnoreCase));

                var searchDirs = targetDir != null ? [targetDir] : packageDirs;

                var found = false;
                foreach (var dir in searchDirs)
                {
                    var files = Directory.GetFiles(dir, $"{simpleName}*.cs", SearchOption.AllDirectories);
                    if (files.Length == 0) continue;

                    found = true;
                    foreach (var file in files)
                        yield return (file, dir);
                }

                if (!found)
                    warnings.Add($"Script source not found: {type.FullName}");
            }
        }

        // -------------------------------------------------------------------------
        // Шейдеры
        // -------------------------------------------------------------------------

        private static IEnumerable<string> GetShaderFiles(
    IEnumerable<AssetItem> allAssets,
    SessionViewModel session,
    List<string> warnings)
        {
            var searchDirs = session.AllPackages
                .Select(p => p.Package.RootDirectory.ToOSPath())
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var asset in allAssets)
            {
                if (asset.Asset is not MaterialAsset material) continue;

                foreach (var mixin in CollectMixinReferences(material.Attributes))
                {
                    var files = searchDirs
                        .SelectMany(d => Directory.GetFiles(d, $"{mixin}.sdsl", SearchOption.AllDirectories)
                            .Concat(Directory.GetFiles(d, $"{mixin}.sdfx", SearchOption.AllDirectories)))
                        .ToArray();

                    if (files.Length == 0)
                    {
                        warnings.Add($"Shader not found: {mixin}.sdsl/.sdfx");
                        continue;
                    }

                    foreach (var file in files)
                        yield return file;
                }
            }
        }

        private static IEnumerable<string> CollectMixinReferences(MaterialAttributes attributes)
        {
            foreach (var prop in attributes.GetType().GetProperties())
            {
                var feature = prop.GetValue(attributes);
                if (feature == null) continue;

                foreach (var mixin in FindMixinsDeep(feature, new HashSet<object>()))
                    yield return mixin;
            }
        }

        private static IEnumerable<string> FindMixinsDeep(object node, HashSet<object> visited)
        {
            if (node == null || !visited.Add(node)) yield break;

            // Нашли шейдерный миксин
            if (node is ComputeShaderClassColor shaderColor
                && !string.IsNullOrEmpty(shaderColor.MixinReference))
            {
                yield return shaderColor.MixinReference;
                yield break;
            }

            // Спускаемся через IComputeNode
            if (node is IComputeNode computeNode)
            {
                foreach (var child in computeNode.GetChildren(null))
                    foreach (var mixin in FindMixinsDeep(child, visited))
                        yield return mixin;
            }

            // Спускаемся через все публичные свойства (для IMaterialFeature и т.д.)
            foreach (var prop in node.GetType().GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                // Пропускаем примитивы и строки
                if (prop.PropertyType.IsPrimitive
                    || prop.PropertyType == typeof(string)
                    || prop.PropertyType.IsEnum)
                    continue;

                object? value = null;
                try { value = prop.GetValue(node); }
                catch { continue; }

                if (value == null) continue;

                foreach (var mixin in FindMixinsDeep(value, visited))
                    yield return mixin;
            }
        }

        private static IEnumerable<string> FindMixinsInNode(object node)
        {
            if (node is ComputeShaderClassColor shaderColor
                && !string.IsNullOrEmpty(shaderColor.MixinReference))
            {
                yield return shaderColor.MixinReference;
                yield break;
            }

            if (node is IComputeNode computeNode)
            {
                foreach (var child in computeNode.GetChildren(null))
                    foreach (var mixin in FindMixinsInNode(child))
                        yield return mixin;
            }
        }

        // -------------------------------------------------------------------------
        // Манифест
        // -------------------------------------------------------------------------

        private static void WriteManifest(
            ZipArchive zip,
            List<AssetViewModel> roots,
            HashSet<AssetItem> allAssets)
        {
            var manifest = new
            {
                Version = 1,
                RootAssets = roots.Select(a => new
                {
                    Id = a.Id.ToString(),
                    Location = a.Url
                }),
                AllAssets = allAssets.Select(a => new
                {
                    Id = a.Id.ToString(),
                    Location = a.Location.ToString(),
                    Type = a.Asset.GetType().Name
                })
            };

            var entry = zip.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true }));
        }

        private static bool IsScriptComponent(Type type)
        {
            var t = type;
            while (t != null)
            {
                if (t.Name == "ScriptComponent") return true;
                t = t.BaseType;
            }
            return false;
        }
    }
}
