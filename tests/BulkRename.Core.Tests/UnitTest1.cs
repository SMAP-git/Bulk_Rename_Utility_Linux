using BulkRename.Core;

namespace BulkRename.Core.Tests;

public sealed class RenameEngineTests
{
    [Fact]
    public void Preview_AppliesReplacementCasePrefixNumberAndExtension()
    {
        var options = new RenameOptions
        {
            ReplaceText = "TRIP",
            ReplaceWith = "Walk",
            CaseMode = "lowercase",
            Prefix = "2026-",
            NumberMode = "Suffix",
            NumberStart = 3,
            NumberIncrement = 2,
            NumberPadding = 2,
            NumberSeparator = "_",
            ExtensionMode = "UPPERCASE"
        };

        var result = RenameEngine.Preview("summer TRIP 01.jpg", options, 1);

        Assert.Equal("2026-summer walk 01_03.JPG", result);
    }

    [Fact]
    public void Preview_ReplaceTextSupportsWildcards()
    {
        var result = RenameEngine.Preview(
            "IMG_2024_final.jpg",
            new RenameOptions { ReplaceText = "IMG_*_", ReplaceWith = "photo-" },
            1);

        Assert.Equal("photo-final.jpg", result);
    }

    [Fact]
    public void Preview_TrailingWildcardMatchesToEndOfName()
    {
        var result = RenameEngine.Preview(
            "Test_file_01_track32_[por]31.srt",
            new RenameOptions { ReplaceText = "_track*" },
            1);

        Assert.Equal("Test_file_01.srt", result);
    }

    [Fact]
    public void Preview_RemovesFirstLastRangeCharsAndWords()
    {
        var result = RenameEngine.Preview(
            "AB_holiday_photo_2024_xy.jpg",
            new RenameOptions { RemoveFirst = 3, RemoveLast = 3, RemoveFrom = 9, RemoveTo = 14, RemoveChars = "_", RemoveWords = "photo" },
            1);

        Assert.Equal("holiday2024.jpg", result);
    }

    [Fact]
    public void Preview_CropAndCleanupOptions()
    {
        Assert.Equal("beta_c.txt", RenameEngine.Preview("alpha_beta_c.txt", new RenameOptions { CropMode = "Before", CropText = "beta" }, 1));
        Assert.Equal("alpha.txt", RenameEngine.Preview("alpha_beta.txt", new RenameOptions { CropMode = "After", CropText = "alpha" }, 1));
        Assert.Equal("Cafe ab.txt", RenameEngine.Preview(".Café 12  a-b!.txt", new RenameOptions
        {
            RemoveAccents = true, RemoveDigits = true, RemoveSymbols = true, RemoveDoubleSpaces = true, TrimSpaces = true, LeadDots = "All"
        }, 1));
    }

    [Fact]
    public void Preview_AppendsFolderNames()
    {
        var options = new RenameOptions { FolderMode = "Prefix", FolderSeparator = "_", FolderLevels = 2 };
        Assert.Equal("Trips_Italy_a.jpg", RenameEngine.Preview("a.jpg", options, 1, "/photos/Trips/Italy"));
        Assert.Equal("a-Italy.jpg", RenameEngine.Preview("a.jpg", new RenameOptions { FolderMode = "Suffix", FolderSeparator = "-" }, 1, "/photos/Trips/Italy"));
    }

    [Fact]
    public void Preview_AddsPrefixInsertAndSuffix()
    {
        var result = RenameEngine.Preview(
            "Hello.txt",
            new RenameOptions { InsertText = "-", InsertPosition = 5, Prefix = "<", Suffix = ">" },
            1);

        Assert.Equal("<Hello->.txt", result);
    }

    [Fact]
    public void Preview_CanApplyRegularExpressionToExtension()
    {
        var result = RenameEngine.Preview(
            "scene.jpg",
            new RenameOptions
            {
                RegexPattern = @"\.jpg$",
                RegexReplacement = ".png",
                RegexIncludeExtension = true
            },
            1);

        Assert.Equal("scene.png", result);
    }

    [Fact]
    public void Preview_NumberingSupportsPreSuffixAndInsert()
    {
        var preSuffix = RenameEngine.Preview(
            "file.txt",
            new RenameOptions { NumberMode = "Pre+Suffix", NumberStart = 1, NumberSeparator = "_" },
            1);
        Assert.Equal("1_file_1.txt", preSuffix);

        var insert = RenameEngine.Preview(
            "document.txt",
            new RenameOptions { NumberMode = "Insert", NumberAt = 3, NumberStart = 5, NumberSeparator = "-" },
            1);
        Assert.Equal("doc5-ument.txt", insert);
    }

    [Fact]
    public void Preview_NumberingSupportsBreakCycling()
    {
        var options = new RenameOptions { NumberMode = "Suffix", NumberStart = 1, NumberIncrement = 1, NumberBreak = 2, NumberSeparator = "_" };
        Assert.Equal("file_1.txt", RenameEngine.Preview("file.txt", options, 1));
        Assert.Equal("file_2.txt", RenameEngine.Preview("file.txt", options, 2));
        Assert.Equal("file_1.txt", RenameEngine.Preview("file.txt", options, 3));
    }

    [Fact]
    public void Preview_NumberingSupportsHexRomanAndAlphabet()
    {
        var hex = RenameEngine.Preview(
            "file.txt",
            new RenameOptions { NumberMode = "Prefix", NumberType = "Base 16 (Hex)", NumberCase = "Lower", NumberPadding = 3, NumberSeparator = "_" },
            10); // 1 + 9 = 10 -> 00a
        Assert.Equal("00a_file.txt", hex);

        var roman = RenameEngine.Preview(
            "chapter.txt",
            new RenameOptions { NumberMode = "Suffix", NumberType = "Roman", NumberCase = "Upper", NumberStart = 4, NumberSeparator = " " },
            1);
        Assert.Equal("chapter IV.txt", roman);

        var alpha = RenameEngine.Preview(
            "item.txt",
            new RenameOptions { NumberMode = "Prefix", NumberType = "Alphabet", NumberCase = "Lower", NumberStart = 27, NumberSeparator = "-" },
            1);
        Assert.Equal("aa-item.txt", alpha);
    }
}

public sealed class RenameTransactionTests
{
    [Fact]
    public void Execute_SupportsSwappingNames()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var first = Path.Combine(directory, "first.txt");
            var second = Path.Combine(directory, "second.txt");
            File.WriteAllText(first, "first contents");
            File.WriteAllText(second, "second contents");

            RenameTransaction.Execute(
            [
                new RenamePlan(first, "second.txt", false),
                new RenamePlan(second, "first.txt", false)
            ]);

            Assert.Equal("second contents", File.ReadAllText(first));
            Assert.Equal("first contents", File.ReadAllText(second));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_RejectsExistingDestinationWithoutChangingSources()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.txt");
            var destination = Path.Combine(directory, "taken.txt");
            File.WriteAllText(source, "source");
            File.WriteAllText(destination, "destination");

            Assert.Throws<IOException>(() =>
                RenameTransaction.Execute([new RenamePlan(source, "taken.txt", false)]));

            Assert.Equal("source", File.ReadAllText(source));
            Assert.Equal("destination", File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bulk-rename-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
