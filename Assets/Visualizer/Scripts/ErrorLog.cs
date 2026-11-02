using System;
using System.IO;
using UnityEngine;

// Chyby a výjimky se zapisují do errors.log vedle aplikace (Windows: složka s .exe; jinde persistentDataPath),
// aby šly poslat při hlášení problému. Opakované stejné chyby se potlačí (ErrorLogThrottle), soubor je omezený na ~512 kB.
public static class ErrorLog
{
    const long MaxBytes = 512 * 1024;
    static string path;
    static readonly ErrorLogThrottle throttle = new ErrorLogThrottle();
    static readonly object gate = new object();
    static bool headerWritten;
    static string header;       // sestaveno v Init() na hlavním vlákně – Unity API nejde volat z vlákna, které chybu zalogovalo
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    public static string Dir => path != null ? Path.GetDirectoryName(path) : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        if (Application.isEditor) return;
        try
        {
            path = Path.Combine(Path.GetDirectoryName(VisualizerSettings.FilePath), "errors.log");
            header = "==== DMX Visualiser " + Application.version + "  " + SystemInfo.operatingSystem + "  " + SystemInfo.graphicsDeviceName + " ====";
            Application.logMessageReceivedThreaded += OnLog;
        }
        catch (Exception e) { Debug.LogWarning("ErrorLog init failed: " + e.Message); }
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
        try
        {
            lock (gate)
            {
                if (!throttle.ShouldWrite(message, clock.Elapsed.TotalSeconds, out int suppressed)) return;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = path + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                    headerWritten = false;
                }
                using (var w = new StreamWriter(path, true))
                {
                    if (!headerWritten)
                    {
                        w.WriteLine(header);
                        headerWritten = true;
                    }
                    w.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + type + "]  " + message + (suppressed > 0 ? "   (+" + suppressed + "× opakováno)" : ""));
                    if (!string.IsNullOrEmpty(stack)) w.WriteLine(stack.TrimEnd());
                }
            }
        }
        catch { }   // log nesmí způsobit další chybu
    }
}
