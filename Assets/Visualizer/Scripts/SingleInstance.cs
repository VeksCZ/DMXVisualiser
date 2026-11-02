#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

// Druhá kopie aplikace by sdílela port 6454 i settings.json. Proto smí běžet jen jedna.
// Po aktualizaci nová verze počká, až stará skončí (nejdéle 6 s, QuitWatchdog ji ukončí do 3 s).
public static class SingleInstance
{
    static Mutex mutex;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Guard()
    {
        try
        {
            mutex = new Mutex(true, "Local\\DMXVisualiser.SingleInstance", out bool created);
            if (created) return;
            bool got;
            try { got = mutex.WaitOne(6000); }
            catch (AbandonedMutexException) { got = true; }     // předchozí instance spadla
            if (got) return;
        }
        catch (Exception e) { Debug.LogWarning("Single instance check failed: " + e.Message); return; }

        bool cs = Application.systemLanguage == SystemLanguage.Czech || Application.systemLanguage == SystemLanguage.Slovak;
        MessageBox(IntPtr.Zero,
            cs ? "DMX Visualiser už běží. Druhou kopii nelze spustit (sdílely by Art-Net port a nastavení)." : "DMX Visualiser is already running. A second copy cannot start (they would share the Art-Net port and settings).",
            "DMX Visualiser", 0x40);
        System.Diagnostics.Process.GetCurrentProcess().Kill();
    }
}
#endif
