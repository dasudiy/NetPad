using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NetPad.Common;
using NetPad.Compilation;
using NetPad.Configuration;
using NetPad.Data;
using NetPad.DotNet;
using NetPad.DotNet.References;
using NetPad.ExecutionModel.External;
using NetPad.IO;
using NetPad.Packages;
using NetPad.Presentation;
using SourceCodeCollection = NetPad.DotNet.CodeAnalysis.SourceCodeCollection;

namespace NetPad.ExecutionModel.ClientServer;

public partial class ClientServerScriptRunner
{
    private readonly ICodeParser _codeParser;
    private readonly ICodeCompiler _codeCompiler;
    private readonly IPackageProvider _packageProvider;
    private readonly Settings _settings;

    /// <summary>
    /// Contains post-setup info needed to run the script.
    /// </summary>
    /// <param name="ScriptDir">The directory that will be used to deploy the script
    /// assembly and any dependencies that are only needed by the script.</param>
    /// <param name="ScriptAssemblyFilePath">The full path to the compiled script assembly.</param>
    /// <param name="InPlaceDependencyDirectories">Directories where dependencies where not copied to one of the
    /// deployment directories and instead should be loaded from their original locations (in-place).</param>
    /// <param name="UserProgramStartLineNumber">The line number that user code starts on.</param>
    private record SetupInfo(
        DirectoryPath ScriptDir,
        FilePath ScriptAssemblyFilePath,
        string[] InPlaceDependencyDirectories,
        int UserProgramStartLineNumber);

    /// <summary>
    /// Sets up the environment the script will run in. It creates a folder structure similar to the following and
    /// deploys dependencies needed to run the script-host process and the script itself to these folders:
    /// <code>
    /// /root                   # A temp directory created for each script when the script is run
    ///     /script-host        # Contains the script-host executable
    ///     /shared-deps        # Contains all dependency assets (assemblies, binaries...etc) that are specific
    ///                           to the script-host process or are shared between script-host and the script assembly
    ///     /script             # Contains a sub-directory for each instance the user runs the script. Each sub dir
    ///                           contains the script assembly and all dependency assets that only the script assembly needs to run
    /// </code>
    /// <para>
    /// Some dependencies are copied (deployed) to this folder structure where they will be loaded from, while other
    /// dependencies will not be copied, and instead will be loaded from their original locations (in-place).
    /// </para>
    /// </summary>
    private async Task<SetupInfo?> SetupRunEnvironmentAsync(RunOptions runOptions, CancellationToken cancellationToken)
    {
        _workingDirectory.CreateIfNotExists();

        // Resolve and collect all dependencies needed by script-host and script
        _ = _appStatusMessagePublisher.PublishAsync(_script.Id, "Gathering dependencies...");
        var (dependencies, additionalCode) = await GatherDependenciesAsync(cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        // Parse and compile the script
        _ = _appStatusMessagePublisher.PublishAsync(_script.Id, "Compiling...");
        var parseCompileResult = ParseAndCompileInner(runOptions, dependencies, additionalCode, cancellationToken);
        if (parseCompileResult == null || cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Parse and compile failed");
            return null;
        }

        var (parsingResult, compilationResult) = parseCompileResult;
        if (!compilationResult.Success)
        {
            var errors = compilationResult
                .Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => CorrectDiagnosticErrorLineNumber(d, parsingResult.UserProgramStartLineNumber));

            await _output.WriteAsync(
                new ErrorScriptOutput("Compilation failed:\n" + errors.JoinToString("\n")),
                cancellationToken: cancellationToken);

            return null;
        }

        // Deploy compiled script, the script-host, and their dependencies
        _ = _appStatusMessagePublisher.PublishAsync(_script.Id, "Preparing...");
        if (!_workingDirectory.ScriptHostExecutableFile.Exists())
        {
            DeployScriptHostExecutable(_workingDirectory);
        }

        await DeploySharedDependenciesAsync(_workingDirectory, dependencies);
        var (scriptDir, scriptAssemblyFilePath) = await DeployScriptDependenciesAsync(
            compilationResult.AssemblyBytes,
            dependencies);

        return new SetupInfo(
            scriptDir,
            scriptAssemblyFilePath,
            dependencies.Where(x => x.LoadStrategy == LoadStrategy.LoadInPlace)
                .SelectMany(x => x.Assets.Select(a => Path.GetDirectoryName(a.Path)))
                .Where(x => !string.IsNullOrEmpty(x))
                .ToHashSet()
                .ToArray()!,
            parsingResult.UserProgramStartLineNumber
        );
    }

