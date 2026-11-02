using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Screenshot scény bez rozhraní (F12 nebo Soubor → Screenshot). Součást VisualizerMenu (VisualizerMenu.cs).
public partial class VisualizerMenu
{
    bool shooting;

    static string ScreenshotsDir =>
#if UNITY_EDITOR
        Path.Combine(Path.GetDirectoryName(Application.dataPath), "Build", "Screenshots");
#else
        Path.Combine(Path.GetDirectoryName(VisualizerSettings.FilePath), "Screenshots");
#endif

    void TakeScreenshot()
    {
        if (shooting || VRRig.IsVR) return;      // ve VR se snímek dělá ovladačem brýlí
        StartCoroutine(ScreenshotRoutine());
    }

    IEnumerator ScreenshotRoutine()
    {
        shooting = true;
        bool wasVisible = uiVisible;
        uiVisible = false;                        // rozhraní se do snímku nedostane
        yield return null;
        yield return new WaitForEndOfFrame();
        string path = null;
        try
        {
            Directory.CreateDirectory(ScreenshotsDir);
            path = Path.Combine(ScreenshotsDir, "DMXVisualiser-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
        }
        catch (Exception e) { Debug.LogWarning("Screenshot failed: " + e.Message); path = null; }
        yield return null;
        uiVisible = wasVisible;
        Flash(path != null ? Loc.F("screenshotSaved", Path.GetFileName(path)) : Loc.T("screenshotFailed"));
        shooting = false;
    }
}
