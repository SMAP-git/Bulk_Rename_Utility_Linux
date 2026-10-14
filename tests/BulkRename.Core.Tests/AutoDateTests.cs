using BulkRename.Core;

namespace BulkRename.Core.Tests;

public sealed class AutoDateTests
{
    private static readonly FileDateTimes Dates = new(
        new DateTime(2024, 2, 29, 13, 5, 9),
        new DateTime(2025, 6, 12, 8, 30, 45),
        new DateTime(2026, 10, 14, 23, 59, 1));

    [Fact]
    public void DisabledOrNone_DoesNotRequireMetadataOrValidateFormat()
    {
        Assert.Equal("photo.jpg", RenameEngine.Preview("photo.jpg",
            new RenameOptions { DateMode = "None", DateFormat = "Invalid" }, 1));
        Assert.Equal("photo.jpg", RenameEngine.Preview("photo.jpg",
            new RenameOptions { DateEnabled = false, DateMode = "Prefix", DateFormat = "Custom" }, 1));
    }

    [Theory]
    [InlineData("Created", "2024-02-29")]
    [InlineData("Modified", "2025-06-12")]
    [InlineData("Accessed", "2026-10-14")]
    [InlineData("Current", "2030-12-31")]
    public void UsesSelectedDateSource(string type, string expected)
    {
        var options = new RenameOptions
        {
            DateMode = "Prefix", DateType = type, DateFormat = "YMD",
            DateCentury = true, DateSegmentSeparator = "-", DateSeparator = "_"
        };
        Assert.Equal(expected + "_photo.jpg",
            RenameEngine.Preview("photo.jpg", options, 1, dates: Dates, currentDate: new DateTime(2030, 12, 31)));
    }

    [Theory]
    [InlineData("Prefix", 0, "290224_photo.jpg")]
    [InlineData("Suffix", 0, "photo_290224.jpg")]
    [InlineData("None", 1, "290224_photo.jpg")]
    [InlineData("None", 3, "ph290224_oto.jpg")]
    [InlineData("None", 999, "photo290224_.jpg")]
    [InlineData("Prefix", 3, "ph290224_oto.jpg")]
    [InlineData("Suffix", 3, "ph290224_oto.jpg")]
    [InlineData("Replace", 3, "ph290224_oto.jpg")]
    [InlineData("Replace", 0, "290224.jpg")]
    public void PlacementPreservesExtension(string mode, int position, string expected)
    {
        var options = new RenameOptions { DateMode = mode, DateInsertPosition = position, DateSeparator = "_" };
        Assert.Equal(expected, RenameEngine.Preview("photo.jpg", options, 1, dates: Dates));
    }

    [Fact]
    public void ZeroPositionReturnsPlacementToMode()
    {
        var inserting = new RenameOptions { DateMode = "Suffix", DateInsertPosition = 1, DateSeparator = "_" };
        var suffix = new RenameOptions { DateMode = "Suffix", DateInsertPosition = 0, DateSeparator = "_" };
        Assert.Equal("290224_photo.jpg", RenameEngine.Preview("photo.jpg", inserting, 1, dates: Dates));
        Assert.Equal("photo_290224.jpg", RenameEngine.Preview("photo.jpg", suffix, 1, dates: Dates));
    }

    [Fact]
    public void NegativePositionRaisesError()
    {
        var options = new RenameOptions { DateInsertPosition = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => RenameEngine.Preview("photo.jpg", options, 1, dates: Dates));
    }

    [Theory]
    [InlineData("DMY", "29-02-24")]
    [InlineData("MDY", "02-29-24")]
    [InlineData("YMD", "24-02-29")]
    [InlineData("YDM", "24-29-02")]
    [InlineData("MYD", "02-24-29")]
    [InlineData("DYM", "29-24-02")]
    [InlineData("HM", "13-05")]
    [InlineData("HMS", "13-05-09")]
    [InlineData("DMYHMS", "29-02-24-13-05-09")]
    [InlineData("MDYHMS", "02-29-24-13-05-09")]
    [InlineData("YMDHMS", "24-02-29-13-05-09")]
    public void FormatsPresetSegments(string format, string expected)
    {
        var options = new RenameOptions { DateMode = "Replace", DateFormat = format, DateSegmentSeparator = "-" };
        Assert.Equal(expected + ".jpg", RenameEngine.Preview("photo.jpg", options, 1, dates: Dates));
    }

