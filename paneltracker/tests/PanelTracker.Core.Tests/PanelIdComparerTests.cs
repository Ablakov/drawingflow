using PanelTracker.Core.Scanning;

namespace PanelTracker.Core.Tests;

public class PanelIdComparerTests
{
    [Fact]
    public void Sorts_numerically_inside_identifiers()
    {
        var ids = new[] { "N10", "N2", "N1", "W046", "S177", "n3", "N021", "N10a" };

        var sorted = ids.OrderBy(x => x, PanelIdComparer.Instance).ToArray();

        Assert.Equal(new[] { "N1", "N2", "n3", "N10", "N10a", "N021", "S177", "W046" }, sorted);
    }

    [Fact]
    public void Equal_numbers_with_different_zero_padding_keep_stable_order()
    {
        Assert.Equal(0, PanelIdComparer.Instance.Compare("N21", "N021"));
        Assert.True(PanelIdComparer.Instance.Compare("N21", "N22") < 0);
        Assert.True(PanelIdComparer.Instance.Compare(null, "A") < 0);
    }
}