    private (List<Dependency> dependencies, SourceCodeCollection additionalCode) dependencyCache = default;
    private string? _lastDependencyCacheKey;

    /// <summary>
    /// Cached package dependency data for individual NuGet packages
    /// </summary>
    private record CachedPackageDependencies(
        string PackageId,
        string Version,
        List<string> AssetPaths,
        List<string> TransitiveDependencies, // List of "PackageId:Version" strings
        DateTime CachedAt,
        string CacheVersion = "v2");

    /// <summary>
    /// Cached data connection resources
    /// </summary>
    private record CachedDataConnectionInfo(
        string DataConnectionHash,
        string TargetFramework,
        List<string> AdditionalCode,
        List<DependencyResolutionData> References,
        bool HasAssembly,
        DateTime CachedAt);

    /// <summary>
    /// Serializable dependency data for caching
    /// </summary>
    private record DependencyResolutionData(
        string ReferenceType,
        string ReferenceData,
        string NeededBy,
        string LoadStrategy,
        List<string> AssetPaths);

    private async Task<(List<Dependency> dependencies, SourceCodeCollection additionalCode)> GatherDependenciesAsync(
        CancellationToken cancellationToken)
    {
        // Generate cache key based on all factors that could affect dependencies
        var cacheKey = GenerateDependencyCacheKey();
        
        // Check memory cache first
        if (dependencyCache != default && _lastDependencyCacheKey == cacheKey)
        {
            return dependencyCache;
        }

        var dependencies = new List<Dependency>();
        var additionalCode = new SourceCodeCollection();

        // Add script references with intelligent package caching
        var packageReferences = _script.Config.References.OfType<PackageReference>().ToList();
        var otherReferences = _script.Config.References.Where(r => !(r is PackageReference)).ToList();

        // Handle package references with per-package caching
        if (packageReferences.Any())
        {
            var packageDependencies = await LoadPackageDependenciesWithCacheAsync(packageReferences, cancellationToken);
            dependencies.AddRange(packageDependencies);
        }

        // Handle other references normally
        dependencies.AddRange(otherReferences
            .Select(x => new Dependency(x, NeededBy.Script, LoadStrategy.LoadInPlace)));

        if (cancellationToken.IsCancellationRequested)
        {
            return (dependencies, additionalCode);
        }

        // Add data connection resources with caching
        if (_script.DataConnection != null)
        {
            var (dcCode, dcReferences, dcAssembly) = await GetDataConnectionResourcesWithCacheAsync(_script.DataConnection);

            if (dcCode.Count > 0)
            {
                additionalCode.AddRange(dcCode);
            }

            if (dcReferences.Count > 0)
            {
                dependencies.AddRange(dcReferences
                    .Select(x => new Dependency(x, NeededBy.Shared, LoadStrategy.DeployAndLoad)));
            }

            if (dcAssembly != null)
            {
                dependencies.Add(
                    new Dependency(new AssemblyImageReference(dcAssembly), NeededBy.Shared,
                        LoadStrategy.DeployAndLoad));
            }
        }

        // Add assembly files needed to support running script
        dependencies.AddRange(_userVisibleAssemblies
            .Select(assemblyPath => new Dependency(
                new AssemblyFileReference(assemblyPath),
                NeededBy.Shared,
                LoadStrategy.DeployAndLoad))
        );

        if (cancellationToken.IsCancellationRequested)
        {
            return (dependencies, additionalCode);
        }

        // Load assets for non-package dependencies
        var nonPackageDeps = dependencies.Where(d => !(d.Reference is PackageReference)).ToList();
        if (nonPackageDeps.Any())
        {
            await Task.WhenAll(nonPackageDeps
                .Select(d => d.LoadAssetsAsync(
                    _script.Config.TargetFrameworkVersion,
                    _packageProvider,
                    cancellationToken)));
        }

        _lastDependencyCacheKey = cacheKey;
        return dependencyCache = (dependencies, additionalCode);
    }

