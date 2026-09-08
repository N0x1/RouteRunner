using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RouteRunner.Core
{
    public sealed class DeletedRoute
    {
        public string OriginalPath, RecoveryPath;
    }
    public sealed class DeleteBatch
    {
        public readonly List<DeletedRoute> Items = new List<DeletedRoute>();
        public readonly List<string> Errors = new List<string>();
    }
    public sealed partial class RouteLibrary
    {
        public string[] RouteFiles()
        {
            var maps = Path.Combine(Root, "Maps");
            if (!Directory.Exists(maps)) return Array.Empty<string>();
            CheckNoLinks(maps);
            return Directory.EnumerateDirectories(maps)
                .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.sroute", SearchOption.TopDirectoryOnly))
                .Where(f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0).ToArray();
        }

        public DeleteBatch Delete(IEnumerable<string> files)
        {
            // Validate every target before moving any file. Only map-library files are eligible.
            var paths = files.Select(OwnedRoutePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var batch = new DeleteBatch();
            var recovery = Path.Combine(Root, "Deleted", Guid.NewGuid().ToString("N"));
            CheckNoLinks(recovery);
            foreach (string path in paths)
            {
                try
                {
                    var destination = Path.Combine(recovery, Path.GetFileName(Path.GetDirectoryName(path)), Path.GetFileName(path));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Move(path, destination);
                    batch.Items.Add(new DeletedRoute { OriginalPath = path, RecoveryPath = destination });
                }
                catch (Exception error) { batch.Errors.Add(Path.GetFileName(path) + ": " + error.Message); }
            }
            return batch;
        }

        public int Restore(DeleteBatch batch)
        {
            int restored = 0;
            batch.Errors.Clear();
            // Removing from the end avoids repeatedly shifting an entire large batch during undo.
            for (int index = batch.Items.Count - 1; index >= 0; index--)
            {
                var item = batch.Items[index];
                try
                {
                    string destination = OwnedRoutePath(item.OriginalPath);
                    string source = Path.GetFullPath(item.RecoveryPath);
                    var deletedRoot = Path.Combine(Root, "Deleted") + Path.DirectorySeparatorChar;
                    if (!source.StartsWith(deletedRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid recovery path.");
                    CheckNoLinks(source);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Move(source, destination); // Never overwrite a newly recorded/imported route.
                    batch.Items.RemoveAt(index); restored++;
                }
                catch (Exception error) { batch.Errors.Add(Path.GetFileName(item.OriginalPath) + ": " + error.Message); }
            }
            return restored;
        }

        public void Replace(Route replacement, string file, Route original)
        {
            file = OwnedRoutePath(file);
            var current = Load(file);
            if (replacement.Id != current.Id || replacement.Map != current.Map ||
                !RouteContent.Matches(current, original))
                throw new IOException("This route changed on disk. Reload it before editing.");
            byte[] bytes = RouteCodec.Encode(replacement);
            string backup = Path.Combine(Root, "Backups", Guid.NewGuid().ToString("N") + ".sroute");
            CheckNoLinks(backup);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                File.Replace(temp, file, backup);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        string OwnedRoutePath(string file)
        {
            string path = Path.GetFullPath(file);
            string maps = Path.Combine(Root, "Maps");
            if (!string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(path)), maps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(path), ".sroute", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Only routes in this library can be edited or deleted.");
            CheckNoLinks(path);
            return path;
        }

        void CheckNoLinks(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string prefix = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.Equals(fullPath, Root, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The path is outside this route library.");
            // The configured root may sit on a linked Steam library or Wine drive. Only links
            // beneath it can redirect individual route operations outside that chosen storage.
            for (string current = fullPath; !string.Equals(current, Root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked folders or files inside the route library are not supported: " + current);
            }
        }
    }
}
