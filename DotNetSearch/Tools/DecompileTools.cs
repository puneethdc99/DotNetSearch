using System.ComponentModel;
using System.Text.Json;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using ModelContextProtocol.Server;
using System.Linq;

namespace DotNetSearch.Tools;

[McpServerToolType]
public static class DecompileTools
{
    [McpServerTool]
    [Description(
        "Decompiles a .NET type (class, interface, enum, struct, delegate) from the NuGet package assemblies " +
        "referenced by a .NET project. Pass the short class name (e.g. 'Session') or the fully qualified name " +
        "(e.g. 'Opc.Ua.Client.Session'). The tool scans the project's obj output folder — which contains all " +
        "referenced NuGet DLLs after a build — so no assembly path is needed. " +
        "Use this when the user or LLM needs to understand the API surface of a third-party or NuGet type " +
        "without access to its source code. " +
        "IMPORTANT: The target project must have been built at least once so the bin and obj folders are populated. " +
        "If typeName is ambiguous (found in multiple assemblies), all matches are returned up to maxResults.")]
    public static string DecompileType(
        [Description(
            "Short class name (e.g. 'Session') or fully qualified name (e.g. 'Opc.Ua.Client.Session'). " +
            "Matching is case-insensitive on the short name. Namespace prefix narrows the match."
        )] string typeName,
        [Description(
            "Absolute path to the .csproj file of the project that references the NuGet package containing the type. " +
            "Example: 'C:\\repos\\MyApp\\MyApp.csproj'. " +
            "The tool resolves the obj output folder automatically from this path."
        )] string projectPath,
        [Description(
            "Maximum number of matching types to return when the name is ambiguous across multiple assemblies. " +
            "Defaults to 1. Increase if you want to compare the same type name across multiple packages."
        )] int maxResults = 1)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return JsonError("typeName must not be empty.");

            if (string.IsNullOrWhiteSpace(projectPath))
                return JsonError("projectPath must not be empty.");

            projectPath = Path.GetFullPath(projectPath);

            if (!File.Exists(projectPath))
                return JsonError($"Project file not found: {projectPath}");

            if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return JsonError("projectPath must point to a .csproj file.");

            string projectDir = Path.GetDirectoryName(projectPath)!;
            string objDir     = Path.Combine(projectDir, "obj");
            string binDir     = Path.Combine(projectDir, "bin");

            // NuGet DLLs are copied to bin by the .NET SDK (standard behaviour).
            // obj contains only the project's own compiled output.
            // We scan both and deduplicate by filename so the same assembly found
            // in Debug + Release configurations is only loaded once.
            var candidateDirs = new[] { binDir, objDir }
                .Where(Directory.Exists)
                .ToArray();

            if (candidateDirs.Length == 0)
                return JsonError(
                    $"Neither bin nor obj folder found under '{projectDir}'. " +
                    "Build the project at least once so assemblies are produced.");

            // Deduplicate: prefer bin over obj; within each folder prefer the first occurrence
            // (alphabetical path order naturally puts Debug before Release which is fine).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dlls = candidateDirs
                .SelectMany(d => Directory.GetFiles(d, "*.dll", SearchOption.AllDirectories))
                .Where(f => seen.Add(Path.GetFileName(f)))
                .ToArray();

            if (dlls.Length == 0)
                return JsonError(
                    $"No DLL files found under '{projectDir}'. " +
                    "Build the project at least once so assemblies are produced.");

            // Normalise the search name
            bool isFullyQualified = typeName.Contains('.');
            string shortName      = isFullyQualified ? typeName.Split('.').Last() : typeName;
            string? namespacePart = isFullyQualified
                ? string.Join('.', typeName.Split('.').SkipLast(1))
                : null;

            maxResults = Math.Max(1, Math.Min(maxResults, 20));

            var matches = new List<DecompileResult>();

            foreach (string dll in dlls)
            {
                if (matches.Count >= maxResults)
                    break;

                try
                {
                    var settings    = new DecompilerSettings { ThrowOnAssemblyResolveErrors = false };
                    var decompiler  = new CSharpDecompiler(dll, settings);
                    var typeSystem  = decompiler.TypeSystem;

                    // Search all types in the primary module
                    foreach (ITypeDefinition typeDef in typeSystem.MainModule.TypeDefinitions)
                    {
                        if (!typeDef.Name.Equals(shortName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // If a namespace prefix was supplied, verify it matches
                        if (namespacePart is not null &&
                            !typeDef.Namespace.EndsWith(namespacePart, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string decompiledSource = decompiler.DecompileTypeAsString(typeDef.FullTypeName);

                        matches.Add(new DecompileResult(
                            FullTypeName:   typeDef.FullName,
                            Assembly:       Path.GetFileName(dll),
                            AssemblyPath:   dll,
                            Source:         decompiledSource));

                        if (matches.Count >= maxResults)
                            break;
                    }
                }
                catch
                {
                    // Skip unreadable / native / obfuscated DLLs silently
                }
            }

            if (matches.Count == 0)
                return JsonError(
                    $"Type '{typeName}' was not found in any assembly under '{projectDir}'. " +
                    "Check the type name spelling, or ensure the NuGet package is referenced and the project is built.");

            var response = new
            {
                typeName,
                matchCount = matches.Count,
                results    = matches.Select(m => new
                {
                    fullTypeName = m.FullTypeName,
                    assembly     = m.Assembly,
                    assemblyPath = m.AssemblyPath,
                    source       = m.Source
                })
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });

    private record DecompileResult(
        string FullTypeName,
        string Assembly,
        string AssemblyPath,
        string Source);
}
