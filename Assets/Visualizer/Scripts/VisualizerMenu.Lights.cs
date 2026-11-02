using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Okno Nastavení → Světla: seznam, detail světla, umístění, kalibrace hlav. Součást VisualizerMenu (VisualizerMenu.cs).
public partial class VisualizerMenu
{
    // Světla: vlevo seznam se zatržítky (zobrazit ve scéně), vpravo nastavení vybraného světla
    void DrawLightsEditor()
    {
        GUILayout.Label(Loc.T("patchHint"), sDim);
        GUILayout.Space(6 * k);
        GUILayout.BeginHorizontal();

        // ---- seznam ----
        GUILayout.BeginVertical(GUILayout.Width(280 * k));
        lightsScroll = GUILayout.BeginScrollView(lightsScroll, GUI.skin.box, GUILayout.Height(Mathf.Min(Screen.height * 0.4f, 250 * k)));
        for (int i = 0; i < stFixtures.Count; i++)
        {
            GUILayout.BeginHorizontal();
            bool was = eOn[i];
            eOn[i] = GUILayout.Toggle(eOn[i], GUIContent.none, sToggle, GUILayout.Width(20 * k));
            if (eOn[i] != was) PreviewVisibility();
            string label = eName[i] + (Overlap(i) != null || stFixtures[i].Profile == null ? "   ⚠" : "");
            bool sel = selFixture == i;
            if (GUILayout.Toggle(sel, label, sListItem) && !sel)
            {
                selFixture = i;
                popup = 0;
                GUIUtility.keyboardControl = 0;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(Loc.T("addFixture"), sButton)) popup = popup == 2 ? 0 : 2;
        if (Event.current.type == EventType.Repaint && popup == 2) popupAnchor = ToScreen(GUILayoutUtility.GetLastRect());
        GUI.enabled = stFixtures.Count > 0;
        if (GUILayout.Button(Loc.T("removeFixture"), sButton) && selFixture < stFixtures.Count)
        {
            RemoveEditorRow(selFixture);
            selFixture = Mathf.Clamp(selFixture, 0, Mathf.Max(0, stFixtures.Count - 1));
            rigDirty = true;
            PreviewVisibility();
        }
        if (GUILayout.Button("↑", sButton, GUILayout.Width(28 * k))) MoveEditorRow(selFixture, -1);
        if (GUILayout.Button("↓", sButton, GUILayout.Width(28 * k))) MoveEditorRow(selFixture, 1);
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        // Načíst výchozí sestavu (přepíše seznam v dialogu, uloží se až OK / Použít)
        GUILayout.Space(4 * k);
        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.T("presetLoad"), sDim, GUILayout.Width(100 * k));
        if (GUILayout.Button(Loc.T("presetMine"), sButton)) LoadPreset(RigPresets.Mine());
        if (GUILayout.Button(Loc.T("presetColleague"), sButton)) LoadPreset(RigPresets.Colleague());
        GUILayout.EndHorizontal();

        // Sestava jako soubor – poslat kolegovi / přenést na jiný počítač
        GUILayout.Space(4 * k);
        GUILayout.Label(Loc.T("rigFile"), sDim);
        GUILayout.BeginHorizontal();
        rigName = GUILayout.TextField(rigName, sField);
        if (GUILayout.Button(Loc.T("rigExport"), sButton, GUILayout.Width(90 * k))) ExportRig();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(Loc.T("rigImport"), sButton)) { if (popup == 3) popup = 0; else OpenRigPicker(); }
        if (Event.current.type == EventType.Repaint && popup == 3) popupAnchor = ToScreen(GUILayoutUtility.GetLastRect());
        if (GUILayout.Button(Loc.T("rigFolder"), sButton)) OpenFolder(RigsDir);
        GUILayout.EndHorizontal();