    /// <summary>
    /// Generates a cache key based on all factors that could affect dependency resolution.
    /// This ensures the cache is invalidated when any dependency-affecting property changes.
    /// </summary>
    private string GenerateDependencyCacheKey()
    {
        unchecked // Overflow is fine, just wrap
        {
            int hash = 17;
            
            // Script references
            foreach (var reference in _script.Config.References.OrderBy(r => r.ToString()))
            {
                hash = hash * 23 + reference.GetHashCode();
            }
            
            // Data connection
            if (_script.DataConnection != null)
            {
                hash = hash * 23 + _script.DataConnection.GetHashCode();
            }
            
            // Target framework version
            hash = hash * 23 + _script.Config.TargetFrameworkVersion.GetHashCode();
            
            // UseAspNet setting (affects runtime configuration)
            hash = hash * 23 + _script.Config.UseAspNet.GetHashCode();
            
            return hash.ToString();
        }
    }

    /// <summary>
    /// 优化：批量预取NuGet包依赖信息，减少网络请求次数
    /// </summary>
    private async Task<Dictionary<string, bool>> BatchCheckPackageInstallationAsync(
        IEnumerable<PackageReference> packageReferences,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, bool>();
        
        // 并行检查所有包的安装状态
        var checkTasks = packageReferences.Select(async pkg =>
        {
            var key = $"{pkg.PackageId}:{pkg.Version}";
            var installInfo = await _packageProvider.GetPackageInstallInfoAsync(pkg.PackageId, pkg.Version);
            return new { Key = key, IsInstalled = installInfo != null };
        });

        var checkResults = await Task.WhenAll(checkTasks);
        
        foreach (var result in checkResults)
        {
            results[result.Key] = result.IsInstalled;
        }

        return results;
    }

    /// <summary>
    /// 优化：并行安装缺失的NuGet包
    /// </summary>
    private async Task BatchInstallMissingPackagesAsync(
        IEnumerable<PackageReference> missingPackages,
        CancellationToken cancellationToken)
    {
        if (!missingPackages.Any()) return;

        // 并行安装所有缺失的包
        var installTasks = missingPackages.Select(pkg =>
            _packageProvider.InstallPackageAsync(
                pkg.PackageId,
                pkg.Version,
                _script.Config.TargetFrameworkVersion));

        await Task.WhenAll(installTasks);
    }

    /// <summary>
    /// Load package dependencies with per-package caching
    /// </summary>
    private async Task<List<Dependency>> LoadPackageDependenciesWithCacheAsync(
        List<PackageReference> packageReferences,
        CancellationToken cancellationToken)
    {
        var dependencies = new List<Dependency>();
        var uncachedPackages = new List<PackageReference>();

        // Try to load from per-package cache first
        foreach (var pkg in packageReferences)
        {
            var cachedDep = await TryLoadCachedPackageDependencyAsync(pkg);
            if (cachedDep != null)
            {
                dependencies.Add(cachedDep);
            }
            else
            {
                uncachedPackages.Add(pkg);
            }
        }

        // Load uncached packages and save to cache
        if (uncachedPackages.Any())
        {
            var uncachedDependencies = uncachedPackages
                .Select(x => new Dependency(x, NeededBy.Script, LoadStrategy.LoadInPlace))
                .ToList();

            await Task.WhenAll(uncachedDependencies
                .Select(d => d.LoadAssetsAsync(
                    _script.Config.TargetFrameworkVersion,
                    _packageProvider,
                    cancellationToken)));

            // Cache each package dependency separately
            foreach (var dep in uncachedDependencies)
            {
                if (dep.Reference is PackageReference pkg)
                {
                    await SavePackageDependencyToCacheAsync(pkg, dep);
                }
            }

            dependencies.AddRange(uncachedDependencies);
        }

        return dependencies;
    }

    /// <summary>
    /// Get data connection resources with caching
    /// </summary>
    private async Task<(SourceCodeCollection Code, IReadOnlyList<Reference> References, AssemblyImage? Assembly)>
        GetDataConnectionResourcesWithCacheAsync(DataConnection dataConnection)
    {
        var dcHash = dataConnection.GetHashCode().ToString();
        var targetFramework = _script.Config.TargetFrameworkVersion.ToString();
        
        // Try to load from cache first
        var cachedDc = await TryLoadCachedDataConnectionAsync(dcHash, targetFramework);
        if (cachedDc != null)
        {
            var code = new SourceCodeCollection();
            foreach (var codeStr in cachedDc.AdditionalCode)
            {
                code.Add(new NetPad.DotNet.CodeAnalysis.SourceCode(codeStr));
            }

            var references = new List<Reference>();
            foreach (var refData in cachedDc.References)
            {
                var reference = DeserializeReference(refData.ReferenceType, refData.ReferenceData);
                if (reference != null)
                {
                    references.Add(reference);
                }
            }

            return (code, references, cachedDc.HasAssembly ? null : null); // Assembly cannot be cached
        }

        // Load fresh data and cache it
        var result = await GetDataConnectionResourcesAsync(dataConnection);
        await SaveDataConnectionToCacheAsync(dcHash, targetFramework, result);
        return result;
    }

