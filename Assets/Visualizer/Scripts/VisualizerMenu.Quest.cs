using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Okno Nastavení → Quest (ADB, instalace aplikace do brýlí). Součást VisualizerMenu (VisualizerMenu.cs).
public partial class VisualizerMenu
{
    // ---- Quest (ADB) ----
    void DrawQuest()
    {
        if (!questDetected) { questDetected = true; quest.Detect(); }
        bool busy = quest.busy;
        bool ready = quest.adbPath != null && quest.device != null && quest.deviceState == "device";

        GUILayout.Label(Loc.T("qTitle"), sHead);
        GUILayout.Label(Loc.T("qHint"), sDim);
        GUILayout.Space(8 * k);

        GUILayout.Label("ADB: " + (quest.adbPath ?? Loc.T("qAdbMissing")), sLabel);
        GUILayout.Label(Loc.T("qDevice") + ": " + (quest.device == null ? "-" : quest.deviceModel + " (" + quest.deviceState + ")"), sLabel);
        GUILayout.Label(Loc.T("qAppVersion") + ": " + (quest.installedVersion ?? "-") + "    " + Loc.F("qPcVersion", Application.version), sLabel);
        GUILayout.Space(8 * k);

        string st = quest.status + (quest.progress >= 0f ? "  " + Mathf.RoundToInt(quest.progress * 100) + " %" : "");
        GUILayout.Label(st, quest.statusError ? sWarn : sHead);
        GUILayout.Space(10 * k);

        GUI.enabled = !busy;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(Loc.T("qDetect"), sButton, GUILayout.Width(200 * k))) quest.Detect();
        if (quest.adbPath == null && GUILayout.Button(Loc.T("qGetAdb"), sButton, GUILayout.Width(260 * k))) quest.DownloadAdb();
        GUILayout.EndHorizontal();
        GUILayout.Space(6 * k);
        GUI.enabled = !busy && ready;
        string inst = quest.installedVersion == null ? Loc.T("qInstall")
            : (quest.installedVersion == Application.version ? Loc.T("qReinstall") : Loc.F("qUpdate", Application.version));
        if (GUILayout.Button(inst, sBig)) quest.InstallApp();
        GUI.enabled = !busy && ready && quest.installedVersion != null;
        if (GUILayout.Button(Loc.T("qPush"), sBig)) { s.EmbedProfiles(); quest.PushSettings(s); }
        GUI.enabled = true;
        GUILayout.Label(Loc.T("qPushHint"), sDim);
    }

    // ---- Aktualizace ----
    void DrawUpdates()
    {
        GUILayout.Label(Loc.F("currentVersion", Application.version), sLabel);
        GUILayout.Space(8 * k);
        switch (updater.state)
        {
            case Updater.State.Checking: GUILayout.Label(Loc.T("checking"), sHead); break;
            case Updater.State.UpToDate: GUILayout.Label(Loc.T("upToDate"), sHead); break;
            case Updater.State.Available:
                GUILayout.Label(Loc.F("newVersion", updater.latestVersion), sHead);
                GUILayout.Space(6 * k);
                if (GUILayout.Button(Loc.T("install"), sBig)) updater.DownloadAndInstall();
                if (GUILayout.Button(Loc.T("openWeb"), sButton)) updater.OpenReleasePage();
                break;
            case Updater.State.Downloading: GUILayout.Label(Loc.F("downloading", Mathf.RoundToInt(updater.progress * 100)), sHead); break;
            case Updater.State.Installing: GUILayout.Label(Loc.T("installing"), sHead); break;
            case Updater.State.Error:
                GUILayout.Label(Loc.F("updateError", updater.error), sWarn);
                if (GUILayout.Button(Loc.T("openWeb"), sButton)) updater.OpenReleasePage();
                break;
        }
        GUILayout.Space(8 * k);
        if (updater.state != Updater.State.Checking && updater.state != Updater.State.Downloading && updater.state != Updater.State.Installing)
            if (GUILayout.Button(Loc.T("checkNow"), sButton)) updater.Check();
    }
}
