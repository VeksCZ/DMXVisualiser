using System;

// Automatická kvalita podle FPS (Quest): když snímková frekvence dlouho nestíhá, sníží se úroveň (0–3),
// když je dlouho rezerva, zase se zvýší. Čistá logika bez Unity (má unit testy); použití viz QualityGovernor.cs.
public sealed class AdaptiveQuality
{
    public const int MaxLevel = 3;

    public int Level { get; private set; }

    readonly float downAfter, upAfter, cooldown;
    float upAfterCur;
    float smoothed = -1f, below, above, sinceChange = 1000f, sinceUp = 1000f;

    // downAfter: jak dlouho musí být FPS nízko, než se kvalita sníží; upAfter: jak dlouho musí být rezerva, než se zvýší;
    // cooldown: nejkratší pauza mezi dvěma změnami.
    public AdaptiveQuality(float downAfter = 3f, float upAfter = 15f, float cooldown = 4f)
    {
        this.downAfter = downAfter;
        this.upAfter = upAfterCur = upAfter;
        this.cooldown = cooldown;
    }

    public void Reset()
    {
        Level = 0; smoothed = -1f; below = above = 0f; sinceChange = sinceUp = 1000f; upAfterCur = upAfter;
    }

    // Volat každý snímek. dt = délka snímku (s), targetFps = obnovovací frekvence displeje. Vrací true, když se Level změnil.
    public bool Update(float dt, float targetFps)
    {
        if (dt <= 0f || targetFps <= 0f) return false;
        sinceChange += dt;
        sinceUp += dt;
        if (dt > 0.5f) { below = above = 0f; return false; }          // zásek (načítání, pauza) neříká nic o výkonu

        float fps = 1f / dt;
        if (smoothed < 0f) smoothed = targetFps;
        smoothed += (fps - smoothed) * (1f - (float)Math.Exp(-dt / 0.5f));

        if (smoothed < targetFps * 0.93f) { below += dt; above = 0f; }
        else if (smoothed >= targetFps * 0.985f) { above += dt; below = 0f; }
        else { below = above = 0f; }

        if (sinceChange < cooldown) return false;
        if (below >= downAfter && Level < MaxLevel)
        {
            // hned po zvýšení zase pokles = zvýšení bylo předčasné → příště čekat dvakrát déle
            if (sinceUp < 30f) upAfterCur = Math.Min(upAfterCur * 2f, 240f);
            Level++;
            Changed();
            return true;
        }
        if (above >= upAfterCur && Level > 0)
        {
            Level--;
            sinceUp = 0f;
            Changed();
            return true;
        }
        return false;
    }

    void Changed() { below = above = 0f; sinceChange = 0f; smoothed = -1f; }

    // Násobek render scale (v passthrough se nepoužije – tam musí zůstat 1).
    public static float RenderScaleFactor(int level)
    {
        switch (level) { case 0: return 1f; case 1: return 0.9f; case 2: return 0.8f; default: return 0.7f; }
    }

    // Světla slabší než tento jas se vypnou (nejsou prakticky vidět, ale stojí výkon).
    public static float LightCullThreshold(int level)
    {
        switch (level) { case 0: return 0.003f; case 1: return 0.03f; case 2: return 0.08f; default: return 0.15f; }
    }
}