    /// <summary>
    /// Gets the file path for caching package dependency data
    /// </summary>
    private string GetPackageCacheFilePath(string packageId, string version)
    {
        var cacheDir = Path.Combine(_settings.PackageCacheDirectoryPath, "PackageDependencies");
        Directory.CreateDirectory(cacheDir);
        var fileName = $"{packageId.ToLowerInvariant()}-{version.ToLowerInvariant()}.json";
        return Path.Combine(cacheDir, fileName);
    }

    /// <summary>
    /// Gets the file path for caching data connection resources
    /// </summary>
    private string GetDataConnectionCacheFilePath(string dcHash, string targetFramework)
    {
        var cacheDir = Path.Combine(_settings.PackageCacheDirectoryPath, "DataConnections");
        Directory.CreateDirectory(cacheDir);
        return Path.Combine(cacheDir, $"{dcHash}-{targetFramework}.json");
    }

    /// <summary>
    /// Try to load cached package dependency
    /// </summary>
    private async Task<Dependency?> TryLoadCachedPackageDependencyAsync(PackageReference packageRef)
    {
        try
        {
            var cacheFile = GetPackageCacheFilePath(packageRef.PackageId, packageRef.Version);
            if (!File.Exists(cacheFile))
                return null;

            var json = await File.ReadAllTextAsync(cacheFile);
            var cached = System.Text.Json.JsonSerializer.Deserialize<CachedPackageDependencies>(json);
            
            if (cached != null && DateTime.UtcNow - cached.CachedAt < TimeSpan.FromDays(7))
            {
                // Verify assets still exist
                var existingAssets = cached.AssetPaths
                    .Where(File.Exists)
                    .Select(path => new ReferenceAsset(path))
                    .ToArray();
                
                if (existingAssets.Length == cached.AssetPaths.Count)
                {
                    var dependency = new Dependency(packageRef, NeededBy.Script, LoadStrategy.LoadInPlace);
                    typeof(Dependency).GetProperty("Assets")?.SetValue(dependency, existingAssets);
                    return dependency;
                }
            }

            // Remove expired or invalid cache
            File.Delete(cacheFile);
        }
        catch
        {
            // Ignore cache loading errors
        }

        return null;
    }

    /// <summary>
    /// Save package dependency to cache
    /// </summary>
    private async Task SavePackageDependencyToCacheAsync(PackageReference packageRef, Dependency dependency)
    {
        try
        {
            var cached = new CachedPackageDependencies(
                packageRef.PackageId,
                packageRef.Version,
                dependency.Assets.Select(a => a.Path).ToList(),
                new List<string>(), // TODO: Add transitive dependencies if needed
                DateTime.UtcNow
            );

            var cacheFile = GetPackageCacheFilePath(packageRef.PackageId, packageRef.Version);
            var json = System.Text.Json.JsonSerializer.Serialize(cached, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, json);
        }
        catch
        {
            // Ignore cache saving errors
        }
    }

    /// <summary>
    /// Try to load cached data connection resources
    /// </summary>
    private async Task<CachedDataConnectionInfo?> TryLoadCachedDataConnectionAsync(string dcHash, string targetFramework)
    {
        try
        {
            var cacheFile = GetDataConnectionCacheFilePath(dcHash, targetFramework);
            if (!File.Exists(cacheFile))
                return null;

            var json = await File.ReadAllTextAsync(cacheFile);
            var cached = System.Text.Json.JsonSerializer.Deserialize<CachedDataConnectionInfo>(json);
            
            if (cached != null && DateTime.UtcNow - cached.CachedAt < TimeSpan.FromHours(24))
            {
                return cached;
            }

            // Remove expired cache
            File.Delete(cacheFile);
        }
        catch
        {
            // Ignore cache loading errors
        }

        return null;
    }

