using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Nastavení settings.json – na Windows ve složce s .exe (portable), jinde v persistentDataPath.
[Serializable]
public class VisualizerSettings
{
    public int version = 9;
    public List<FixtureEntry> fixtures;
    public float hazeBuildRate = 0.08f;
    public float hazeDecay = 0.01f;
    public int cameraPreset = 0;
    public bool fullscreen = false;
    public int windowWidth = 1600;
    public int windowHeight = 900;
    public float dataHold = 3f;           // s: po výpadku dat světla drží poslední stav, pak se 2 s plynule ztlumí (0 = hned tma)
    public bool checkUpdatesOnStart = true;
    public float hazeDensity = -1f;       // haze z minulého spuštění (-1 = nenastaveno)
    public float sceneHaze = -1f;
    public bool hasWindowPos = false;     // poslední pozice okna (vůči monitoru displayName)
    public int windowX, windowY;
    public string displayName = "";
    public string language = "";      // "cs" / "en", prázdné = podle systému
    public bool showStatusBar = true;
    public bool vrPassthrough = false;     // Quest: místo virtuálního sálu skutečné okolí z kamer
    public bool vrScannedRoom = false;     // Quest: místo virtuálního sálu naskenovaná místnost (room setup)
    public bool panelOpen = true;
    public bool panelMinimized = false;
    public float roomLight = 0f;           // teplé světlo v sále (0–1)
    public List<FixtureProfile> customProfiles = new List<FixtureProfile>();   // vlastní profily použité v sestavě (přenáší se s nastavením)

