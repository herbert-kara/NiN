namespace ServiceLib.Tests.Common;

/// <summary>
/// The grid has to stay readable at every window size: a header must never be cut
/// mid-word, and the columns must add up to the width the grid was given instead of
/// stopping short of it.
/// </summary>
public class ColumnWidthFitTests
{
    [Test]
    public async Task Fit_ShouldKeepAHeaderWiderThanItsCells()
    {
        // "Delay (ms)" over a cell that renders "-1": the header is what needs the room.
        await ColumnWidthFit.Fit([52, 26], [110, 92], 0).Should().BeEquivalentTo([110d, 92d], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Fit_ShouldKeepContentWiderThanItsHeader()
    {
        await ColumnWidthFit.Fit([300, 40], [90, 40], 0).Should().BeEquivalentTo([300d, ColumnWidthFit.MinWidth], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Fit_ShouldNeverGoBelowTheMinimumWidth()
    {
        await ColumnWidthFit.Fit([0, 4], [0, 6], 0).Should().BeEquivalentTo([ColumnWidthFit.MinWidth, ColumnWidthFit.MinWidth], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Fit_ShouldSpendTheSlackOnTheWidestColumn()
    {
        var widths = ColumnWidthFit.Fit([180, 60], [90, 70], 500);

        await widths.Sum().Should().BeEqualTo(500);
        await widths[0].Should().BeEqualTo(430);
        await widths[1].Should().BeEqualTo(70);
    }

    [Test]
    public async Task Fit_ShouldTakeTheExcessFromTheWidestColumnFirst()
    {
        var widths = ColumnWidthFit.Fit([600, 60], [90, 92], 400);

        await widths.Sum().Should().BeEqualTo(400);
        await widths[0].Should().BeEqualTo(308);
        await widths[1].Should().BeEqualTo(92);
    }

    [Test]
    public async Task Fit_ShouldLeaveTheColumnsAloneWhenTheGridHasNoWidthYet()
    {
        await ColumnWidthFit.Fit([180, 60], [90, 70], 0).Should().BeEquivalentTo([180d, 70d], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Fit_ShouldRefuseMixedLengths()
    {
        Action fit = () => ColumnWidthFit.Fit([10, 10], [10], 100);

        await fit.Should().Throw<ArgumentException>();
    }
}