        // Profily světel (JSON) – vlastní světla bez nového buildu
        GUILayout.Space(4 * k);
        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.T("profiles"), sDim, GUILayout.Width(100 * k));
        if (GUILayout.Button(Loc.T("profilesFolder"), sButton)) OpenFolder(ProfileLibrary.UserDir);
        if (GUILayout.Button(Loc.T("profilesReload"), sButton)) ReloadProfiles();
        GUILayout.EndHorizontal();
        if (ProfileLibrary.Errors.Count > 0) GUILayout.Label(Loc.F("profileErrors", string.Join("; ", ProfileLibrary.Errors)), sWarn);
        GUILayout.EndVertical();

        GUILayout.Space(14 * k);

        // ---- detail ----
        detailScroll = GUILayout.BeginScrollView(detailScroll);
        GUILayout.BeginVertical();
        if (stFixtures.Count > 0) DrawFixtureDetail(Mathf.Clamp(selFixture, 0, stFixtures.Count - 1));
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndHorizontal();
    }

    void DrawFixtureDetail(int i)
    {
        var f = stFixtures[i];
        var p = f.Profile;
        float lw = 120 * k;
        if (p == null)
        {
            GUILayout.Label(eName[i], sHead);
            GUILayout.Label(Loc.F("profileMissing", f.profile), sWarn);
            return;
        }
        GUILayout.Label(p.Label, sHead);
        string src = p.source == "builtin" ? Loc.T("srcBuiltin") : p.source == "embedded" ? Loc.T("srcEmbedded") : Path.GetFileName(p.source);
        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.F("profileSource", src), sDim);
        if (GUILayout.Button(Loc.T("profileEdit"), sButton, GUILayout.ExpandWidth(false)))
        {
            try
            {
                var copy = JsonUtility.FromJson<FixtureProfile>(JsonUtility.ToJson(p));
                string path = ProfileLibrary.SaveToLibrary(copy);
                Flash(Loc.F("profileSaved", path));
                OpenFolder(ProfileLibrary.UserDir);
            }
            catch (Exception e) { settingsError = e.Message; }
        }
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(p.notes)) GUILayout.Label(p.notes, sDim);

        bool wasOn = eOn[i];
        eOn[i] = GUILayout.Toggle(eOn[i], " " + Loc.T("showInScene"), sToggle);
        if (eOn[i] != wasOn) PreviewVisibility();
        GUILayout.Space(6 * k);

        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.T("name"), sLabel, GUILayout.Width(lw));
        eName[i] = GUILayout.TextField(eName[i], sField);
        GUILayout.EndHorizontal();

        if (p.IsProp) GUILayout.Label(Loc.T("propHint"), sDim);
        else
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("universe"), sLabel, GUILayout.Width(lw));
            eUni[i] = GUILayout.TextField(eUni[i], sField, GUILayout.Width(60 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("address"), sLabel, GUILayout.Width(lw));
            eAddr[i] = GUILayout.TextField(eAddr[i], sField, GUILayout.Width(80 * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // select režimu kanálů – rozbalená nabídka se kreslí navrch (DrawPopup)
            var modes = new string[Mathf.Max(1, p.ModeCount)];
            for (int m = 0; m < p.ModeCount; m++) modes[m] = p.modes[m].name ?? (p.modes[m].channels + "ch");
            int mi = Mathf.Clamp(eMode[i], 0, modes.Length - 1);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("channelMode"), sLabel, GUILayout.Width(lw));
            if (GUILayout.Button(modes[mi] + "   ▾", sSelect)) popup = popup == 1 ? 0 : 1;
            if (Event.current.type == EventType.Repaint && popup == 1) popupAnchor = ToScreen(GUILayoutUtility.GetLastRect());
            GUILayout.EndHorizontal();
            selectModes = modes;
            selectIndex = mi;
            selectFor = i;

            GUILayout.Space(6 * k);
            int ch = p.Channels(mi);
            if (int.TryParse(eAddr[i], out int a))
                GUILayout.Label(Loc.F("channelsInfo", ch, a, a + ch - 1), sDim);
            string ov = Overlap(i);
            if (ov != null) GUILayout.Label(Loc.F("overlap", ov), sWarn);
        }

        if (!p.IsHazer) DrawPlacement(i, lw);
        if (p.HasMovers) DrawCalibration(i, f, lw);
    }

    // Umístění ve scéně: automaticky podle profilu, nebo vlastní souřadnice
    void DrawPlacement(int i, float lw)
    {
        GUILayout.Space(10 * k);
        GUILayout.Label(Loc.T("placement"), sHead);
        GUILayout.BeginHorizontal();
        bool was = eCustom[i];
        if (GUILayout.Toggle(!eCustom[i], Loc.T("posAuto"), sTab)) eCustom[i] = false;
        if (GUILayout.Toggle(eCustom[i], Loc.T("customPos"), sTab)) eCustom[i] = true;
        GUILayout.EndHorizontal();
        if (eCustom[i] && !was && scene != null && i < scene.instances.Count && scene.instances[i].root != null)
        {
            // předvyplnit aktuálním místem ve scéně
            var t = scene.instances[i].root;
            var lp = t.localPosition; var le = t.localEulerAngles;
            ePos[i] = new[] { Num(Round(lp.x)), Num(Round(lp.y)), Num(Round(lp.z)), Num(Ang(le.x)), Num(Ang(le.y)), Num(Ang(le.z)) };
        }
        if (eCustom[i] != was) ApplyPlacementPreview(i);
        if (!eCustom[i]) return;

        bool changed = false;
        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.T("posXYZ"), sLabel, GUILayout.Width(lw + 40 * k));
        for (int c = 0; c < 3; c++) { string o = ePos[i][c]; ePos[i][c] = GUILayout.TextField(ePos[i][c], sField, GUILayout.Width(60 * k)); changed |= o != ePos[i][c]; }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.T("rotXYZ"), sLabel, GUILayout.Width(lw + 40 * k));
        for (int c = 3; c < 6; c++) { string o = ePos[i][c]; ePos[i][c] = GUILayout.TextField(ePos[i][c], sField, GUILayout.Width(60 * k)); changed |= o != ePos[i][c]; }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label(Loc.T("posHint"), sDim);
        if (changed) ApplyPlacementPreview(i);
    }

    static float Round(float v) => Mathf.Round(v * 100f) / 100f;
    static float Ang(float v) { v = Mathf.Repeat(v + 180f, 360f) - 180f; return Mathf.Round(v * 10f) / 10f; }

    // Náhled umístění hned při psaní (uloží se až OK / Použít)
    void ApplyPlacementPreview(int i)
    {
        var f = stFixtures[i];
        f.customPos = eCustom[i];
        var v = new float[6];
        for (int c = 0; c < 6; c++) if (!TryDeg(ePos[i][c], out v[c])) return;
        f.pos = new Vector3(v[0], v[1], v[2]);
        f.rot = new Vector3(v[3], v[4], v[5]);
        if (scene != null && i < scene.instances.Count && scene.instances[i].entry == f) scene.FinishVisibility();
        else { rigDirty = true; PreviewVisibility(); }
    }

    // Kalibrace moving headů + živý odečet pan/tilt ze SoundSwitche
    void DrawCalibration(int i, FixtureEntry f, float lw)
    {
        GUILayout.Space(10 * k);
        GUILayout.Label(Loc.T("calibration"), sHead);
        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.T("panOffset"), sLabel, GUILayout.Width(lw));
        eOffset[i] = GUILayout.TextField(eOffset[i], sField, GUILayout.Width(60 * k));
        GUILayout.Label("°", sLabel, GUILayout.Width(16 * k));
        GUILayout.Space(12 * k);
        GUILayout.Label(Loc.T("tiltOffset"), sLabel);
        eTiltOffset[i] = GUILayout.TextField(eTiltOffset[i], sField, GUILayout.Width(60 * k));
        GUILayout.Label("°", sLabel, GUILayout.Width(16 * k));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        eInvPan[i] = GUILayout.Toggle(eInvPan[i], " " + Loc.T("invertPan"), sToggle);
        eInvTilt[i] = GUILayout.Toggle(eInvTilt[i], " " + Loc.T("invertTilt"), sToggle);
        GUILayout.EndHorizontal();

        // živé hodnoty podle aktuálně zadané adresy (i před uložením)
        int.TryParse(eUni[i], out int u);
        int.TryParse(eAddr[i], out int a);
        var probe = new FixtureEntry(f.profile, f.name, a, eMode[i]) { universe = u, invertPan = eInvPan[i], invertTilt = eInvTilt[i] };
        DmxPatch.MoverRanges(f.Profile, out float panRange, out float tiltRange);
        if (patch != null && patch.ReadPanTilt(probe, out int pv, out int tv))
        {
            float pn = pv / 65535f, tn = tv / 65535f;
            if (eInvPan[i]) pn = 1f - pn;
            if (eInvTilt[i]) tn = 1f - tn;
            TryDeg(eOffset[i], out float po);
            TryDeg(eTiltOffset[i], out float to);
            float panDeg = (pn - 0.5f) * panRange + po;
            float tiltDeg = (tn - 0.5f) * tiltRange + to;
            GUILayout.Label(Loc.F("liveValues", pv, panDeg.ToString("+0;-0;0"), tv, tiltDeg.ToString("+0;-0;0")), sLabel);

            // Jedním klikem: aktuální pozice ze SS (např. Stage Center) = střed parketu
            if (GUILayout.Button(Loc.T("calibCenter"), sButton, GUILayout.ExpandWidth(false)) && scene != null)
            {
                if (rigDirty) PreviewVisibility();   // scéna musí odpovídat dialogu
                if (patch.SolveOffsets(i, probe, scene.danceFloorCenter, out float npo, out float nto))
                {
                    eOffset[i] = npo.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                    eTiltOffset[i] = nto.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        else GUILayout.Label(Loc.T("liveNone"), sDim);
        GUILayout.Label(Loc.T("calibrationHint"), sDim);
    }
}
