using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using RouteRunner.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[assembly: ComputerysModdingUtilities.StraftatMod(true)]

namespace RouteRunner
{
    [BepInPlugin("practice.straftat.routerunner", "Route Runner", "1.3.5")]
    [BepInDependency("kestrel.straftat.modmenu", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("practice.straftat.routeghost")]
    [DefaultExecutionOrder(10000)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        ConfigEntry<Key> menuKey, recordKey, playKey, stopKey, resetKey;
        ConfigEntry<int> sampleRate, maxSeconds;
        ConfigEntry<bool> showIdleHint, modEnabled, showHud;
        bool runtimeEnabled = true;
        ConfigEntry<float> opacity, countdownSeconds, interfaceScale, ghostRed, ghostGreen, ghostBlue;
        RouteLibrary library;
        List<RouteSummary> entries = new List<RouteSummary>();
        Route selected, recording;
        CaptureRig capture;
        GhostRig ghost;
        FirstPersonController playbackPlayer;
        float recordTime, nextSample, playbackTime, countdown;
        float statusUntil;
        float speed = 1;
        bool loop, playing, panel, allMaps, busy, startPending;
        bool modMenuReady;
        float modSettingsSaveAt = -1;
        int suppressHotkeysThroughFrame;
        string title = "Practice route", search = "", status = "Enter an exploration map, then press F6.", selectedPath = "";
        string activeMap;
        int sceneHandle;
        Vector2 scroll;
        Rect window;
        readonly ConcurrentQueue<Action> completed = new ConcurrentQueue<Action>();
        Task pendingIO;
        bool shuttingDown;
        InputActionMap blockedInput;
        readonly List<InputAction> suspendedActions = new List<InputAction>();
        FirstPersonController blockedPlayer;
        CursorLockMode oldCursor;
        bool oldCursorVisible;

        void Awake()
        {
            // Keep the persistent mod host out of normal scene-object enumeration.
            gameObject.hideFlags |= HideFlags.HideAndDontSave;
            useGUILayout = false;
            panelView = gameObject.AddComponent<PanelView>();
            panelView.Owner = this; panelView.useGUILayout = false; panelView.enabled = false;
            Config.SaveOnConfigSet = false;
            try
            {
                if (LegacyMigration.CopyConfig(Path.Combine(Paths.ConfigPath, "practice.straftat.routeghost.cfg"), Config.ConfigFilePath)) Config.Reload();
            }
            catch (Exception error) { Logger.LogWarning("Could not copy previous settings: " + error.Message); }
            modEnabled = Config.Bind("General", "Enable Mod", true, "Enable Route Runner. Turning off saves the current take, removes the ghost and closes the panel. Turn back on here without restarting.");
            showHud = Config.Bind("General", "Show HUD", true, "Show Route Runner banners, recording/playback status and notifications. Turn off to hide all HUD elements; the route panel still works.");
            modEnabled.SettingChanged += (_, __) => QueueModSettingsSave();
            showHud.SettingChanged += (_, __) => QueueModSettingsSave();
            menuKey = Config.Bind("Keys", "Menu", Key.F6, "Toggle route library and controls.");
            recordKey = Config.Bind("Keys", "Record", Key.F7, "Start/finish a route recording in exploration.");
            playKey = Config.Bind("Keys", "Play", Key.F8, "Start/restart selected ghost with countdown.");
            stopKey = Config.Bind("Keys", "Stop", Key.F9, "Save recording and remove ghost.");
            resetKey = Config.Bind("Keys", "ReturnToStart", Key.F10, "Return to selected route start in exploration.");
            sampleRate = Config.Bind("Recording", "SamplesPerSecond", 30, new ConfigDescription("Target pose sample rate; actual samples follow rendered frames.", new AcceptableValueRange<int>(10, 60)));
            maxSeconds = Config.Bind("Recording", "MaxSeconds", 180, new ConfigDescription("Auto-save at this length or the file-size limit.", new AcceptableValueRange<int>(10, 300)));
            opacity = Config.Bind("Ghost", "Opacity", 0.7f, new ConfigDescription("Ghost opacity from invisible (0) to solid (1).", new AcceptableValueRange<float>(0, 1)));
            ghostRed = Config.Bind("Ghost", "Red", 0.15f, new ConfigDescription("Ghost red channel.", new AcceptableValueRange<float>(0, 1)));
            ghostGreen = Config.Bind("Ghost", "Green", 0.95f, new ConfigDescription("Ghost green channel.", new AcceptableValueRange<float>(0, 1)));
            ghostBlue = Config.Bind("Ghost", "Blue", 1f, new ConfigDescription("Ghost blue channel.", new AcceptableValueRange<float>(0, 1)));
            countdownSeconds = Config.Bind("Ghost", "CountdownSeconds", 3f, new ConfigDescription("Lead-in before each replay or loop.", new AcceptableValueRange<float>(0, 10)));
            interfaceScale = Config.Bind("Interface", "Scale", 1f, new ConfigDescription("Panel/text scale. Automatically limited to fit the screen.", new AcceptableValueRange<float>(0.8f, 1.5f)));
            showIdleHint = Config.Bind("Interface", "ShowIdleHint", false, "Show a persistent hotkey hint when not recording or playing. Requires Show HUD.");
            var settingsRevision = Config.Bind("Interface", "SettingsRevision", 0, "Internal settings migration version.");
            if (settingsRevision.Value < 1)
            {
                if (Mathf.Approximately(opacity.Value, 0.4f)) opacity.Value = 0.7f;
                settingsRevision.Value = 1;
            }
            Config.Save();
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("kestrel.straftat.modmenu", out var modMenu) && modMenu.Metadata.Version >= new Version(1, 2, 0))
            {
                try { ConfigureModMenu(); modMenuReady = true; }
                catch (Exception error) { Logger.LogWarning("Mod Menu integration unavailable; using the panel Settings tab. " + error.Message); }
            }
            try
            {
                library = new RouteLibrary(Path.Combine(Paths.BepInExRootPath, "RouteRunner"));
                RunIO(() =>
                {
                    int migrated = LegacyMigration.CopyRoutes(Path.Combine(Paths.BepInExRootPath, "RouteGhost"), library, s => Logger.LogWarning(s));
                    var list = library.List(s => Logger.LogWarning(s));
                    return () => { entries = list; Notify(migrated > 0 ? "Copied " + migrated + " existing routes to Route Runner." : "Library ready: " + list.Count + " routes."); };
                });
            }
            catch (Exception e) { Report(e); enabled = false; }
            activeMap = GameBridge.Map; sceneHandle = SceneManager.GetActiveScene().handle;
            Logger.LogInfo("Route Runner 1.3.5 loaded. Open " + menuKey.Value + " for routes. " + (modMenuReady ? "Settings are in Settings > Mods > Route Runner." : "Settings are in the panel.") + " Exploration only.");
        }

        void Start() { Logger.LogInfo("Route Runner frame loop started."); }

        void Update()
        {
            if (shuttingDown) return;
            while (completed.TryDequeue(out var action))
            {
                try { action(); } catch (Exception error) { Report(error); }
            }
            if (modSettingsSaveAt >= 0 && Time.unscaledTime >= modSettingsSaveAt) { SavePanelSettings(); modSettingsSaveAt = -1; }
            if (library == null) return;
            try
            {
                ApplyEnabledState();
                // Continue pending input restoration and file completions while gameplay features are off.
                RestorePanelInput();
                if (!runtimeEnabled) return;
                var scene = SceneManager.GetActiveScene();
                if (scene.handle != sceneHandle || !string.Equals(activeMap, GameBridge.Map, StringComparison.Ordinal))
                {
                    StopRecording("Map changed; route saved"); StopGhost(); SetPanel(false);
                    GameBridge.ClearPlayerCache();
                    activeMap = GameBridge.Map; sceneHandle = scene.handle;
                }
                if ((recording != null || ghost != null || panel) && !GameBridge.Ready)
                {
                    if (recording != null) StopRecording("Exploration ended or player respawned; route saved");
                    if (ghost != null) StopGhost();
                    if (panel && !GameBridge.Exploring) SetPanel(false);
                }
                if (ghost != null && playbackPlayer != GameBridge.Player) StopGhost();
                // Do not open F6 or trigger actions while typing/rebinding in the game's settings menu.
                if (GameBridge.MenuOpen)
                {
                    if (recording != null) StopRecording("Menu opened; route saved");
                    if (panel) SetPanel(false);
                    return;
                }
                if (Time.frameCount <= suppressHotkeysThroughFrame) return;
                if (panel && rebindTarget != null) { CaptureBinding(); return; }
                if (Down(menuKey.Value) && GameBridge.Exploring && (!panel || !editingText || (menuKey.Value >= Key.F1 && menuKey.Value <= Key.F12))) SetPanel(!panel);
                if (panel && Down(Key.Escape)) SetPanel(false);
                // Letter bindings must not fire while typing route names or searches in the panel.
                if (panel || busy || !GameBridge.Exploring) return;
                if (Down(stopKey.Value)) { StopRecording("Route saved"); StopGhost(); }
                if (Down(recordKey.Value)) ToggleRecord();
                if (Down(playKey.Value)) StartPlayback();
                if (Down(resetKey.Value)) ReturnToStart();
            }
            catch (Exception e) { Recover(e); }
            finally { UpdatePanelVisibility(); }
        }

        void LateUpdate()
        {
            if (shuttingDown || !modEnabled.Value) return;
            if (panel) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (recording == null && (ghost == null || !playing)) return;
            try
            {
                if (!GameBridge.Ready) return;
                if (recording != null && (capture == null || capture.Player != GameBridge.Player)) { StopRecording("Player changed; route saved"); return; }
                if (panel || GameBridge.MenuOpen || Time.timeScale == 0) return;
                float dt = Time.deltaTime;
                if (recording != null)
                {
                    if (startPending)
                    {
                        recording.Frames.Add(capture.Capture(0)); startPending = false;
                        nextSample = 1f / recording.SampleRate;
                    }
                    else
                    {
                        recordTime += dt;
                        if (recordTime >= nextSample)
                        {
                            AddSample(recordTime);
                            // Do not fabricate repeated poses to catch up after a low-FPS frame.
                            nextSample = recordTime + 1f / recording.SampleRate;
                        }
                        long bytes = (long)(recording.Frames.Count + 2) * (6 + (7 + recording.Bones.Length * 10) * 4) + 300000;
                        if (recordTime >= maxSeconds.Value || recording.Frames.Count >= RouteCodec.MaxFrames - 1 || bytes >= RouteCodec.MaxRawBytes)
                            StopRecording("Recording limit reached; route saved");
                    }
                }
                if (ghost != null && playing)
                {
                    if (countdown > 0)
                    {
                        countdown -= dt;
                        // Only the portion after countdown belongs to playback.
                        dt = Mathf.Max(0, -countdown); countdown = Mathf.Max(0, countdown);
                        if (countdown > 0) return;
                    }
                    playbackTime += dt * speed;
                    if (playbackTime >= selected.Duration)
                    {
                        if (loop) { playbackTime = 0; countdown = countdownSeconds.Value; }
                        else { playbackTime = selected.Duration; playing = false; }
                    }
                    ghost.Show(selected, playbackTime);
                }
            }
            catch (Exception e) { Recover(e); }
        }

        void AddSample(float time)
        {
            if (recording.Frames.Count != 0 && time <= recording.Duration) return;
            var frame = capture.Capture(time);
            if (recording.Frames.Count > 0)
            {
                var prior = recording.Frames[recording.Frames.Count - 1];
                frame.Cut = Vector3.Distance(CaptureRig.V(frame.Pose, 0), CaptureRig.V(prior.Pose, 0)) > 8 || time - prior.Time > 0.5f;
            }
            recording.Frames.Add(frame);
        }

        void ToggleRecord()
        {
            if (recording != null) { StopRecording("Route saved"); return; }
            RequireReady();
            if (busy) return;
            StopGhost();
            capture = new CaptureRig(GameBridge.Player);
            Logger.LogInfo("Recording rig: " + capture.Bones.Length + " transforms; meshes: " + string.Join(", ", capture.Renderers.Select(r => r.name)));
            recording = new Route
            {
                Name = string.IsNullOrWhiteSpace(title) ? "Practice route" : title.Trim(),
                Map = GameBridge.Map,
                GameVersion = Application.version,
                SampleRate = sampleRate.Value,
                Bones = capture.Paths
            };
            int frameBytes = 6 + (7 + capture.Bones.Length * 10) * sizeof(float);
            recording.Frames.Capacity = Math.Min(sampleRate.Value * maxSeconds.Value + 2, Math.Min(RouteCodec.MaxFrames, (RouteCodec.MaxRawBytes - 300000) / frameBytes));
            recordTime = 0; nextSample = 0; startPending = true;
            SetPanel(false);
            Notify("Recording. Press " + recordKey.Value + " to save.");
        }

        void StopRecording(string message, bool refreshLibrary = true)
        {
            if (recording == null) return;
            var route = recording;
            // Only poses collected in LateUpdate belong to the take; never sample a newly loaded scene.
            recording = null; capture?.Dispose(); capture = null; startPending = false;
            if (route.Frames.Count < 2) { Notify("Take was too short; move for a moment before saving."); return; }
            selected = route; selectedPath = "";
            RunIO(() =>
            {
                string path = library.Save(route);
                var list = refreshLibrary ? library.List(s => Logger.LogWarning(s)) : null;
                return () => { selectedPath = path; if (list != null) entries = list; Notify(message + ": " + route.Name + " (" + route.Duration.ToString("0.00") + "s)."); };
            });
        }

        void StartPlayback()
        {
            RequireSelectedMap();
            if (recording != null || busy) throw new InvalidOperationException("Finish saving the recording first.");
            using (var rig = new CaptureRig(GameBridge.Player, evaluateAnimation: false))
            {
                if (ghost == null || playbackPlayer != GameBridge.Player || !ghost.CanReuse(rig, selected))
                { StopGhost(); ghost = new GhostRig(rig, selected, GhostColour); }
                else { ghost.SetColour(GhostColour); ghost.Show(selected, 0); }
            }
            playbackPlayer = GameBridge.Player;
            playbackTime = 0; countdown = countdownSeconds.Value; playing = true;
            SetPanel(false);
            Notify("Ghost ready. Follow after the countdown.");
        }
        void StopGhost() { ghost?.Dispose(); ghost = null; playbackPlayer = null; playing = false; playbackTime = 0; countdown = 0; }

        void ReturnToStart()
        {
            RequireSelectedMap();
            if (recording != null) throw new InvalidOperationException("Stop recording before returning to the start.");
            var p = GameBridge.Player; var cc = p.characterController;
            bool wasEnabled = cc != null && cc.enabled;
            try
            {
                if (cc != null) cc.enabled = false;
                var frame = selected.Frames[0];
                p.transform.SetPositionAndRotation(CaptureRig.V(frame.Pose, 0), CaptureRig.Q(frame.Pose, 3));
                p.moveDirection = Vector3.zero; p.moveAdded = Vector3.zero; p.currentInput = Vector2.zero; p.currentInputRaw = Vector2.zero;
            }
            finally { if (cc != null) cc.enabled = wasEnabled; }
            Notify("Returned to the route start. Press " + playKey.Value + " to follow the ghost.");
        }

        void SetPanel(bool value)
        {
            if (value == panel) return;
            if (value)
            {
                // Opening the library finishes the take; menu movement cannot introduce hidden gaps.
                if (recording != null) StopRecording("Route saved");
                oldCursor = Cursor.lockState; oldCursorVisible = Cursor.visible;
                var p = GameBridge.Player;
                if (p != null && p.playerControls != null)
                {
                    RestorePanelInput();
                    blockedPlayer = p;
                    blockedInput = p.playerControls.Player.Get();
                    suspendedActions.Clear();
                    foreach (var action in blockedInput.actions)
                        if (action.enabled) suspendedActions.Add(action);
                    blockedInput.Disable();
                }
            }
            else
            {
                // Release the input lock even if trimming or saving settings subsequently fails.
                panel = false;
                RestorePanelInput();
                if (!GameBridge.MenuOpen) { Cursor.lockState = oldCursor; Cursor.visible = oldCursorVisible; }
                pendingDeletion = null;
                if (editingRoute != null) CancelTrim();
                rebindTarget = null;
                editingText = false;
                SavePanelSettings();
            }
            panel = value;
        }

        void RestorePanelInput(bool releasingPlugin = false)
        {
            if (panel || blockedInput == null) return;
            // The game shares controls across players. Never revive controls for an old/despawned owner.
            var player = GameBridge.Player;
            bool samePlayer = blockedPlayer != null && blockedPlayer == player && player.isActiveAndEnabled &&
                player.playerControls != null && ReferenceEquals(blockedInput, player.playerControls.Player.Get());
            if (samePlayer)
            {
                // Escape can open the pause menu before Update closes our panel. Keep the restoration
                // pending until that menu closes; dropping it here permanently disabled movement.
                if (GameBridge.MenuOpen && !releasingPlugin) return;
                foreach (var action in suspendedActions) action.Enable();
            }
            suspendedActions.Clear(); blockedInput = null; blockedPlayer = null;
        }

        void RefreshLibrary()
        {
            RunIO(() => { var list = library.List(s => Logger.LogWarning(s)); return () => { entries = list; Notify("Library refreshed: " + list.Count + " routes."); }; });
        }
        void LoadRoute(RouteSummary summary)
        {
            CancelTrim();
            StopGhost();
            RunIO(() =>
            {
                var route = library.Load(summary.FilePath); return () =>
            {
                selected = route; selectedPath = summary.FilePath;
                Notify("Loaded " + route.Name + "." + (route.GameVersion != Application.version ? " Recorded on a different game version; map geometry may have changed." : ""));
            };
            });
        }
        void ImportCode()
        {
            string code = GUIUtility.systemCopyBuffer;
            StopGhost();
            RunIO(() =>
            {
                var route = RouteCodec.FromCode(code); string path = library.Save(route); var list = library.List(s => Logger.LogWarning(s));
                return () => { selected = route; selectedPath = path; entries = list; Notify("Imported " + route.Name + " on " + route.Map + "."); };
            });
        }
        void ImportFiles()
        {
            RunIO(() =>
            {
                int count = 0, failed = 0;
                foreach (var path in Directory.EnumerateFiles(library.Imports, "*.sroute").Take(100))
                {
                    try { library.Save(library.Load(path)); count++; }
                    catch (Exception e) { failed++; Logger.LogWarning(Path.GetFileName(path) + ": " + e.Message); }
                }
                var list = library.List(s => Logger.LogWarning(s));
                return () => { entries = list; Notify("Imported " + count + " files; " + failed + " rejected. Originals remain in Imports."); };
            });
        }
        void ExportFile()
        {
            var route = selected;
            RunIO(() => { string path = library.Export(route); return () => { GUIUtility.systemCopyBuffer = path; Notify("Exported .sroute file. Its full path is copied to the clipboard."); }; });
        }
        void CopyCode()
        {
            var route = selected;
            RunIO(() => { string code = RouteCodec.ToCode(route); return () => { GUIUtility.systemCopyBuffer = code; Notify("Route code copied (" + code.Length.ToString("N0") + " characters). If chat limits are exceeded, use Export file."); }; });
        }

        void RunIO(Func<Action> work)
        {
            if (busy || shuttingDown) throw new InvalidOperationException("A route file operation is still running or the plugin is shutting down.");
            busy = true;
            pendingIO = Task.Run(() =>
            {
                try { var done = work(); completed.Enqueue(() => { busy = false; done(); }); }
                catch (Exception e) { completed.Enqueue(() => { busy = false; Report(e); }); }
            });
        }
        void RequireReady()
        {
            if (!modEnabled.Value) throw new InvalidOperationException("Enable Route Runner in Settings > Mods first.");
            if (!GameBridge.Ready) throw new InvalidOperationException("Spawn into an exploration map first. Route Runner is disabled in matches.");
        }

        void ApplyEnabledState()
        {
            if (runtimeEnabled == modEnabled.Value) return;
            runtimeEnabled = modEnabled.Value;
            if (runtimeEnabled)
            {
                suppressHotkeysThroughFrame = Time.frameCount + 1;
                Notify("Route Runner enabled.");
                return;
            }
            try { StopRecording("Route saved; Route Runner disabled"); }
            finally
            {
                StopGhost();
                SetPanel(false);
                CancelTrim();
                GameBridge.ClearPlayerCache();
                Logger.LogInfo("Route Runner disabled. Settings and pending file operations remain available.");
            }
        }
        void RequireSelectedMap()
        {
            RequireReady();
            if (selected == null) throw new InvalidOperationException("Record or select a route first.");
            if (!string.Equals(selected.Map, GameBridge.Map, StringComparison.Ordinal)) throw new InvalidOperationException("Open " + selected.Map + " in exploration to use this route.");
        }
        static bool Down(Key key) => Keyboard.current != null && key != Key.None && Keyboard.current[key].wasPressedThisFrame;
        void Notify(string text) { status = text; statusUntil = Time.unscaledTime + 8; Logger.LogInfo(text); }
        void Report(Exception e) { status = e.Message; statusUntil = Time.unscaledTime + 12; Logger.LogError(e); }
        void Recover(Exception e)
        {
            capture?.Dispose(); capture = null;
            if (recording != null)
            {
                var partial = recording; recording = null;
                if (partial.Frames.Count >= 2)
                {
                    selected = partial; selectedPath = "";
                    if (!busy) RunIO(() => { string path = library.Save(partial); return () => { selectedPath = path; Notify("Partial route recovered and saved after an error. Check the log."); }; });
                }
            }
            StopGhost(); Report(e);
        }

        void OnApplicationQuit() { Shutdown(); }
        void OnDestroy() { Shutdown(); }
        void OnDisable()
        {
            try { SetPanel(false); }
            finally
            {
                RestorePanelInput(releasingPlugin: true);
                if (panelView != null) panelView.enabled = false;
            }
        }
        void Shutdown()
        {
            if (shuttingDown) return;
            try
            {
                if (recording != null && !busy) StopRecording("Route saved", refreshLibrary: false);
                SetPanel(false);
                RestorePanelInput(releasingPlugin: true);
                // Only shutdown waits. Normal gameplay never blocks on the background file worker.
                if (pendingIO != null && !pendingIO.Wait(TimeSpan.FromSeconds(5))) Logger.LogWarning("A route file operation is still finishing at shutdown.");
            }
            catch (Exception e) { Logger.LogError(e); }
            shuttingDown = true;
            capture?.Dispose(); StopGhost();
            GameBridge.ClearPlayerCache();
            DisposePanel();
        }
    }
}