    [Theory]
    [InlineData(-1, "28-02-2024")]
    [InlineData(1, "01-03-2024")]
    [InlineData(307, "01-01-2025")]
    public void OffsetHandlesLeapDaysAndYearBoundaries(int days, string expected)
    {
        var options = new RenameOptions
        {
            DateMode = "Replace", DateCentury = true, DateOffsetDays = days, DateSegmentSeparator = "-"
        };
        Assert.Equal(expected + ".jpg", RenameEngine.Preview("photo.jpg", options, 1, dates: Dates));
    }

    [Theory]
    [InlineData("%Y-%m-%d_%H-%M-%S", "2024-02-29_13-05-09")]
    [InlineData("%y_%I-%M%p_%%", "24_01-05PM_%")]
    [InlineData("%a_%A_%b_%B_%h", "Thu_Thursday_Feb_February_Feb")]
    [InlineData("%j_%w_%u", "060_4_4")]
    [InlineData("%F_%T_%R", "2024-02-29_13:05:09_13:05")]
    [InlineData("literal", "literal")]
    public void FormatsCustomPercentTokens(string format, string expected)
    {
        var options = new RenameOptions { DateMode = "Replace", DateFormat = "Custom", DateCustomFormat = format };
        Assert.Equal(expected + ".jpg", RenameEngine.Preview("photo.jpg", options, 1, dates: Dates));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%")]
    [InlineData("%Q")]
    public void InvalidCustomFormatRaisesError(string format)
    {
        var options = new RenameOptions { DateMode = "Prefix", DateFormat = "Custom", DateCustomFormat = format };
        Assert.Throws<FormatException>(() => RenameEngine.Preview("photo.jpg", options, 1, dates: Dates));
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("Modified")]
    [InlineData("Accessed")]
    public void MissingMetadataRaisesErrorInsteadOfUsingCurrentDate(string type)
    {
        var options = new RenameOptions { DateMode = "Prefix", DateType = type };
        Assert.Throws<ArgumentException>(() => RenameEngine.Preview("photo.jpg", options, 1));
        Assert.Throws<ArgumentException>(() => RenameEngine.Preview("photo.jpg", options, 1,
            dates: new FileDateTimes(DateTime.MinValue, DateTime.MinValue, DateTime.MinValue)));
    }

    [Fact]
    public void OffsetOverflowRaisesError()
    {
        var options = new RenameOptions { DateMode = "Prefix", DateOffsetDays = int.MaxValue };
        Assert.Throws<ArgumentOutOfRangeException>(() => RenameEngine.Preview("photo.jpg", options, 1, dates: Dates));
    }

    [Fact]
    public void CombinesWithExistingOperationsAndExtensionChanges()
    {
        var options = new RenameOptions
        {
            Prefix = "edited-", NumberMode = "Suffix", NumberSeparator = "_",
            DateMode = "Suffix", DateSeparator = "_", ExtensionMode = "UPPERCASE"
        };
        Assert.Equal("edited-photo_1_290224.JPG", RenameEngine.Preview("photo.jpg", options, 1, dates: Dates));
        Assert.Equal("edited-folder_1_290224", RenameEngine.Preview("folder", options, 1, dates: Dates));
    }

    [Fact]
    public void CurrentDateDoesNotRequireFileMetadata()
    {
        var options = new RenameOptions { DateMode = "Replace", DateType = "Current", DateFormat = "YMD", DateCentury = true };
        Assert.Equal("20301231.txt",
            RenameEngine.Preview("file.txt", options, 1, currentDate: new DateTime(2030, 12, 31)));
    }
}
