using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RouteRunner.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RouteRunner
{
    public sealed partial class Plugin
    {
        GUISkin panelSkin;
        GUIStyle wrap, heading, section, hint, statusStyle, routeButton, selectedRouteButton, singleLine;
        PanelView panelView;
        const float PanelWidth = 780, PanelHeight = 600;
        readonly List<Texture2D> panelTextures = new List<Texture2D>();
        ConfigEntry<Key> rebindTarget;
        string rebindLabel;
        int rebindFrame, panelTab;
        bool centerPanel = true, settingsDirty, editingText;
        float lastPanelScale;
        int lastWidth, lastHeight;
        static readonly string[] PracticeTabs = { "Routes", "Sharing", "Playback", "Edit" };
        static readonly string[] FallbackTabs = { "Routes", "Sharing", "Playback", "Edit", "Settings" };
        string idleHud;
        Key hudMenuKey, hudRecordKey;
        string activityHud;
        float nextHudUpdate;

        void UpdatePanelVisibility()
        {
            if (panelView == null) return;
            var mode = OverlayState.Mode(!shuttingDown && library != null && modEnabled.Value && GameBridge.Exploring && !GameBridge.MenuOpen,
                panel, recording != null || ghost != null, showIdleHint.Value, Time.unscaledTime, statusUntil, showHud.Value);
            bool layout = mode == OverlayMode.Panel, visible = mode != OverlayMode.Hidden;
            if (panelView.useGUILayout != layout) panelView.useGUILayout = layout;
            if (panelView.enabled != visible) panelView.enabled = visible;
        }

        void InitPanel()
        {
            if (panelSkin != null) return;
            panelSkin = Instantiate(GUI.skin);
            var background = Solid(new Color(0.035f, 0.047f, 0.065f, 1));
            var surface = Solid(new Color(0.095f, 0.12f, 0.16f, 1));
            var hover = Solid(new Color(0.16f, 0.22f, 0.29f, 1));
            var active = Solid(new Color(0.055f, 0.32f, 0.37f, 1));
            var cyan = new Color(0.35f, 0.95f, 1f, 1);
            Style(panelSkin.label, null, null, null, 14);
            Style(panelSkin.button, surface, hover, active, 14);
            panelSkin.button.onNormal.background = active;
            panelSkin.button.padding = new RectOffset(8, 8, 4, 4);
            panelSkin.button.fixedHeight = 28;
            Style(panelSkin.textField, surface, hover, active, 14);
            panelSkin.textField.padding = new RectOffset(8, 8, 4, 4);
            panelSkin.textField.fixedHeight = 28;
            // Keep the skin's checkbox graphics, with readable labels.
            panelSkin.toggle.fontSize = 14; panelSkin.toggle.richText = false;
            panelSkin.toggle.normal.textColor = Color.white; panelSkin.toggle.onNormal.textColor = cyan;
            panelSkin.toggle.hover.textColor = Color.white; panelSkin.toggle.onHover.textColor = cyan;
            Style(panelSkin.box, surface, surface, surface, 14);
            Style(panelSkin.window, background, background, background, 16);
            panelSkin.window.padding = new RectOffset(14, 14, 38, 12);
            panelSkin.window.border = new RectOffset();
            panelSkin.window.normal.textColor = cyan; panelSkin.window.onNormal.textColor = cyan;
            panelSkin.window.alignment = TextAnchor.UpperLeft;
            panelSkin.window.contentOffset = Vector2.zero;
            panelSkin.horizontalSlider.fixedHeight = 24;
            panelSkin.horizontalSliderThumb.fixedWidth = 18;
            panelSkin.horizontalSliderThumb.fixedHeight = 24;
            panelSkin.horizontalSliderThumb.normal.background = active;
            panelSkin.horizontalSliderThumb.hover.background = hover;
            panelSkin.horizontalSliderThumb.active.background = active;
            wrap = new GUIStyle(panelSkin.label) { wordWrap = true, richText = false };
            heading = new GUIStyle(wrap) { fontSize = 15, fontStyle = FontStyle.Bold };
            section = new GUIStyle(heading); section.normal.textColor = cyan;
            hint = new GUIStyle(wrap) { fontSize = 12 }; hint.normal.textColor = new Color(0.73f, 0.8f, 0.87f);
            statusStyle = new GUIStyle(hint) { padding = new RectOffset(8, 8, 4, 4) };
            statusStyle.normal.background = surface;
            routeButton = new GUIStyle(panelSkin.button) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            selectedRouteButton = new GUIStyle(routeButton);
            selectedRouteButton.normal.background = active;
            singleLine = new GUIStyle(panelSkin.label) { wordWrap = false, clipping = TextClipping.Clip };
        }

        Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply(); panelTextures.Add(texture); return texture;
        }
        static void Style(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active, int fontSize)
        {
            style.fontSize = fontSize; style.richText = false;
            foreach (var state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
                state.textColor = Color.white;
            if (normal == null) return;
            style.normal.background = style.onNormal.background = normal;
            style.hover.background = style.onHover.background = hover;
            style.active.background = style.onActive.background = active;
            style.focused.background = style.onFocused.background = active;
        }

        internal void RenderPanel()
        {
            if (shuttingDown || library == null || !modEnabled.Value || (!panel && (!showHud.Value || Event.current.type != EventType.Repaint || !GameBridge.Exploring))) return;
            InitPanel();
            float scale = Mathf.Min(Mathf.Max(.85f, Screen.height / 1080f) * interfaceScale.Value,
                Mathf.Min((Screen.width - 32f) / PanelWidth, (Screen.height - 48f) / PanelHeight));
            scale = Mathf.Max(0.25f, scale);
            if (centerPanel || Screen.width != lastWidth || Screen.height != lastHeight || !Mathf.Approximately(scale, lastPanelScale))
            {
                window = new Rect((Screen.width / scale - PanelWidth) / 2, (Screen.height / scale - PanelHeight) / 2, PanelWidth, PanelHeight);
                centerPanel = false; lastWidth = Screen.width; lastHeight = Screen.height; lastPanelScale = scale;
            }
            var matrix = GUI.matrix; var oldSkin = GUI.skin; var oldColor = GUI.color; var oldBackground = GUI.backgroundColor; var oldContent = GUI.contentColor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.skin = panelSkin; GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            try
            {
                if (GameBridge.Exploring && !panel)
                {
                    if (idleHud == null || hudMenuKey != menuKey.Value || hudRecordKey != recordKey.Value)
                    {
                        hudMenuKey = menuKey.Value; hudRecordKey = recordKey.Value;
                        idleHud = "Route Runner  |  " + hudMenuKey + " library  |  " + hudRecordKey + " record";
                    }
                    // Refresh timer text at 10 Hz; pose recording/playback still use their full rates.
                    if (activityHud == null || Time.unscaledTime >= nextHudUpdate)
                    {
                        activityHud = recording != null ? "REC  " + recordTime.ToString("0.0") + "s  |  " + recordKey.Value + " save" :
                            ghost != null ? (countdown > 0 ? "START IN " + Mathf.CeilToInt(countdown) : (playing ? "GHOST  " : "PAUSED / FINISHED  ") + playbackTime.ToString("0.0") + " / " + selected.Duration.ToString("0.0") + "s") : idleHud;
                        nextHudUpdate = Time.unscaledTime + .1f;
                    }
                    float left = (Screen.width / scale - 440) / 2;
                    bool active = recording != null || ghost != null || showIdleHint.Value;
                    if (active) GUI.Box(new Rect(left, 12, 440, 28), activityHud);
                    if (Time.unscaledTime < statusUntil) GUI.Label(new Rect(left, active ? 44 : 12, 440, 48), status, statusStyle);
                }
                if (panel)
                {
                    window = GUI.Window(0x524747, window, DrawWindow, GUIContent.none);
                    window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width / scale - window.width));
                    window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height / scale - window.height));
                }
            }
            finally { GUI.matrix = matrix; GUI.skin = oldSkin; GUI.color = oldColor; GUI.backgroundColor = oldBackground; GUI.contentColor = oldContent; }
        }

        void DrawWindow(int id)
        {
            // Window title padding is skin-dependent; reserve an explicit title row above the body.
            GUI.Label(new Rect(14, 8, PanelWidth - 110, 24), "Route Runner", heading);
            bool close = GUI.Button(new Rect(PanelWidth - 78, 6, 66, 26), "Close");
            GUILayout.BeginArea(new Rect(14, 40, PanelWidth - 28, PanelHeight - 96));
            GUILayout.Label(GameBridge.Ready ? GameBridge.Map + "  /  Exploration" : "Exploration required", hint, GUILayout.Height(20));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Take name", GUILayout.Width(85));
            bool nameEnabled = GUI.enabled; GUI.enabled = nameEnabled && rebindTarget == null;
            GUI.SetNextControlName("routerunner-text-name"); title = GUILayout.TextField(title, 80);
            GUI.enabled = nameEnabled;
            GUILayout.EndHorizontal();
            bool canUse = GameBridge.Ready && !busy && editingRoute == null && pendingDeletion == null;
            GUILayout.BeginHorizontal();
            Button("Record  [" + recordKey.Value + "]", canUse, ToggleRecord);
            Button("Play  [" + playKey.Value + "]", canUse && selected != null && selected.Map == GameBridge.Map, StartPlayback);
            Button("Stop  [" + stopKey.Value + "]", !busy, () => { StopRecording("Route saved"); StopGhost(); });
            Button("To start  [" + resetKey.Value + "]", canUse && selected != null && selected.Map == GameBridge.Map, ReturnToStart);
            GUILayout.EndHorizontal();
            GUILayout.Label(selected == null ? "No route selected" : selected.Name + "  ·  " + selected.Map + "  ·  " + selected.Duration.ToString("0.00") + "s", singleLine, GUILayout.Height(24));
            bool enabledBefore = GUI.enabled; GUI.enabled = rebindTarget == null && pendingDeletion == null && !busy;
            int tab = GUILayout.Toolbar(panelTab, modMenuReady ? PracticeTabs : FallbackTabs);
            GUI.enabled = enabledBefore;
            if (tab != panelTab) { CancelTrim(); panelTab = tab; bodyScroll = Vector2.zero; GUI.FocusControl(null); }
            GUILayout.Space(8);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll, GUILayout.Height(340));
            if (panelTab == 0) DrawLibrary();
            else if (panelTab == 1) DrawSharing();
            else if (panelTab == 2) DrawPlayback();
            else if (panelTab == 3) DrawEditor();
            else DrawSettings();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            string footer = busy ? "Working on route files…" : Time.unscaledTime < statusUntil ? status :
                modMenuReady ? "Settings → Mods → Route Runner   |   " + menuKey.Value + " closes this panel" : menuKey.Value + " closes this panel";
            GUI.Label(new Rect(14, PanelHeight - 46, PanelWidth - 28, 36), footer, statusStyle);
            editingText = GUI.GetNameOfFocusedControl().StartsWith("routerunner-text-", StringComparison.Ordinal);
            if (rebindTarget == null) GUI.DragWindow(new Rect(0, 0, PanelWidth - 90, 34));
            if (close) { GUI.FocusControl(null); SetPanel(false); }
        }

        void DrawLibrary()
        {
            if (pendingDeletion != null)
            {
                GUILayout.Label("Delete " + pendingDeletion.Length + " routes?", section);
                GUILayout.Label("These will be removed from the library and moved to Deleted. Imports and exports are kept. Clear all includes every map, regardless of filters.", wrap);
                GUILayout.Space(12);
                GUILayout.BeginHorizontal();
                Button("Confirm delete (" + pendingDeletion.Length + ")", !busy && pendingDeletion.Length > 0, ConfirmDeletion);
                Button("Cancel", !busy, () => pendingDeletion = null);
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.BeginHorizontal();
            allMaps = GUILayout.Toggle(allMaps, "All maps", GUILayout.Width(115));
            GUILayout.Label("Search", GUILayout.Width(60));
            GUI.SetNextControlName("routerunner-text-search"); search = GUILayout.TextField(search, 100);
            Button("Refresh", !busy, RefreshLibrary, GUILayout.Width(95));
            GUILayout.EndHorizontal();
            var rows = VisibleRoutes;
            var viewport = GUILayoutUtility.GetRect(0, 190, GUILayout.ExpandWidth(true));
            float contentWidth = Mathf.Max(1, viewport.width - 20);
            const float rowHeight = 30;
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, contentWidth, Mathf.Max(viewport.height, rows.Count * rowHeight)), false, true);
            // Draw only visible rows, with one extra at each edge. Selection still spans all 200 matches.
            int first = Mathf.Clamp(Mathf.FloorToInt(scroll.y / rowHeight) - 1, 0, rows.Count);
            int last = Mathf.Min(rows.Count, first + Mathf.CeilToInt(viewport.height / rowHeight) + 3);
            for (int index = first; index < last; index++)
            {
                var row = rows[index];
                var entry = row.Entry;
                bool prior = GUI.enabled; GUI.enabled = !busy;
                float top = index * rowHeight;
                bool ticked = GUI.Toggle(new Rect(0, top + 5, 24, 24), checkedRoutes.Contains(entry.FilePath), "");
                if (ticked) checkedRoutes.Add(entry.FilePath); else checkedRoutes.Remove(entry.FilePath);
                if (GUI.Button(new Rect(28, top, Mathf.Max(1, contentWidth - 28), 28), row.Label, selectedPath == entry.FilePath ? selectedRouteButton : routeButton))
                { GUI.FocusControl(null); LoadRoute(entry); }
                GUI.enabled = prior;
            }
            if (rows.Count == 0) GUI.Label(new Rect(0, 0, contentWidth, 65), "No matching routes. Record a take or open Sharing to import one.", wrap);
            GUI.EndScrollView();
            if (routeView.HasMore) GUILayout.Label("Showing 200 matches. Narrow the search.", hint);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            Button("Select shown", !busy, () => { foreach (var row in VisibleRoutes) checkedRoutes.Add(row.Entry.FilePath); });
            Button("Unselect all", !busy && checkedRoutes.Count > 0, () => checkedRoutes.Clear());
            Button("Delete checked (" + checkedRoutes.Count + ")", !busy && checkedRoutes.Count > 0, () => RequestDeletion(false));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Button("Edit selected route", !busy && selected != null, BeginTrim);
            Button("Undo last delete", !busy && lastDeleted != null && lastDeleted.Items.Count > 0, UndoDeletion);
            Button("Clear all routes…", !busy, () => RequestDeletion(true));
            GUILayout.EndHorizontal();
            GUILayout.Label("Click to load · Tick to delete", hint);
        }

        void DrawSharing()
        {
            GUILayout.Label("Share a route", section);
            GUILayout.Label("Export the selected route and send the .sroute file.", hint);
            GUILayout.BeginHorizontal();
            Button("Export file", !busy && selected != null, ExportFile);
            Button("Copy route code", !busy && selected != null, CopyCode);
            Button("Open exports folder", true, () => OpenFolder(library.Exports));
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("Import a friend's route", section);
            GUILayout.Label("Put .sroute files in Imports, then import. Practise on the same map.", hint);
            GUILayout.BeginHorizontal();
            Button("Import files", !busy, ImportFiles);
            Button("Import clipboard code", !busy, ImportCode);
            Button("Open imports folder", true, () => OpenFolder(library.Imports));
            GUILayout.EndHorizontal();
            GUILayout.Label("Use files for long takes; codes may exceed chat limits.", hint);
            GUILayout.Space(12);
            Button("Open route library folder", true, () => OpenFolder(library.Root));
        }

        void DrawEditor()
        {
            GUILayout.Label("Trim the selected take", section);
            if (editingRoute == null)
            {
                GUILayout.Label("Trim the start or end. Saving keeps the original in Backups.", hint);
                Button("Edit selected route", !busy && selected != null, BeginTrim);
                return;
            }
            GUILayout.Label(editingRoute.Name + "  |  Original: " + editingRoute.Duration.ToString("0.00") + "s", wrap);
            GUILayout.Space(12);
            bool prior = GUI.enabled; GUI.enabled = !busy;
            int beforeFirst = trimFirst, beforeLast = trimLast;
            float start = trimFirst, end = trimLast;
            Slider("Start / left edge", ref start, 0, editingRoute.Frames.Count - 2, editingRoute.Frames[trimFirst].Time.ToString("0.00") + "s");
            trimFirst = Mathf.Clamp(Mathf.RoundToInt(start), 0, trimLast - 1);
            Slider("End / right edge", ref end, 1, editingRoute.Frames.Count - 1, editingRoute.Frames[trimLast].Time.ToString("0.00") + "s");
            trimLast = Mathf.Clamp(Mathf.RoundToInt(end), trimFirst + 1, editingRoute.Frames.Count - 1);
            GUI.enabled = prior;
            if (!busy && GameBridge.Ready && editingRoute.Map == GameBridge.Map && (beforeFirst != trimFirst || beforeLast != trimLast))
            {
                try { PreviewTrim(beforeFirst != trimFirst ? trimFirst : trimLast); } catch (Exception error) { Report(error); }
            }
            float duration = editingRoute.Frames[trimLast].Time - editingRoute.Frames[trimFirst].Time;
            GUILayout.Label("Keep " + duration.ToString("0.00") + "s  •  Remove " + (editingRoute.Duration - duration).ToString("0.00") + "s", heading);
            GUILayout.Label("Drag to preview · Closing or changing tabs cancels unsaved edits.", hint);
            GUILayout.BeginHorizontal();
            Button("Preview start", !busy && GameBridge.Ready && editingRoute.Map == GameBridge.Map, () => PreviewTrim(trimFirst));
            Button("Preview end", !busy && GameBridge.Ready && editingRoute.Map == GameBridge.Map, () => PreviewTrim(trimLast));
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            Button("Save trim", !busy && (trimFirst != 0 || trimLast != editingRoute.Frames.Count - 1), SaveTrim);
            Button("Cancel edit", !busy, CancelTrim);
            GUILayout.EndHorizontal();
        }

        void DrawPlayback()
        {
            GUILayout.Label("Playback", section);
            GUILayout.Label("Close the panel to continue the ghost.", hint);
            GUILayout.Space(12);
            Slider("Playback speed", ref speed, 0.25f, 2, speed.ToString("0.00") + "×");
            speed = Mathf.Round(speed * 20) / 20;
            loop = GUILayout.Toggle(loop, "Loop with countdown");
            GUILayout.BeginHorizontal();
            Button(playing ? "Pause replay" : "Resume replay", ghost != null, () => playing = !playing);
            Button("Back to 1× speed", true, () => speed = 1);
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("Timeline", section);
            if (ghost != null)
            {
                GUILayout.Label(playbackTime.ToString("0.00") + "s / " + selected.Duration.ToString("0.00") + "s", wrap);
                float value = GUILayout.HorizontalSlider(playbackTime, 0, selected.Duration);
                if (Mathf.Abs(value - playbackTime) > 0.0001f)
                { playbackTime = value; countdown = 0; playing = false; ghost.Show(selected, value); }
                GUILayout.Label("Scrubbing pauses playback.", hint);
            }
            else GUILayout.Label("Start a ghost with Play to scrub its timeline.", wrap);
            GUILayout.Space(12);
            if (!modMenuReady)
            {
                float seconds = countdownSeconds.Value;
                Slider("Countdown", ref seconds, 0, 10, seconds.ToString("0.0") + "s");
                SetSetting(countdownSeconds, Mathf.Round(seconds * 2) / 2);
            }
        }

        void DrawSettings()
        {
            bool hud = GUILayout.Toggle(showHud.Value, "Show HUD");
            if (hud != showHud.Value) showHud.Value = hud;
            GUILayout.Label("Keyboard bindings", section);
            GUILayout.Label("Click a binding, then press a keyboard key. Escape cancels. Choose keys you do not use for movement or weapons.", hint);
            BindingRow("Open / close panel", menuKey);
            BindingRow("Record / finish recording", recordKey);
            BindingRow("Stop recording / ghost", stopKey);
            BindingRow("Start / restart playback", playKey);
            BindingRow("Return to route start", resetKey);
            if (rebindTarget != null)
            {
                if (GUILayout.Button("Cancel key change  [Escape]")) { rebindTarget = null; Notify("Key change cancelled."); }
                return;
            }
            GUILayout.Space(8);
            GUILayout.Label("Ghost appearance", section);
            GUILayout.BeginHorizontal();
            ColourPreset("Cyan", new Color(0.15f, 0.95f, 1));
            ColourPreset("Orange", new Color(1, 0.45f, 0.1f));
            ColourPreset("Pink", new Color(1, 0.25f, 0.6f));
            ColourPreset("Purple", new Color(0.65f, 0.35f, 1));
            ColourPreset("Green", new Color(0.25f, 1, 0.3f));
            ColourPreset("White", Color.white);
            GUILayout.EndHorizontal();
            ColourSlider("Red", ghostRed); ColourSlider("Green", ghostGreen); ColourSlider("Blue", ghostBlue);
            float transparency = 1 - opacity.Value;
            Slider("Transparency", ref transparency, 0, 1, Mathf.RoundToInt(transparency * 100) + "% transparent");
            SetSetting(opacity, 1 - Mathf.Round(transparency * 100) / 100);
            ghost?.SetColour(GhostColour);
            var previous = GUI.color; GUI.color = new Color(ghostRed.Value, ghostGreen.Value, ghostBlue.Value);
            GUI.DrawTexture(GUILayoutUtility.GetRect(80, 22), Texture2D.whiteTexture); GUI.color = previous;
            float size = interfaceScale.Value;
            Slider("Panel / text size", ref size, 0.8f, 1.5f, Mathf.RoundToInt(size * 100) + "%");
            SetSetting(interfaceScale, Mathf.Round(size * 20) / 20);
            bool idleHint = GUILayout.Toggle(showIdleHint.Value, "Show idle hotkey hint");
            if (idleHint != showIdleHint.Value) { showIdleHint.Value = idleHint; settingsDirty = true; }
            GUILayout.Label("0% transparency = solid · 100% = invisible", hint);
        }

        void ColourPreset(string label, Color colour)
        {
            Button(label, true, () => { SetSetting(ghostRed, colour.r); SetSetting(ghostGreen, colour.g); SetSetting(ghostBlue, colour.b); ghost?.SetColour(GhostColour); });
        }
        void ColourSlider(string label, ConfigEntry<float> channel)
        {
            float value = channel.Value;
            Slider(label, ref value, 0, 1, Mathf.RoundToInt(value * 255).ToString());
            SetSetting(channel, Mathf.Round(value * 255) / 255);
        }

        void BindingRow(string label, ConfigEntry<Key> entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(310));
            Button(rebindTarget == entry ? "Press a key…" : entry.Value.ToString() + "   •   Change", true, () =>
            {
                rebindTarget = entry; rebindLabel = label; rebindFrame = Time.frameCount; editingText = false;
                Notify("Choose a key for " + label + ". Escape cancels.");
            });
            GUILayout.EndHorizontal();
        }
        void CaptureBinding()
        {
            if (Keyboard.current == null || Time.frameCount <= rebindFrame) return;
            if (Down(Key.Escape)) { rebindTarget = null; Notify("Key change cancelled."); return; }
            foreach (var key in Keyboard.current.allKeys)
            {
                if (!key.wasPressedThisFrame || key.keyCode == Key.None) continue;
                foreach (var binding in new[] { menuKey, recordKey, stopKey, playKey, resetKey })
                {
                    if (binding != rebindTarget && binding.Value == key.keyCode)
                    { Notify(key.keyCode + " is already used by " + binding.Definition.Key + ". Choose another key, or Escape to cancel."); return; }
                }
                rebindTarget.Value = key.keyCode; settingsDirty = true; rebindTarget = null;
                SavePanelSettings(); Notify(rebindLabel + " is now " + key.keyCode + ".");
                return; // Consume the capture frame so the new binding cannot fire an action.
            }
        }
        void Slider(string label, ref float value, float min, float max, string display)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(display, GUILayout.Width(130));
            GUILayout.EndHorizontal();
        }
        void SetSetting(ConfigEntry<float> setting, float value)
        {
            if (Mathf.Approximately(setting.Value, value)) return;
            setting.Value = value; settingsDirty = true;
        }
        void SavePanelSettings()
        {
            if (!settingsDirty) return;
            try { Config.Save(); settingsDirty = false; }
            catch (Exception error) { Report(error); }
        }
        void Button(string text, bool active, Action action, params GUILayoutOption[] options)
        {
            bool prior = GUI.enabled; GUI.enabled = prior && active && rebindTarget == null;
            bool clicked = GUILayout.Button(text, options); GUI.enabled = prior;
            if (clicked) { GUI.FocusControl(null); editingText = false; try { action(); } catch (Exception e) { Report(e); } }
        }
        void DisposePanel()
        {
            if (panelView != null) { panelView.enabled = false; Destroy(panelView); panelView = null; }
            SavePanelSettings();
            if (panelSkin != null) Destroy(panelSkin);
            foreach (var texture in panelTextures) if (texture != null) Destroy(texture);
            panelTextures.Clear();
        }
    }
}
