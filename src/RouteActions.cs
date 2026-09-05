using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RouteRunner.Core;
using UnityEngine;

namespace RouteRunner
{
    public sealed partial class Plugin
    {
        readonly HashSet<string> checkedRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] pendingDeletion;
        DeleteBatch lastDeleted;
        Route editingRoute;
        string editingPath;
        int trimFirst, trimLast;
        Vector2 bodyScroll;
        Color GhostColour => new Color(ghostRed.Value, ghostGreen.Value, ghostBlue.Value, opacity.Value);

        readonly RouteListView routeView = new RouteListView();
        List<RouteRow> VisibleRoutes
        {
            get { routeView.Refresh(entries, GameBridge.Map, search, allMaps); return routeView.Rows; }
        }

        void OpenFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }

        void RequestDeletion(bool all)
        {
            if (all)
                RunIO(() => { var files = library.RouteFiles(); return () => { pendingDeletion = files; }; });
            else pendingDeletion = checkedRoutes.ToArray();
        }
        void ConfirmDeletion()
        {
            var files = pendingDeletion; pendingDeletion = null;
            CancelTrim(); StopGhost();
            RunIO(() =>
            {
                var batch = library.Delete(files);
                var list = library.List(s => Logger.LogWarning(s));
                return () =>
                {
                    lastDeleted = batch; entries = list;
                    if (batch.Items.Any(i => string.Equals(i.OriginalPath, selectedPath, StringComparison.OrdinalIgnoreCase))) { selected = null; selectedPath = ""; }
                    foreach (var item in batch.Items) checkedRoutes.Remove(item.OriginalPath);
                    foreach (string error in batch.Errors) Logger.LogWarning(error);
                    Notify("Deleted " + batch.Items.Count + " routes; " + batch.Errors.Count + " failed. Undo is available; files are in Deleted.");
                };
            });
        }
        void UndoDeletion()
        {
            var batch = lastDeleted;
            RunIO(() =>
            {
                int count = library.Restore(batch); var list = library.List(s => Logger.LogWarning(s));
                return () =>
                {
                    entries = list; foreach (string error in batch.Errors) Logger.LogWarning(error);
                    Notify("Restored " + count + " routes; " + batch.Items.Count + " still in Deleted.");
                };
            });
        }

        void BeginTrim()
        {
            if (selected == null || busy) return;
            StopGhost();
            editingRoute = selected; editingPath = selectedPath;
            trimFirst = 0; trimLast = selected.Frames.Count - 1;
            panelTab = 3; bodyScroll = Vector2.zero;
        }
        void CancelTrim()
        {
            if (editingRoute == null) return;
            StopGhost(); editingRoute = null; editingPath = null;
        }
        void PreviewTrim(int frame)
        {
            RequireSelectedMap();
            if (ghost == null)
                using (var rig = new CaptureRig(GameBridge.Player, evaluateAnimation: false)) ghost = new GhostRig(rig, editingRoute, GhostColour);
            playbackPlayer = GameBridge.Player; playing = false; countdown = 0;
            playbackTime = editingRoute.Frames[frame].Time;
            ghost.Show(editingRoute, playbackTime);
        }
        void SaveTrim()
        {
            var original = editingRoute; string path = editingPath;
            int first = trimFirst, last = trimLast;
            StopGhost();
            RunIO(() =>
            {
                var trimmed = RouteEditor.Trim(original, first, last);
                if (string.IsNullOrEmpty(path)) path = library.Save(trimmed);
                else library.Replace(trimmed, path, original);
                var list = library.List(s => Logger.LogWarning(s));
                return () =>
                {
                    selected = trimmed; selectedPath = path; entries = list; editingRoute = null; editingPath = null;
                    Notify("Trim saved: " + trimmed.Duration.ToString("0.00") + "s. Previous saved take is kept in Backups.");
                };
            });
        }
    }
}
