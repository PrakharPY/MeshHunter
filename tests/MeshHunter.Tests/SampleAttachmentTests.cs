using MeshHunter.Models;
using MeshHunter.Services;
using Xunit;

namespace MeshHunter.Tests;

public sealed class SampleAttachmentTests
{
    [SampleFact("MESHHUNTER_ASPHALT_BAR", "MESHHUNTER_ASPHALT_TEXTURES")]
    public void ExtractsValidatedAsphaltArchiveEntriesAndEmbeddedImages()
    {
        using var workspace = new TemporaryDirectory();
        var analyzer = new AssetAnalyzer();
        var barResults = analyzer.Analyze(
            [Environment.GetEnvironmentVariable("MESHHUNTER_ASPHALT_BAR")!],
            workspace.Path);
        var bar = Assert.Single(barResults, asset => asset.Format == "Asphalt 4 BAR");
        Assert.Contains("Extracted 764 of 804 entries", bar.Description);
        Assert.Contains("40 entries", bar.Description);
        var models = barResults.Where(asset => asset.Format == "Asphalt 4 binary OBJ").ToArray();
        Assert.Equal(72, models.Length);
        Assert.Equal(37_887, models.Sum(asset => MeshFormatReader.Read(asset.Path).Triangles.Count / 3));

        var textureResults = analyzer.Analyze(
            [Environment.GetEnvironmentVariable("MESHHUNTER_ASPHALT_TEXTURES")!],
            workspace.Path);
        Assert.Contains(textureResults, asset => asset.Format == "Embedded PNG/JPEG collection");
        Assert.Equal(160, textureResults.Count(asset => asset.Format == "PNG"));
        Assert.Equal(0, textureResults.Count(asset => asset.Format == "JPEG"));
    }

    [SampleFact("MESHHUNTER_RAGING_THUNDER_1")]
    public void ExtractsRagingThunderOneModelsAndPngTextures()
    {
        var results = Analyze(Environment.GetEnvironmentVariable("MESHHUNTER_RAGING_THUNDER_1")!);

        Assert.Contains(results, asset => asset.Format == "Raging Thunder 1 FUFS");
        Assert.Equal(406, results.Count(asset => asset.Kind == AssetKind.Mesh && asset.Format == "PIFF"));
        Assert.Equal(134, results.Count(asset => asset.Kind == AssetKind.Texture && asset.Format == "PNG"));
    }

    [SampleFact("MESHHUNTER_RAGING_THUNDER_2")]
    public void ExtractsRagingThunderTwoDdsAndPngTextures()
    {
        using var workspace = new TemporaryDirectory();
        var archivePath = Environment.GetEnvironmentVariable("MESHHUNTER_RAGING_THUNDER_2")!;
        var extracted = Path.Combine(workspace.Path, "extracted-vfs");
        Assert.True(GameArchiveExtractor.TryExtractRagingThunderVfs(archivePath, extracted, out _));
        var pxcPath = Assert.Single(Directory.GetFiles(extracted, "*.pxc"));
        var extractedMesh = RagingThunderPxcMeshReader.Read(pxcPath);
        Assert.NotEmpty(extractedMesh.Triangles);

        var results = new AssetAnalyzer().Analyze([archivePath], workspace.Path);

        Assert.Contains(results, asset => asset.Format == "Raging Thunder 2 FUFS");
        Assert.Equal(235, results.Count(asset => asset.Kind == AssetKind.Texture && asset.Format == "DDS"));
        Assert.Equal(12, results.Count(asset => asset.Kind == AssetKind.Texture && asset.Format == "PNG"));
        var models = results.Where(asset => asset.Format == "Raging Thunder 2 PXC").ToArray();
        Assert.Equal(13, models.Length);
        foreach (var model in models)
        {
            var mesh = MeshFormatReader.Read(model.Path);
            Assert.NotEmpty(mesh.Vertices);
            Assert.NotEmpty(mesh.Triangles);
            Assert.All(mesh.Triangles, index => Assert.InRange(index, 0, mesh.Vertices.Count - 1));
        }
    }

    [SampleFact("MESHHUNTER_FASTLANE_ICR", "MESHHUNTER_FASTLANE_GSR")]
    public void RecognizesFastlaneContainersWithoutClaimingExtraction()
    {
        foreach (var variable in new[] { "MESHHUNTER_FASTLANE_ICR", "MESHHUNTER_FASTLANE_GSR" })
        {
            var results = Analyze(Environment.GetEnvironmentVariable(variable)!);
            Assert.Contains(results, asset => asset.Kind == AssetKind.Other && asset.Format == "Fastlane KAPG");
            Assert.DoesNotContain(results, asset => asset.Kind is AssetKind.Mesh or AssetKind.Texture);
        }
    }

    [SampleFact("MESHHUNTER_RIDGE_RACER_DAT")]
    public void DoesNotClaimUnsupportedRidgeRacerDatPayload()
    {
        var results = Analyze(Environment.GetEnvironmentVariable("MESHHUNTER_RIDGE_RACER_DAT")!);

        Assert.DoesNotContain(results, asset => asset.Kind is AssetKind.Mesh or AssetKind.Texture or AssetKind.Archive);
    }

    private static IReadOnlyList<AssetEntry> Analyze(string inputPath)
    {
        var workspace = Directory.CreateTempSubdirectory();
        try
        {
            return new AssetAnalyzer().Analyze([inputPath], workspace.FullName).ToArray();
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory().FullName;

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class SampleFactAttribute : FactAttribute
{
    public SampleFactAttribute(string environmentVariable) : this([environmentVariable]) { }

    public SampleFactAttribute(string firstEnvironmentVariable, string secondEnvironmentVariable)
        : this([firstEnvironmentVariable, secondEnvironmentVariable])
    {
    }

    private SampleFactAttribute(string[] environmentVariables)
    {
        if (environmentVariables.Any(variable => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))))
            Skip = "Set the required MESHHUNTER sample environment variables to run attachment integration tests.";
    }
}
