using System;
using System.Collections.Generic;

// Pozice okna a výběr monitoru bez Unity (má unit testy). Souřadnice jsou vůči levému hornímu rohu monitoru.
public static class WindowPlacement
{
    // Index monitoru podle uloženého názvu; -1 = nenalezen (zůstane se na aktuálním).
    public static int FindDisplay(IList<string> names, string wanted)
    {
        if (names == null || string.IsNullOrEmpty(wanted)) return -1;
        for (int i = 0; i < names.Count; i++)
            if (string.Equals(names[i], wanted, StringComparison.Ordinal)) return i;
        return -1;
    }

    // Okno musí jít chytit za titulek: horní hrana v monitoru a aspoň minVisible px na šířku vidět.
    public static bool IsVisible(int x, int y, int w, int h, int dispW, int dispH, int minVisible = 120)
    {
        if (w <= 0 || h <= 0 || dispW <= 0 || dispH <= 0) return false;
        if (y < 0 || y > dispH - 60) return false;
        int visibleW = Math.Min(x + w, dispW) - Math.Max(x, 0);
        return visibleW >= minVisible;
    }

    // Další monitor v kruhu (current < 0 = neznámý → první).
    public static int NextIndex(int current, int count) => count <= 0 ? -1 : (current + 1) % count;
}
