using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RecipeManager.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    [Theory]
    [InlineData("RecipeManager.Core", "")]
    [InlineData("RecipeManager.Infrastructure", "RecipeManager.Core")]
    [InlineData("RecipeManager.Api", "RecipeManager.Core,RecipeManager.Infrastructure")]
    public void Projects_reference_only_their_allowed_layers(string project, string allowed)
    {
        var document = XDocument.Load(Path.Combine(Root(), "src", project, project + ".csproj"));
        var expected = allowed.Split(',', StringSplitOptions.RemoveEmptyEntries).Order().ToArray();
        var actual = document.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include")))
            .Order().ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Core_does_not_depend_on_web_or_persistence_frameworks()
    {
        var document = XDocument.Load(Path.Combine(Root(), "src", "RecipeManager.Core", "RecipeManager.Core.csproj"));
        Assert.Empty(document.Descendants("FrameworkReference"));
        Assert.DoesNotContain(document.Descendants("PackageReference"), reference =>
            ((string?)reference.Attribute("Include") ?? "").StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || ((string?)reference.Attribute("Include") ?? "").Contains("EntityFramework", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_modules_reference_other_modules_only_through_contract_namespaces()
    {
        var directory = Path.Combine(Root(), "src", "RecipeManager.Core", "Modules");
        if (!Directory.Exists(directory))
        {
            return; // No modules exist during setup; this guard covers all later module sources.
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var owner = Regex.Match(source, @"namespace RecipeManager\.Core\.Modules\.(\w+)");
            if (!owner.Success)
            {
                continue;
            }

            foreach (Match reference in Regex.Matches(source, @"RecipeManager\.Core\.Modules\.(\w+)\.(\w+)"))
            {
                var target = reference.Groups[1].Value;
                if (target != owner.Groups[1].Value)
                {
                    Assert.True(reference.Groups[2].Value == "Contracts",
                        $"{Path.GetRelativePath(Root(), file)} references {target} internals instead of Contracts.");
                }
            }
        }
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RecipeManager.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("RecipeManager.slnx not found.");
    }
}