    // Windows: portable – settings.json leží ve složce s .exe (přenáší se se složkou, aktualizace ho nepřepíše).
    // Když do složky nejde zapisovat (např. Program Files), zůstává v persistentDataPath.
    static string filePath;
    public static string FilePath
    {
        get
        {
            if (filePath != null) return filePath;
            string legacy = Path.Combine(Application.persistentDataPath, "settings.json");
            filePath = legacy;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                string appDir = Path.GetDirectoryName(Application.dataPath);
                string portable = Path.Combine(appDir, "settings.json");
                if (!File.Exists(portable))
                {
                    if (File.Exists(legacy)) File.Copy(legacy, portable);       // převzít dosavadní nastavení
                    else { File.WriteAllText(portable + ".tmp", ""); File.Delete(portable + ".tmp"); } // test zápisu
                }
                filePath = portable;
            }
            catch (Exception e) { Debug.LogWarning("Portable settings unavailable: " + e.Message); }
#endif
            return filePath;
        }
    }

    static VisualizerSettings ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<VisualizerSettings>(File.ReadAllText(path));
        }
        catch (Exception e) { Debug.LogWarning("Settings load failed (" + Path.GetFileName(path) + "): " + e.Message); return null; }
    }

    public static VisualizerSettings Load()
    {
        // Poškozený soubor (pád při zápisu, ruční úprava) → zkusí se záloha settings.json.bak
        VisualizerSettings s = ReadFile(FilePath) ?? ReadFile(FilePath + ".bak");
        if (s == null) s = new VisualizerSettings();
        bool empty = s.fixtures == null || s.fixtures.Count == 0;
        if (empty) s.fixtures = RigPresets.Mine();
        if (string.IsNullOrEmpty(s.language))
            s.language = Application.systemLanguage == SystemLanguage.Czech || Application.systemLanguage == SystemLanguage.Slovak ? "cs" : "en";
        if (s.windowWidth < 640 || s.windowHeight < 360) { s.windowWidth = 1600; s.windowHeight = 900; }
        if (s.version < 4) s.panelOpen = true;
        if (!empty && s.version < 9) MigrateLegacy(s);
        foreach (var f in s.fixtures) f.Migrate();   // typ jako číslo → id profilu
        if (s.customProfiles == null) s.customProfiles = new List<FixtureProfile>();
        ProfileLibrary.Reload(s.customProfiles);
        s.version = 9;
        return s;
    }

    // Převody nastavení ze starších verzí (patch se tehdy ukládal jako typ světla)
    static void MigrateLegacy(VisualizerSettings s)
    {
        bool Old(FixtureEntry f) => string.IsNullOrEmpty(f.profile);
        if (s.version < 5 && !s.fixtures.Exists(f => Old(f) && f.type == FixtureType.PocketPro))
        {
            // v0.5.1: přibyly ADJ Pocket Pro – doplnit do uloženého patche za GigBar
            int at = s.fixtures.FindIndex(f => Old(f) && f.type == FixtureType.GigBarMoveILS) + 1;
            s.fixtures.Insert(at, new FixtureEntry { type = FixtureType.PocketPro, name = "Pocket Pro R", address = 46 });
            s.fixtures.Insert(at, new FixtureEntry { type = FixtureType.PocketPro, name = "Pocket Pro L", address = 33 });
        }
        if (s.version < 6)
        {
            // v0.5.1: hlavy GigBaru mají „dopředu“ kolem 1/3 rozsahu panu
            foreach (var f in s.fixtures) if (Old(f) && f.type == FixtureType.GigBarMoveILS && f.panOffset == 0f) f.panOffset = 90f;
        }
        if (s.version < 7)
        {
            // v0.5.1: kalibrace na SS „Stage Center“ = střed parketu (jen pokud ji uživatel neměnil)
            var defs = RigPresets.Mine();
            int pi = 0;
            foreach (var f in s.fixtures)
            {
                if (!Old(f)) continue;
                FixtureEntry def = null;
                if (f.type == FixtureType.GigBarMoveILS && f.panOffset == 90f) def = defs[0];
                else if (f.type == FixtureType.PocketPro) { if (pi < 2 && f.panOffset == 0f) def = defs[1 + pi]; pi++; }
                if (def == null || f.tiltOffset != 0f || f.invertPan || f.invertTilt) continue;
                f.panOffset = def.panOffset; f.tiltOffset = def.tiltOffset;
            }
        }
        if (s.version < 8 && !s.fixtures.Exists(f => Old(f) && f.type >= FixtureType.EventTable))
        {
            // v0.5.5: stůl / booth a repro jako položky seznamu – podle toho, čí sestava je uložená
            bool colleague = s.fixtures.Exists(f => Old(f) && (f.type == FixtureType.DerbyStrobe || f.type == FixtureType.BlackPar || f.type == FixtureType.DoubleHelix));
            s.fixtures.AddRange(colleague
                ? new List<FixtureEntry> { new FixtureEntry("vonyx-db3-pro", "DJ booth", 1), new FixtureEntry("fbt-promaxx-14a", "Repro L", 1), new FixtureEntry("fbt-promaxx-14a", "Repro R", 1) }
                : new List<FixtureEntry> { new FixtureEntry("adj-pro-event-table-2", "DJ stůl", 1), new FixtureEntry("fbt-promaxx-12a", "Repro L", 1), new FixtureEntry("fbt-promaxx-12a", "Repro R", 1) });
        }
    }

    // Do nastavení se ukládají profily, které nejsou vestavěné (nebo jsou upravené) – aby šly se sestavou do Questu a ke kolegovi
    public void EmbedProfiles() => customProfiles = UsedCustomProfiles(fixtures);

    public static List<FixtureProfile> UsedCustomProfiles(List<FixtureEntry> list)
    {
        var res = new List<FixtureProfile>();
        var seen = new HashSet<string>();
        if (list == null) return res;
        foreach (var f in list)
        {
            var p = f != null ? f.Profile : null;
            if (p == null || p.source == "builtin" || !seen.Add(p.id)) continue;
            res.Add(p);
        }
        return res;
    }

    public void Save()
    {
        EmbedProfiles();
        try
        {
            // Atomicky: zapsat vedle a vyměnit, předchozí verze zůstane jako .bak
            string path = FilePath, tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }
        catch (Exception e) { Debug.LogWarning("Settings save failed: " + e.Message); }
    }
}
