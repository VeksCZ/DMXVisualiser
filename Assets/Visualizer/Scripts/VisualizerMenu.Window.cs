using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Okno aplikace na PC: zapamatování pozice a monitoru, přesun na další monitor (projektor). Součást VisualizerMenu (VisualizerMenu.cs).
public partial class VisualizerMenu
{
    static bool WindowFeatures => !Application.isEditor && !VRRig.IsVR && Application.platform == RuntimePlatform.WindowsPlayer;

    // Uloží monitor a (v okně) pozici. Volá se při ukončení a před přepnutím na celou obrazovku.
    void CaptureWindow()
    {
        if (!WindowFeatures || s == null) return;
        try
        {
            s.displayName = Screen.mainWindowDisplayInfo.name ?? "";
            if (!IsFullscreen)
            {
                var pos = Screen.mainWindowPosition;
                s.windowX = pos.x; s.windowY = pos.y; s.hasWindowPos = true;
            }
        }
        catch (Exception e) { Debug.LogWarning("CaptureWindow: " + e.Message); }
    }

    // Po startu: okno zpátky na uložený monitor a místo (jen když je pozice na monitoru vidět).
    IEnumerator RestoreWindow()
    {
        yield return null;
        yield return null;                 // SetResolution musí proběhnout dřív než přesun
        var layout = new List<DisplayInfo>();
        Screen.GetDisplayLayout(layout);
        if (layout.Count == 0) yield break;
        var names = new List<string>();
        foreach (var di in layout) names.Add(di.name);
        int idx = WindowPlacement.FindDisplay(names, s.displayName);
        if (idx < 0) yield break;          // uložený monitor není připojený → zůstane se tam, kde okno vzniklo
        var disp = layout[idx];

        if (s.fullscreen)
        {
            yield return Screen.MoveMainWindowTo(disp, Vector2Int.zero);
            Screen.SetResolution(disp.width, disp.height, FullScreenMode.FullScreenWindow);
        }
        else if (s.hasWindowPos && WindowPlacement.IsVisible(s.windowX, s.windowY, s.windowWidth, s.windowHeight, disp.width, disp.height))
            yield return Screen.MoveMainWindowTo(disp, new Vector2Int(s.windowX, s.windowY));
    }

    // Soubor → Přesunout na další monitor (jen když jsou víc než jeden)
    int DisplayCount()
    {
        if (!WindowFeatures) return 1;
        var l = new List<DisplayInfo>();
        Screen.GetDisplayLayout(l);
        return l.Count;
    }

    void MoveToNextDisplay()
    {
        if (!WindowFeatures) return;
        StartCoroutine(MoveToNextDisplayRoutine());
    }

    IEnumerator MoveToNextDisplayRoutine()
    {
        var layout = new List<DisplayInfo>();
        Screen.GetDisplayLayout(layout);
        if (layout.Count < 2) yield break;
        var cur = Screen.mainWindowDisplayInfo;
        int curIdx = layout.FindIndex(x => x.name == cur.name);
        var next = layout[WindowPlacement.NextIndex(curIdx, layout.Count)];
        bool full = IsFullscreen;
        if (full) Screen.SetResolution(Screen.width, Screen.height, FullScreenMode.Windowed);   // z celé obrazovky nejde přímo na jiný monitor
        yield return null;
        yield return Screen.MoveMainWindowTo(next, full ? Vector2Int.zero : new Vector2Int(60, 60));
        if (full) Screen.SetResolution(next.width, next.height, FullScreenMode.FullScreenWindow);
        CaptureWindow();
        s.Save();
    }
}
