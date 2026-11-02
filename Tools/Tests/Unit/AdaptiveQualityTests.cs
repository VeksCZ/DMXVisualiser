using Xunit;

public class AdaptiveQualityTests
{
    const float Hz = 72f;

    // Simuluje sekundy běhu s konstantním FPS; vrací počet změn úrovně.
    static int Run(AdaptiveQuality q, float seconds, float fps)
    {
        int changes = 0;
        float dt = 1f / fps;
        for (float t = 0; t < seconds; t += dt) if (q.Update(dt, Hz)) changes++;
        return changes;
    }

    [Fact]
    public void StaysAtZeroWhenFpsIsFine()
    {
        var q = new AdaptiveQuality();
        Assert.Equal(0, Run(q, 60, 72));
        Assert.Equal(0, q.Level);
    }

    [Fact]
    public void StepsDownWhenFpsStaysLow()
    {
        var q = new AdaptiveQuality();
        Run(q, 5, 55);
        Assert.Equal(1, q.Level);
    }

    [Fact]
    public void ContinuesDownToMaxButNotBeyond()
    {
        var q = new AdaptiveQuality();
        Run(q, 120, 40);
        Assert.Equal(AdaptiveQuality.MaxLevel, q.Level);
    }

    [Fact]
    public void ShortDipDoesNotChangeAnything()
    {
        var q = new AdaptiveQuality();
        Run(q, 10, 72);
        Run(q, 1, 45);              // 1 s je míň než downAfter
        Run(q, 10, 72);
        Assert.Equal(0, q.Level);
    }

    [Fact]
    public void HitchesAreIgnored()
    {
        var q = new AdaptiveQuality();
        for (int i = 0; i < 100; i++) { q.Update(1f / 72f, Hz); q.Update(2f, Hz); }   // opakované zásekové snímky (načítání)
        Assert.Equal(0, q.Level);
    }

    [Fact]
    public void RecoversAfterLongHeadroom()
    {
        var q = new AdaptiveQuality();
        Run(q, 5, 55);
        Assert.Equal(1, q.Level);
        Run(q, 40, 72);
        Assert.Equal(0, q.Level);
    }

    [Fact]
    public void DoesNotRecoverTooEarly()
    {
        var q = new AdaptiveQuality();
        Run(q, 5, 55);
        Run(q, 8, 72);
        Assert.Equal(1, q.Level);
    }

    [Fact]
    public void CooldownLimitsChangeRate()
    {
        var q = new AdaptiveQuality(downAfter: 0.1f, upAfter: 15f, cooldown: 4f);
        int changes = Run(q, 8.5f, 30);
        Assert.InRange(changes, 1, 3);        // nejvýš jedna změna za 4 s (+ první hned)
    }

    [Fact]
    public void OscillationIsDampedByBackoff()
    {
        // 60 fps na úrovni 0, 72 fps na vyšších úrovních → zvýšení se hned vrátí; interval se musí prodlužovat
        var q = new AdaptiveQuality();
        int changes = 0;
        float dt = 1f / 72f;
        for (float t = 0; t < 600; t += dt)
        {
            float fps = q.Level == 0 ? 55f : 72f;
            if (q.Update(1f / fps, Hz)) changes++;
        }
        Assert.InRange(changes, 2, 22);       // bez zpomalování by to bylo ~60
        Assert.True(q.Level <= 1);
    }

    [Fact]
    public void InvalidInputIsIgnored()
    {
        var q = new AdaptiveQuality();
        Assert.False(q.Update(0f, Hz));
        Assert.False(q.Update(0.01f, 0f));
        Assert.Equal(0, q.Level);
    }

    [Fact]
    public void ResetGoesBackToFullQuality()
    {
        var q = new AdaptiveQuality();
        Run(q, 30, 40);
        q.Reset();
        Assert.Equal(0, q.Level);
    }

    [Fact]
    public void LevelTablesAreMonotonic()
    {
        for (int l = 1; l <= AdaptiveQuality.MaxLevel; l++)
        {
            Assert.True(AdaptiveQuality.RenderScaleFactor(l) < AdaptiveQuality.RenderScaleFactor(l - 1));
            Assert.True(AdaptiveQuality.LightCullThreshold(l) > AdaptiveQuality.LightCullThreshold(l - 1));
        }
        Assert.Equal(1f, AdaptiveQuality.RenderScaleFactor(0));
        Assert.True(AdaptiveQuality.RenderScaleFactor(AdaptiveQuality.MaxLevel) >= 0.6f);
    }
}
