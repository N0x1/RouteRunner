using System;
using System.IO;

namespace RouteRunner.Core
{
    public static class LegacyMigration
    {
        public static bool CopyConfig(string legacy, string current)
        {
            if (File.Exists(current) || !File.Exists(legacy)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(current));
            File.Copy(legacy, current);
            return true;
        }

        public static int CopyRoutes(string legacyRoot, RouteLibrary current, Action<string> warning)
        {
            string marker = Path.Combine(current.Root, ".legacy-migrated");
            if (File.Exists(marker) || !Directory.Exists(legacyRoot)) return 0;
            if ((File.GetAttributes(legacyRoot) & FileAttributes.ReparsePoint) != 0) throw new IOException("Legacy route folder is a link.");
            int count = 0, failed = 0;
            var old = new RouteLibrary(legacyRoot);
            foreach (string file in old.RouteFiles())
            {
                try { current.Save(old.Load(file)); count++; }
                catch (Exception error) { failed++; warning("Old route migration: " + Path.GetFileName(file) + ": " + error.Message); }
            }
            foreach (var pair in new[] { new[] { old.Imports, current.Imports }, new[] { old.Exports, current.Exports } })
            {
                if ((File.GetAttributes(pair[0]) & FileAttributes.ReparsePoint) != 0) { failed++; warning("Skipped linked legacy folder."); continue; }
                foreach (string file in Directory.EnumerateFiles(pair[0], "*.sroute"))
                {
                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked file.");
                        string destination = Path.Combine(pair[1], Path.GetFileName(file));
                        if (!File.Exists(destination)) File.Copy(file, destination);
                        else if (!FileContent.Matches(file, destination)) throw new IOException("A different file already exists at the destination.");
                    }
                    catch (Exception error) { failed++; warning("Old sharing file migration: " + Path.GetFileName(file) + ": " + error.Message); }
                }
            }
            // Mark even partial migration: restarting must not resurrect routes deliberately deleted afterwards.
            File.WriteAllText(marker, "Copied " + count + " routes; " + failed + " skipped. Original folder retained as backup.");
            return count;
        }
    }
}