    /// <summary>
    /// Save data connection resources to cache
    /// </summary>
    private async Task SaveDataConnectionToCacheAsync(
        string dcHash, 
        string targetFramework, 
        (SourceCodeCollection Code, IReadOnlyList<Reference> References, AssemblyImage? Assembly) resources)
    {
        try
        {
            var refData = resources.References.Select(r => new DependencyResolutionData(
                r.GetType().Name,
                SerializeReference(r),
                NeededBy.Shared.ToString(),
                LoadStrategy.DeployAndLoad.ToString(),
                new List<string>() // References don't have asset paths at this level
            )).ToList();

            var cached = new CachedDataConnectionInfo(
                dcHash,
                targetFramework,
                resources.Code.Select(c => c.ToCodeString()).ToList(),
                refData,
                resources.Assembly != null,
                DateTime.UtcNow
            );

            var cacheFile = GetDataConnectionCacheFilePath(dcHash, targetFramework);
            var json = System.Text.Json.JsonSerializer.Serialize(cached, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, json);
        }
        catch
        {
            // Ignore cache saving errors
        }
    }



    /// <summary>
    /// Serializes a reference object to string for caching
    /// </summary>
    private string SerializeReference(Reference reference)
    {
        return reference switch
        {
            PackageReference pkg => System.Text.Json.JsonSerializer.Serialize(new { pkg.PackageId, pkg.Version, pkg.Title }),
            AssemblyFileReference file => System.Text.Json.JsonSerializer.Serialize(new { file.AssemblyPath }),
            AssemblyImageReference => "AssemblyImage", // Cannot serialize, will need to regenerate
            _ => reference.ToString() ?? ""
        };
    }

