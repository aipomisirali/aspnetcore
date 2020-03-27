// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using static Microsoft.AspNetCore.Components.WebAssembly.Build.WebAssemblyRuntimePackage;

namespace Microsoft.AspNetCore.Components.WebAssembly.Build
{
    public class BuildIntegrationTest
    {
        [Fact]
        public async Task Build_WithDefaultSettings_Works()
        {
            // Arrange
            using var project = ProjectDirectory.Create("standalone");
            var result = await MSBuildProcessManager.DotnetMSBuild(project);

            Assert.BuildPassed(result);

            var buildOutputDirectory = project.BuildOutputDirectory;

            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "blazor.boot.json");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "blazor.webassembly.js");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "wasm", "dotnet.wasm");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "wasm", DotNetJsFileName);
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "standalone.dll");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "Microsoft.Extensions.Logging.Abstractions.dll"); // Verify dependencies are part of the output.

            var staticWebAssets = Assert.FileExists(result, buildOutputDirectory, "standalone.StaticWebAssets.xml");
            Assert.FileContains(result, staticWebAssets, Path.Combine("netstandard2.1", "wwwroot"));

            var bootJsonPath = Path.Combine(buildOutputDirectory, "wwwroot", "_framework", "blazor.boot.json");
            var bootJsonData = ReadBootJsonData(result, bootJsonPath);
        }

        [Fact]
        public async Task Build_ProducesBootJsonDataWithExpectedContent()
        {
            // Arrange
            using var project = ProjectDirectory.Create("standalone", additionalProjects: new[] { "razorclasslibrary" });
            var result = await MSBuildProcessManager.DotnetMSBuild(project);

            Assert.BuildPassed(result);

            var buildOutputDirectory = project.BuildOutputDirectory;

            var bootJsonPath = Path.Combine(buildOutputDirectory, "wwwroot", "_framework", "blazor.boot.json");
            var bootJsonData = ReadBootJsonData(result, bootJsonPath);

            var runtime = bootJsonData.resources.runtime.Keys;
            Assert.Contains(DotNetJsFileName, runtime);
            Assert.Contains("dotnet.wasm", runtime);

            var assemblies = bootJsonData.resources.assembly.Keys;
            Assert.Contains("standalone.dll", assemblies);
            Assert.Contains("Microsoft.Extensions.Logging.Abstractions.dll", assemblies);

            var pdb = bootJsonData.resources.pdb.Keys;
            Assert.Contains("standalone.pdb", pdb);
        }

        [Fact]
        public async Task Build_Hosted_Works()
        {
            // Arrange
            using var project = ProjectDirectory.Create("blazorhosted", additionalProjects: new[] { "standalone", "razorclasslibrary", });
            project.TargetFramework = "netcoreapp3.1";
            var result = await MSBuildProcessManager.DotnetMSBuild(project);

            Assert.BuildPassed(result);

            var buildOutputDirectory = project.BuildOutputDirectory;

            var path = Path.GetFullPath(Path.Combine(project.SolutionPath, "standalone", "bin", project.Configuration, "netstandard2.1", "standalone.dll"));
            Assert.FileDoesNotExist(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "standalone.dll");

            var staticWebAssets = Assert.FileExists(result, buildOutputDirectory, "blazorhosted.StaticWebAssets.xml");
            Assert.FileContains(result, staticWebAssets, Path.Combine("netstandard2.1", "wwwroot"));
            Assert.FileContains(result, staticWebAssets, Path.Combine("razorclasslibrary", "wwwroot"));
            Assert.FileContains(result, staticWebAssets, Path.Combine("standalone", "wwwroot"));
        }

        [Fact]
        public async Task Build_WithLinkOnBuildDisabled_Works()
        {
            // Arrange
            using var project = ProjectDirectory.Create("standalone");
            project.AddProjectFileContent(
@"<PropertyGroup>
    <BlazorWebAssemblyEnableLinking>false</BlazorWebAssemblyEnableLinking>
</PropertyGroup>");

            var result = await MSBuildProcessManager.DotnetMSBuild(project);

            Assert.BuildPassed(result);

            var buildOutputDirectory = project.BuildOutputDirectory;

            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "blazor.boot.json");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "blazor.webassembly.js");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "wasm", "dotnet.wasm");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "wasm", DotNetJsFileName);
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "standalone.dll");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "Microsoft.Extensions.Logging.Abstractions.dll"); // Verify dependencies are part of the output.
        }

        [Fact]
        public async Task Build_SatelliteAssembliesAreCopiedToBuildOutput()
        {
            // Arrange
            using var project = ProjectDirectory.Create("standalone", additionalProjects: new[] { "razorclasslibrary", "classlibrarywithsatelliteassemblies" });
            project.AddProjectFileContent(
@"
<PropertyGroup>
    <DefineConstants>$(DefineConstants);REFERENCE_classlibrarywithsatelliteassemblies</DefineConstants>
</PropertyGroup>
<ItemGroup>
    <ProjectReference Include=""..\classlibrarywithsatelliteassemblies\classlibrarywithsatelliteassemblies.csproj"" />
</ItemGroup>");

            var result = await MSBuildProcessManager.DotnetMSBuild(project, args: "/restore");

            Assert.BuildPassed(result);

            var buildOutputDirectory = project.BuildOutputDirectory;

            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "standalone.dll");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "classlibrarywithsatelliteassemblies.dll");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "Microsoft.CodeAnalysis.CSharp.dll");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "fr", "Microsoft.CodeAnalysis.CSharp.resources.dll"); // Verify satellite assemblies are present in the build output.

            var bootJsonPath = Path.Combine(buildOutputDirectory, "wwwroot", "_framework", "blazor.boot.json");
            Assert.FileContains(result, bootJsonPath, "\"Microsoft.CodeAnalysis.CSharp.dll\"");
            Assert.FileContains(result, bootJsonPath, "\"fr\\/Microsoft.CodeAnalysis.CSharp.resources.dll\"");
        }

        [Fact]
        public async Task Build_WithBlazorWebAssemblyEnableLinkingFalse_SatelliteAssembliesAreCopiedToBuildOutput()
        {
            // Arrange
            using var project = ProjectDirectory.Create("standalone", additionalProjects: new[] { "razorclasslibrary", "classlibrarywithsatelliteassemblies" });
            project.AddProjectFileContent(
@"
<PropertyGroup>
    <BlazorWebAssemblyEnableLinking>false</BlazorWebAssemblyEnableLinking>
    <DefineConstants>$(DefineConstants);REFERENCE_classlibrarywithsatelliteassemblies</DefineConstants>
</PropertyGroup>
<ItemGroup>
    <ProjectReference Include=""..\classlibrarywithsatelliteassemblies\classlibrarywithsatelliteassemblies.csproj"" />
</ItemGroup>");

            var result = await MSBuildProcessManager.DotnetMSBuild(project, args: "/restore");

            Assert.BuildPassed(result);

            var buildOutputDirectory = project.BuildOutputDirectory;

            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "standalone.dll");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "classlibrarywithsatelliteassemblies.dll");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "Microsoft.CodeAnalysis.CSharp.dll");
            Assert.FileExists(result, buildOutputDirectory, "wwwroot", "_framework", "_bin", "fr", "Microsoft.CodeAnalysis.CSharp.resources.dll"); // Verify satellite assemblies are present in the build output.

            var bootJson = ReadBootJsonData(result, Path.Combine(buildOutputDirectory, "wwwroot", "_framework", "blazor.boot.json"));

            var satelliteResources = bootJson.resources.satelliteResources;
            Assert.NotNull(satelliteResources);

            Assert.Contains("es-ES", satelliteResources.Keys);
            Assert.Contains("es-ES/classlibrarywithsatelliteassemblies.resources.dll", satelliteResources["es-ES"].Keys);
            Assert.Contains("fr", satelliteResources.Keys);
            Assert.Contains("fr/Microsoft.CodeAnalysis.CSharp.resources.dll", satelliteResources["fr"].Keys);
            Assert.Contains("ja", satelliteResources.Keys);
            Assert.Contains("ja/standalone.resources.dll", satelliteResources["ja"].Keys);
        }

        private static GenerateBlazorBootJson.BootJsonData ReadBootJsonData(MSBuildResult result, string path)
        {
            return JsonSerializer.Deserialize<GenerateBlazorBootJson.BootJsonData>(
                File.ReadAllText(Path.Combine(result.Project.DirectoryPath, path)),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }
}
