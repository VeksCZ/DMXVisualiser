using System.Collections.Generic;
using Xunit;

public class WindowPlacementTests
{
    [Fact]
    public void FindsDisplayByName()
    {
        var names = new List<string> { "DELL U2720Q", "Projector" };
        Assert.Equal(1, WindowPlacement.FindDisplay(names, "Projector"));
        Assert.Equal(0, WindowPlacement.FindDisplay(names, "DELL U2720Q"));
    }

    [Fact]
    public void UnknownOrEmptyDisplayIsNotFound()
    {
        var names = new List<string> { "A", "B" };
        Assert.Equal(-1, WindowPlacement.FindDisplay(names, "C"));
        Assert.Equal(-1, WindowPlacement.FindDisplay(names, ""));
        Assert.Equal(-1, WindowPlacement.FindDisplay(names, null));
        Assert.Equal(-1, WindowPlacement.FindDisplay(null, "A"));
        Assert.Equal(-1, WindowPlacement.FindDisplay(new List<string>(), "A"));
    }

    [Fact]
    public void WindowInsideDisplayIsVisible() => Assert.True(WindowPlacement.IsVisible(100, 50, 1600, 900, 1920, 1080));

    [Fact]
    public void WindowMostlyOffscreenButGrabbableIsVisible() => Assert.True(WindowPlacement.IsVisible(1700, 100, 1600, 900, 1920, 1080));

    [Fact]
    public void WindowCompletelyOffscreenIsNot()
    {
        Assert.False(WindowPlacement.IsVisible(2000, 100, 1600, 900, 1920, 1080));      // vpravo mimo
        Assert.False(WindowPlacement.IsVisible(-1600, 100, 1600, 900, 1920, 1080));     // vlevo mimo
        Assert.False(WindowPlacement.IsVisible(1850, 100, 1600, 900, 1920, 1080));      // jen 70 px vidět
    }

    [Fact]
    public void TitleBarAboveOrBelowDisplayIsNotVisible()
    {
        Assert.False(WindowPlacement.IsVisible(100, -40, 1600, 900, 1920, 1080));
        Assert.False(WindowPlacement.IsVisible(100, 1050, 1600, 900, 1920, 1080));
    }

    [Fact]
    public void SmallerMonitorThanBefore()
    {
        // uloženo na 4K, teď FullHD – okno daleko vpravo se nesmí obnovit
        Assert.False(WindowPlacement.IsVisible(3000, 100, 1600, 900, 1920, 1080));
    }

    [Fact]
    public void InvalidSizesAreNotVisible()
    {
        Assert.False(WindowPlacement.IsVisible(0, 0, 0, 900, 1920, 1080));
        Assert.False(WindowPlacement.IsVisible(0, 0, 1600, 900, 0, 1080));
    }

    [Fact]
    public void NextDisplayCycles()
    {
        Assert.Equal(1, WindowPlacement.NextIndex(0, 2));
        Assert.Equal(0, WindowPlacement.NextIndex(1, 2));
        Assert.Equal(0, WindowPlacement.NextIndex(-1, 3));
        Assert.Equal(-1, WindowPlacement.NextIndex(0, 0));
    }
}

public class ErrorLogThrottleTests
{
    [Fact]
    public void FirstOccurrenceIsWritten()
    {
        var t = new ErrorLogThrottle();
        Assert.True(t.ShouldWrite("boom", 0, out int sup));
        Assert.Equal(0, sup);
    }

    [Fact]
    public void RepeatsAreSuppressedWithinWindow()
    {
        var t = new ErrorLogThrottle(windowSeconds: 60);
        t.ShouldWrite("boom", 0, out _);
        for (int i = 1; i <= 1000; i++) Assert.False(t.ShouldWrite("boom", i * 0.016, out _));
    }

    [Fact]
    public void AfterWindowItIsWrittenAgainWithCount()
    {
        var t = new ErrorLogThrottle(windowSeconds: 60);
        t.ShouldWrite("boom", 0, out _);
        for (int i = 0; i < 500; i++) t.ShouldWrite("boom", 1 + i * 0.01, out _);
        Assert.True(t.ShouldWrite("boom", 61, out int sup));
        Assert.Equal(500, sup);
        Assert.False(t.ShouldWrite("boom", 62, out sup));
        Assert.Equal(0, sup);
    }

    [Fact]
    public void DifferentMessagesAreIndependent()
    {
        var t = new ErrorLogThrottle();
        Assert.True(t.ShouldWrite("a", 0, out _));
        Assert.True(t.ShouldWrite("b", 0, out _));
        Assert.False(t.ShouldWrite("a", 1, out _));
    }

    [Fact]
    public void MemoryIsBounded()
    {
        var t = new ErrorLogThrottle(maxKeys: 10);
        for (int i = 0; i < 1000; i++) Assert.True(t.ShouldWrite("msg" + i, i, out _));   // vždy nové zprávy, nepadne ani nezaroste
        Assert.True(t.ShouldWrite(null, 5000, out _));
    }
}