    /// <summary>
    /// Deserializes a reference object from cached string data
    /// </summary>
    private Reference? DeserializeReference(string referenceType, string referenceData)
    {
        try
        {
            if (referenceType == nameof(PackageReference))
            {
                using var doc = JsonDocument.Parse(referenceData);
                var root = doc.RootElement;
                var packageId = root.GetProperty("PackageId").GetString();
                var version = root.GetProperty("Version").GetString();
                var title = root.GetProperty("Title").GetString();
                
                if (packageId != null && version != null && title != null)
                {
                    return new PackageReference(packageId, title, version);
                }
            }
            else if (referenceType == nameof(AssemblyFileReference))
            {
                using var doc = JsonDocument.Parse(referenceData);
                var root = doc.RootElement;
                var assemblyPath = root.GetProperty("AssemblyPath").GetString();
                
                if (assemblyPath != null)
                {
                    return new AssemblyFileReference(assemblyPath);
                }
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(SourceCodeCollection Code, IReadOnlyList<Reference> References, AssemblyImage? Assembly)>
        GetDataConnectionResourcesAsync(DataConnection dataConnection)
    {
        var code = new SourceCodeCollection();
        var references = new List<Reference>();

        var targetFrameworkVersion = _script.Config.TargetFrameworkVersion;

        var connectionResources =
            await _dataConnectionResourcesCache.GetResourcesAsync(dataConnection, targetFrameworkVersion);

        var applicationCode = connectionResources.SourceCode?.ApplicationCode;
        if (applicationCode?.Count > 0)
        {
            code.AddRange(applicationCode);
        }

        var requiredReferences = connectionResources.RequiredReferences;
        if (requiredReferences?.Length > 0)
        {
            references.AddRange(requiredReferences);
        }

        return (code, references, connectionResources.Assembly);
    }

    private ParseAndCompileResult? ParseAndCompileInner(
        RunOptions runOptions,
        List<Dependency> dependencies,
        SourceCodeCollection additionalCode,
        CancellationToken cancellationToken)
    {
        var compileAssemblyImageDeps = dependencies
            .Select(d =>
                d.NeededBy != NeededBy.ScriptHost && d.Reference is AssemblyImageReference air
                    ? air.AssemblyImage
                    : null!)
            .Where(x => x != null!)
            .ToArray();

        var compileAssemblyFileDeps = dependencies
            .Where(x => x.NeededBy != NeededBy.ScriptHost)
            .SelectMany(x => x.Assets)
            .DistinctBy(x => x.Path)
            .Where(x => x.IsManagedAssembly)
            .Select(x => new
            {
                x.Path,
                AssemblyName = AssemblyName.GetAssemblyName(x.Path)
            })
            // Choose the highest version of duplicate assemblies
            .GroupBy(a => a.AssemblyName.Name)
            .Select(grp => grp.OrderBy(x => x.AssemblyName.Version).Last())
            .Select(x => x.Path)
            .ToHashSet();

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return ParseAndCompile.Do(
            runOptions.SpecificCodeToRun ?? _script.Code,
            _script,
            _codeParser,
            _codeCompiler,
            compileAssemblyImageDeps,
            compileAssemblyFileDeps,
            additionalCode);
    }

    private static void DeployScriptHostExecutable(WorkingDirectory workingDirectory)
    {
        if (!workingDirectory.ScriptHostExecutableSourceDirectory.Exists())
        {
            throw new InvalidOperationException(
                $"Could not find source script-host executable directory to deploy. Path does not exist: {workingDirectory.ScriptHostExecutableSourceDirectory}");
        }

        // Copy script-host app to working dir
        FileSystemUtil.CopyDirectory(
            workingDirectory.ScriptHostExecutableSourceDirectory.Path,
            workingDirectory.ScriptHostExecutableRunDirectory.Path,
            true);
    }

    private static async Task DeploySharedDependenciesAsync(WorkingDirectory workingDirectory,
        IList<Dependency> dependencies)
    {
        workingDirectory.SharedDependenciesDirectory.CreateIfNotExists();

        var sharedDeps = dependencies
            .Where(x => x.NeededBy is NeededBy.ScriptHost or NeededBy.Shared);

        await DeployAsync(workingDirectory.SharedDependenciesDirectory, sharedDeps);
    }

    private async Task<(DirectoryPath, FilePath)> DeployScriptDependenciesAsync(
        byte[] scriptAssembly,
        IList<Dependency> dependencies)
    {
        var scriptDeployDir = _workingDirectory.CreateNewScriptDeployDirectory();
        scriptDeployDir.CreateIfNotExists();

        var scriptDeps = dependencies.Where(x => x.NeededBy is NeededBy.Script).ToArray();

        await DeployAsync(scriptDeployDir, scriptDeps);

        // Write compiled assembly to dir
        var fileSafeScriptName = StringUtil
                                     .RemoveInvalidFileNameCharacters(_script.Name, "_")
                                     .Replace(" ", "_")
                                 // Arbitrary suffix so we don't match an assembly/asset with the same name.
                                 // Example: Assume user names script "Microsoft.Extensions.DependencyInjection"
                                 // If user also has a reference to "Microsoft.Extensions.DependencyInjection.dll"
                                 // then code further below will not copy the "Microsoft.Extensions.DependencyInjection.dll"
                                 // to the output directory, resulting in the referenced assembly not being found.
                                 + "__";

        var scriptAssemblyFilePath = scriptDeployDir.CombineFilePath($"{fileSafeScriptName}.dll");

        await File.WriteAllBytesAsync(scriptAssemblyFilePath.Path, scriptAssembly);

        // A runtimeconfig.json file tells .NET how to run the assembly
        var probingPaths = new[]
            {
                scriptDeployDir.Path,
                _workingDirectory.SharedDependenciesDirectory.Path,
            }
            .Union(scriptDeps.SelectMany(x => x.Assets.Select(a => Path.GetDirectoryName(a.Path)!)))
            .Where(x => !string.IsNullOrEmpty(x))
            .ToArray();

        await File.WriteAllTextAsync(
            Path.Combine(scriptDeployDir.Path, $"{fileSafeScriptName}.runtimeconfig.json"),
            GenerateRuntimeConfigFileContents(probingPaths)
        );

        // The scriptconfig.json is custom and passes some options to the running script
        await File.WriteAllTextAsync(
            Path.Combine(scriptDeployDir.Path, "scriptconfig.json"),
            $$"""
              {
                  "output": {
                      "maxDepth": {{_settings.Results.MaxSerializationDepth}},
                      "maxCollectionSerializeLength": {{_settings.Results.MaxCollectionSerializeLength}}
                  }
              }
              """);

        return (scriptDeployDir, scriptAssemblyFilePath);
    }

    private static async Task DeployAsync(DirectoryPath destination, IEnumerable<Dependency> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            var reference = dependency.Reference;

            if (reference is AssemblyImageReference air)
            {
                var assemblyImage = air.AssemblyImage;
                var fileName = assemblyImage.ConstructAssemblyFileName();
                var destFilePath = destination.CombineFilePath(fileName);

                // Checking file exists means that the first assembly in the list of paths will win.
                // Later assemblies with the same file name will not be copied to the output directory.
                if (!destFilePath.Exists())
                {
                    await File.WriteAllBytesAsync(
                        destFilePath.Path,
                        assemblyImage.Image);
                }
            }

            if (dependency.LoadStrategy == LoadStrategy.LoadInPlace)
            {
                continue;
            }

            foreach (var asset in dependency.Assets)
            {
                var destFilePath = destination.CombineFilePath(Path.GetFileName(asset.Path));
                if (!destFilePath.Exists())
                {
                    File.Copy(asset.Path, destFilePath.Path, true);
                }
            }
        }
    }

    private string GenerateRuntimeConfigFileContents(string[] probingPaths)
    {
        var tfm = _script.Config.TargetFrameworkVersion.GetTargetFrameworkMoniker();
        var frameworkName = _script.Config.UseAspNet ? "Microsoft.AspNetCore.App" : "Microsoft.NETCore.App";
        int majorVersion = _script.Config.TargetFrameworkVersion.GetMajorVersion();

        var runtimeVersion = _dotNetInfo.GetDotNetRuntimeVersionsOrThrow()
            .Where(v => v.Version.Major == majorVersion &&
                        v.FrameworkName.Equals(frameworkName, StringComparison.OrdinalIgnoreCase))
            .MaxBy(v => v.Version)?
            .Version;

        if (runtimeVersion == null)
        {
            throw new Exception(
                $"Could not find a {tfm} runtime with the name {frameworkName} and version {majorVersion}");
        }

        var probingPathsJson = System.Text.Json.JsonSerializer.Serialize(probingPaths);

        return $$"""
                 {
                     "runtimeOptions": {
                         "tfm": "{{tfm}}",
                         "rollForward": "Minor",
                         "framework": {
                             "name": "{{frameworkName}}",
                             "version": "{{runtimeVersion}}"
                         },
                         "additionalProbingPaths": {{probingPathsJson}}
                     }
                 }
                 """;
    }

    /// <summary>
    /// Corrects line numbers in compilation errors relative to the line number where user code starts.
    /// </summary>
    private static string CorrectDiagnosticErrorLineNumber(Diagnostic diagnostic, int userProgramStartLineNumber)
    {
        var err = diagnostic.ToString();

        if (!err.StartsWith('('))
        {
            return err;
        }

        var errParts = err.Split(':');
        var span = errParts.First().Trim(['(', ')']);
        var spanParts = span.Split(',');
        var lineNumberStr = spanParts[0];

        return int.TryParse(lineNumberStr, out int lineNumber)
            ? $"({lineNumber - userProgramStartLineNumber},{spanParts[1]}):{errParts.Skip(1).JoinToString(":")}"
            : err;
    }

    /// <summary>
    /// The component that a dependency is needed by.
    /// </summary>
    enum NeededBy
    {
        Script,
        ScriptHost,
        Shared,
    }

    /// <summary>
    /// How a dependency is deployed and loaded.
    /// </summary>
    enum LoadStrategy
    {
        /// <summary>
        /// Dependency will be copied to output directory and loaded from there.
        /// </summary>
        DeployAndLoad,

        /// <summary>
        /// Dependency will not be copied, and will be loaded from its original location (in-place).
        /// </summary>
        LoadInPlace
    }

    /// <summary>
    /// A dependency needed by the run environment.
    /// </summary>
    /// <param name="Reference">A reference dependency.</param>
    /// <param name="NeededBy">Which components need this dependency to run.</param>
    /// <param name="LoadStrategy">How will this dependency be deployed.</param>
    record Dependency(Reference Reference, NeededBy NeededBy, LoadStrategy LoadStrategy)
    {
        public ReferenceAsset[] Assets { get; private set; } = [];

        public async Task LoadAssetsAsync(
            DotNetFrameworkVersion dotNetFrameworkVersion,
            IPackageProvider packageProvider,
            CancellationToken cancellationToken = default)
        {
            var assets = await Reference.GetAssetsAsync(dotNetFrameworkVersion, packageProvider, cancellationToken);
            Assets = assets.ToArray();
        }
    }
}
