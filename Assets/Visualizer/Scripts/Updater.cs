using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

// Aktualizace přes GitHub Releases.
// Vydání musí obsahovat .zip se složkou buildu (DMXVisualiser.exe + DMXVisualiser_Data …).
// Instalace: stáhne zip, spustí PowerShell skript, aplikace se ukončí, skript rozbalí
// soubory přes starou verzi a aplikaci znovu spustí. Nastavení (settings.json) je jinde, zůstane.
public class Updater : MonoBehaviour
{
    public const string Repo = "VeksCZ/DMXVisualiser";

    public enum State { Idle, Checking, UpToDate, Available, Downloading, Installing, Error }
    public State state = State.Idle;
    public string latestVersion;
    public string releaseUrl;
    public string error;
    public float progress;

    string assetUrl;
    long assetSize;

    [Serializable] class GhAsset { public string name; public string browser_download_url; public long size; }
    [Serializable] class GhRelease { public string tag_name; public string html_url; public GhAsset[] assets; }

    // Při startu na pozadí zjistí, jestli je novější verze (nic nestahuje). Offline se tiše přeskočí.
    public void StartupCheck()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        StartCoroutine(StartupRoutine());
#endif
    }

    IEnumerator StartupRoutine()
    {
        yield return new WaitForSecondsRealtime(10f);
        if (state == State.Idle) Check();
    }

    public void Check()
    {
        if (state == State.Checking || state == State.Downloading || state == State.Installing) return;
        StartCoroutine(CheckRoutine());
    }

    IEnumerator CheckRoutine()
    {
        state = State.Checking;
        error = null;
        using (var req = UnityWebRequest.Get("https://api.github.com/repos/" + Repo + "/releases/latest"))
        {
            req.SetRequestHeader("User-Agent", "DMXVisualiser");
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            req.timeout = 15;
            yield return req.SendWebRequest();

            if (req.responseCode == 404) { state = State.Error; error = Loc.T("noRelease"); yield break; }
            if (req.result != UnityWebRequest.Result.Success) { state = State.Error; error = req.error; yield break; }

            GhRelease rel = null;
            try { rel = JsonUtility.FromJson<GhRelease>(req.downloadHandler.text); }
            catch (Exception e) { state = State.Error; error = e.Message; yield break; }
            if (rel == null || string.IsNullOrEmpty(rel.tag_name)) { state = State.Error; error = Loc.T("noRelease"); yield break; }

            latestVersion = rel.tag_name.TrimStart('v', 'V');
            releaseUrl = rel.html_url;
            assetUrl = null;
            assetSize = 0;
            if (rel.assets != null)
                foreach (var a in rel.assets)
                    if (a.name != null && a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { assetUrl = a.browser_download_url; assetSize = a.size; break; }

            state = IsNewer(latestVersion, Application.version) ? State.Available : State.UpToDate;
        }
    }

    public static bool IsNewer(string remote, string local)
    {
        if (Version.TryParse(Normalize(remote), out var r) && Version.TryParse(Normalize(local), out var l)) return r > l;
        return !string.Equals(remote, local, StringComparison.OrdinalIgnoreCase);
    }

    static string Normalize(string v)
    {
        v = (v ?? "").Trim().TrimStart('v', 'V');
        int dash = v.IndexOf('-');
        if (dash >= 0) v = v.Substring(0, dash);
        if (!v.Contains(".")) v += ".0";
        return v;
    }

    public void OpenReleasePage()
    {
        Application.OpenURL(string.IsNullOrEmpty(releaseUrl) ? "https://github.com/" + Repo + "/releases" : releaseUrl);
    }

    public void DownloadAndInstall()
    {
        if (state != State.Available) return;
        if (string.IsNullOrEmpty(assetUrl)) { state = State.Error; error = Loc.T("noAsset"); return; }
        if (Application.isEditor) { state = State.Error; error = Loc.T("editorNoInstall"); return; }
        StartCoroutine(InstallRoutine());
    }

    IEnumerator InstallRoutine()
    {
        state = State.Downloading;
        progress = 0f;
        string tmp = Path.Combine(Path.GetTempPath(), "DMXVisualiserUpdate");
        try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); Directory.CreateDirectory(tmp); }
        catch (Exception e) { state = State.Error; error = e.Message; yield break; }
        string zip = Path.Combine(tmp, "update.zip");

        using (var req = UnityWebRequest.Get(assetUrl))
        {
            req.SetRequestHeader("User-Agent", "DMXVisualiser");
            req.downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true };
            var op = req.SendWebRequest();
            while (!op.isDone) { progress = req.downloadProgress; yield return null; }
            if (req.result != UnityWebRequest.Result.Success) { state = State.Error; error = req.error; yield break; }
        }

        // Ověření před ukončením aplikace: úplná velikost a čitelný zip s .exe uvnitř
        if (!DownloadLooksValid(zip, assetSize)) { state = State.Error; error = Loc.T("updateCorrupt"); yield break; }

        state = State.Installing;
        string appDir = Path.GetDirectoryName(Application.dataPath);           // složka s .exe
        string exeName = Path.GetFileName(appDir) + ".exe";                      // fallback
        foreach (var f in Directory.GetFiles(appDir, "*.exe"))
            if (!Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase)) { exeName = Path.GetFileName(f); break; }
        int pid = Process.GetCurrentProcess().Id;

        string script = Path.Combine(tmp, "update.ps1");
        // Celé v try/catch: při chybě se důvod zapíše do update.log a aplikace se stejně spustí znovu (stará verze zůstane).
        string ps =
            "$ErrorActionPreference = 'Stop'\n" +
            "$tmp = '" + Esc(tmp) + "'\n" +
            "$app = '" + Esc(appDir) + "'\n" +
            "$log = Join-Path $tmp '" + LogName + "'\n" +
            "try {\n" +
            "  try { Wait-Process -Id " + pid + " -Timeout 30 -ErrorAction SilentlyContinue } catch {}\n" +
            "  $out = Join-Path $tmp 'unpacked'\n" +
            "  Expand-Archive -LiteralPath (Join-Path $tmp 'update.zip') -DestinationPath $out -Force\n" +
            "  $exe = Get-ChildItem -Path $out -Recurse -Filter '" + Esc(exeName) + "' | Select-Object -First 1\n" +
            "  if (-not $exe) { $exe = Get-ChildItem -Path $out -Recurse -Filter '*.exe' | Where-Object { $_.Name -notlike 'UnityCrashHandler*' } | Select-Object -First 1 }\n" +
            "  if (-not $exe) { throw 'V archivu chybí .exe' }\n" +
            "  robocopy $exe.DirectoryName $app /E /R:3 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null\n" +
            "  if ($LASTEXITCODE -ge 8) { throw ('robocopy selhal, kód ' + $LASTEXITCODE) }\n" +
            "} catch {\n" +
            "  ($_ | Out-String) | Set-Content -Path $log -Encoding UTF8\n" +
            "}\n" +
            "Start-Process -FilePath (Join-Path $app '" + Esc(exeName) + "')\n";
        try
        {
            File.WriteAllText(script, ps);
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception e) { state = State.Error; error = e.Message; yield break; }

        yield return new WaitForSecondsRealtime(0.5f);
        Application.Quit();
    }

    static string Esc(string s) => s.Replace("'", "''");

    const string LogName = "update.log";
    static string TempDir => Path.Combine(Path.GetTempPath(), "DMXVisualiserUpdate");

    // Zip musí mít očekávanou velikost (podle GitHubu), jít otevřít a obsahovat aplikaci (.exe).
    public static bool DownloadLooksValid(string zipPath, long expectedSize)
    {
        try
        {
            var fi = new FileInfo(zipPath);
            if (!fi.Exists || fi.Length == 0) return false;
            if (expectedSize > 0 && fi.Length != expectedSize) return false;
            using (var fs = File.OpenRead(zipPath))
            using (var za = new ZipArchive(fs, ZipArchiveMode.Read))
                foreach (var e in za.Entries)
                    if (e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !e.Name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch (Exception e) { Debug.LogWarning("Update zip check failed: " + e.Message); }
        return false;
    }

    // Po neúspěšné aktualizaci (skript zapsal log a spustil starou verzi) ukázat důvod v okně Aktualizace.
    void Start()
    {
        try
        {
            string log = Path.Combine(TempDir, LogName);
            if (!File.Exists(log)) return;
            string msg = File.ReadAllText(log).Trim();
            File.Delete(log);
            if (msg.Length == 0) return;
            if (msg.Length > 300) msg = msg.Substring(0, 300);
            Debug.LogWarning("Last update failed: " + msg);
            state = State.Error;
            error = Loc.T("updateFailed") + " " + msg;
        }
        catch { }
    }
}
