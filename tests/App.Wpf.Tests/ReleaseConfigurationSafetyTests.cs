namespace RbxManager.App.Tests;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

public sealed class ReleaseConfigurationSafetyTests
{
    private static readonly string[] ForbiddenTestTypeKeywords =
    {
        "UiTest",
        "UITest",
        "FakeLauncher",
        "FakeRobloxAuthService",
        "FakeServerBrowserService",
        "FakeProcessProvider"
    };

    [Fact]
    public void ReleaseBuild_ContainsNoUiTestHooks()
    {
        string repoRoot = FindRepoRoot();
        string releaseDllPath = Path.Combine(repoRoot, "src", "App.Wpf", "bin", "Release", "net10.0-windows", "RbxManager.App.dll");

        // If the Release DLL exists on disk (e.g. after dotnet build -c Release or during release test run),
        // inspect all type definitions using System.Reflection.Metadata.
        if (File.Exists(releaseDllPath))
        {
            using FileStream stream = File.OpenRead(releaseDllPath);
            using PEReader peReader = new(stream);
            MetadataReader metadataReader = peReader.GetMetadataReader();

            foreach (TypeDefinitionHandle typeHandle in metadataReader.TypeDefinitions)
            {
                TypeDefinition typeDef = metadataReader.GetTypeDefinition(typeHandle);
                string typeName = metadataReader.GetString(typeDef.Name);

                foreach (string keyword in ForbiddenTestTypeKeywords)
                {
                    Assert.False(
                        typeName.Contains(keyword, StringComparison.OrdinalIgnoreCase),
                        $"Release assembly {releaseDllPath} contains forbidden UI-test hook type '{typeName}' containing '{keyword}'.");
                }
            }
        }

#if !DEBUG
        // When running under Release configuration directly, inspect the loaded assembly
        Type[] appTypes = typeof(RbxManager.App.App).Assembly.GetTypes();
        foreach (Type type in appTypes)
        {
            foreach (string keyword in ForbiddenTestTypeKeywords)
            {
                Assert.False(
                    type.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase),
                    $"Release build contains forbidden UI-test hook type '{type.FullName}'.");
            }
        }
#endif
    }

    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.GetFiles(current, "*.slnx").Length > 0 || Directory.GetFiles(current, "*.sln").Length > 0)
            {
                return current;
            }
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }
            current = parent.FullName;
        }
        throw new InvalidOperationException("Could not find repository root containing .slnx or .sln.");
    }
}
