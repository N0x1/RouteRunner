using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RouteRunner.Core
{
    public sealed partial class RouteLibrary
    {
        public readonly string Root;
        public string Imports => Path.Combine(Root, "Imports");
        public string Exports => Path.Combine(Root, "Exports");
        public RouteLibrary(string root)
        {
            Root = Path.GetFullPath(root);
            if (Root.Length > Path.GetPathRoot(Root).Length)
                Root = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            CheckNoLinks(Imports); CheckNoLinks(Exports);
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Imports); Directory.CreateDirectory(Exports);
        }
        public string Save(Route route)
        {
            var bytes = RouteCodec.Encode(route);
            var folder = Path.Combine(Root, "Maps", MapKey(route.Map));
            CheckNoLinks(folder);
            Directory.CreateDirectory(folder);
            var destination = Path.Combine(folder, route.Id + ".sroute");
            CheckNoLinks(destination);
            if (File.Exists(destination))
            {
                if (FileContent.Matches(destination, bytes)) return destination;
                // A reused imported ID never overwrites somebody else's take.
                route.Id = Guid.NewGuid().ToString("N");
                bytes = RouteCodec.Encode(route);
                destination = Path.Combine(folder, route.Id + ".sroute");
            }
            WriteNew(destination, bytes);
            return destination;
        }
        public Route Load(string file)
        {
            var info = new FileInfo(file);
            if (info.Length > RouteCodec.MaxFileBytes) throw new InvalidDataException("Route file is too large.");
            return RouteCodec.Decode(File.ReadAllBytes(file));
        }
        public List<RouteSummary> List(Action<string> warning)
        {
            var result = new List<RouteSummary>();
            // Storage has exactly one map-directory level; do not traverse arbitrary imported directory trees.
            foreach (var path in RouteFiles())
            {
                if (result.Count >= 5000) { warning("Library display is limited to 5000 routes."); return result; }
                try
                {
                    if (new FileInfo(path).Length > RouteCodec.MaxFileBytes) throw new InvalidDataException("File is too large.");
                    using (var file = File.OpenRead(path))
                    {
                        var summary = RouteCodec.ReadSummary(file); summary.FilePath = path; result.Add(summary);
                    }
                }
                catch (Exception e) { warning(Path.GetFileName(path) + ": " + e.Message); }
            }
            return result.OrderBy(r => r.Map, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        public string Export(Route route)
        {
            var path = Path.Combine(Exports, MapKey(route.Map) + "-" + route.Id + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".sroute");
            CheckNoLinks(path);
            WriteNew(path, RouteCodec.Encode(route)); return path;
        }
        public static string MapKey(string map)
        {
            var stem = new string(map.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(48).ToArray());
            using (var sha = SHA256.Create()) return (stem.Length == 0 ? "map" : stem) + "-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(map)), 0, 8).Replace("-", "").ToLowerInvariant();
        }
        static void WriteNew(string path, byte[] bytes)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
