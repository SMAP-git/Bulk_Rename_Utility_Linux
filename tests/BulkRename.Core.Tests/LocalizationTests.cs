using System.Text.Json;
using System.Text.RegularExpressions;

namespace BulkRename.Core.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void AllCataloguesHaveCompleteUniqueKeysAndPreserveFormatPlaceholders()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Languages");
        var paths = Directory.GetFiles(directory, "*.json");
        Assert.Equal(19, paths.Length);
        var english = ReadCatalogue(Path.Combine(directory, "en.json"));
        foreach (var path in paths)
        {
            var catalogue = ReadCatalogue(path);
            Assert.True(english.Keys.ToHashSet().SetEquals(catalogue.Keys),
                $"{Path.GetFileName(path)} does not match the English catalogue keys.");
            foreach (var (key, value) in catalogue)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{Path.GetFileName(path)}: {key} is empty.");
                Assert.True(Placeholders(english[key]).SequenceEqual(Placeholders(value)),
                    $"{Path.GetFileName(path)}: {key} changes format placeholders.");
                if (Path.GetFileName(path) != "en.json" && key.EndsWith("Tip", StringComparison.Ordinal))
                {
                    Assert.NotEqual(english[key], value);
                }
            }
        }
    }

    private static Dictionary<string, string> ReadCatalogue(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var catalogue = new Dictionary<string, string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            Assert.True(catalogue.TryAdd(property.Name, property.Value.GetString()!),
                $"{Path.GetFileName(path)} has a duplicate key: {property.Name}");
        }

        return catalogue;
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{\d+\}").Select(match => match.Value).Order().ToArray();
}
