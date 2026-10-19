using PanelTracker.Core.Scanning;

namespace PanelTracker.Core.Tests;

public class PanelFileNameParserTests
{
    [Theory]
    [InlineData("N021", "N021", null)]
    [InlineData("N021_R2", "N021", 2)]
    [InlineData("N021-R12", "N021", 12)]
    [InlineData("N021 R3", "N021", 3)]
    [InlineData("N021_rev4", "N021", 4)]
    [InlineData("N021_Rev.5", "N021", 5)]
    [InlineData("N021 (R6)", "N021", 6)]
    [InlineData("W046", "W046", null)]
    [InlineData("S177_R01", "S177", 1)]
    [InlineData("WR2", "WR2", null)]
    [InlineData("N021_R2_old", "N021_R2_old", null)]
    [InlineData("  N021_R2  ", "N021", 2)]
    public void Parses_identifier_and_revision(string name, string expectedId, int? expectedRevision)
    {
        var parsed = PanelFileNameParser.Parse(name);

        Assert.Equal(expectedId, parsed.PanelId);
        Assert.Equal(expectedRevision, parsed.Revision);
    }

    [Fact]
    public void ParsePath_strips_directory_and_extension()
    {
        var parsed = PanelFileNameParser.ParsePath(Path.Combine("C:", "proj", "01 Ready", "N021_R2.pdf"));

        Assert.Equal("N021", parsed.PanelId);
        Assert.Equal(2, parsed.Revision);
    }
}
