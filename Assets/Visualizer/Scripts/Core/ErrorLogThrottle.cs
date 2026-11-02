using System;
using System.Collections.Generic;

// Chyba, která se opakuje každý snímek (výjimka v Update), nesmí zaplnit log: stejná zpráva se zapíše jednou,
// při další změně nebo po windowSeconds se připojí počet potlačených opakování. Bez Unity (má unit testy).
public sealed class ErrorLogThrottle
{
    readonly double windowSeconds;
    readonly int maxKeys;
    readonly Dictionary<string, Entry> seen = new Dictionary<string, Entry>();

    class Entry { public double lastWritten; public int suppressed; }

    public ErrorLogThrottle(double windowSeconds = 60.0, int maxKeys = 200)
    {
        this.windowSeconds = windowSeconds;
        this.maxKeys = maxKeys;
    }

    // true = zapsat. suppressed = kolik stejných zpráv se od posledního zápisu přeskočilo.
    public bool ShouldWrite(string key, double nowSeconds, out int suppressed)
    {
        suppressed = 0;
        key = key ?? "";
        if (!seen.TryGetValue(key, out var e))
        {
            if (seen.Count >= maxKeys) seen.Clear();            // pojistka proti nekonečně různým zprávám
            seen[key] = new Entry { lastWritten = nowSeconds };
            return true;
        }
        if (nowSeconds - e.lastWritten >= windowSeconds)
        {
            suppressed = e.suppressed;
            e.suppressed = 0;
            e.lastWritten = nowSeconds;
            return true;
        }
        e.suppressed++;
        return false;
    }
}
