using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;

namespace RouteRunner.Core
{
    // Pure managed format. No Unity objects, executable content, or game assets are shared.
    public static class RouteCodec
    {
        public const int MaxRawBytes = 64 * 1024 * 1024;
        public const int MaxFileBytes = MaxRawBytes + 1024 * 1024;
        public const int MaxBones = 256;
        public const int MaxFrames = 18001;
        public const int MaxClipboardChars = 2 * 1024 * 1024;
        public const string Prefix = "STRAFTAT-ROUTE-1:";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(Route route)
        {
            Validate(route);
            byte[] raw;
            // Pre-size once and use its backing array: avoid geometric growth plus a full payload copy.
            int payloadSize = PayloadSize(route);
            using (var buffer = new MemoryStream(payloadSize))
            {
                using (var w = new BinaryWriter(buffer, Utf8, true))
                {
                    w.Write(1);
                    WriteText(w, route.Id); WriteText(w, route.Name); WriteText(w, route.Map);
                    WriteText(w, route.GameVersion); WriteText(w, route.CreatedUtc);
                    w.Write(route.SampleRate); w.Write(route.Bones.Length); w.Write(route.Frames.Count); w.Write(route.Duration);
                    foreach (var bone in route.Bones) WriteText(w, bone);
                    var poseBytes = new byte[(7 + route.Bones.Length * 10) * sizeof(float)];
                    foreach (var frame in route.Frames)
                    {
                        w.Write(frame.Time); w.Write(frame.State); w.Write(frame.Cut);
                        if (BitConverter.IsLittleEndian)
                        {
                            Buffer.BlockCopy(frame.Pose, 0, poseBytes, 0, poseBytes.Length);
                            w.Write(poseBytes);
                        }
                        else foreach (var f in frame.Pose) w.Write(f);
                    }
                }
                if (buffer.Length > MaxRawBytes) throw Bad("Route exceeds the 64 MiB limit.");
                if (buffer.Length != payloadSize) throw Bad("Unexpected route payload size.");
                raw = buffer.GetBuffer();
            }
            using (var file = new MemoryStream())
            {
                using (var w = new BinaryWriter(file, Utf8, true))
                {
                    w.Write(new byte[] { 83, 82, 71, 49 }); w.Write(raw.Length);
                    using (var sha = SHA256.Create()) w.Write(sha.ComputeHash(raw));
                }
                using (var gzip = new GZipStream(file, CompressionLevel.Optimal, true)) gzip.Write(raw, 0, raw.Length);
                return file.ToArray();
            }
        }

        public static Route Decode(byte[] file)
        {
            if (file == null || file.Length < 50 || file.Length > MaxFileBytes) throw Bad("Invalid route file size.");
            using (var source = new MemoryStream(file, false))
            using (var reader = new BinaryReader(source, Utf8, true))
            {
                if (reader.ReadUInt32() != 0x31475253) throw Bad("This is not a Route Runner .sroute file.");
                int size = reader.ReadInt32();
                if (size < 40 || size > MaxRawBytes) throw Bad("Invalid expanded route size.");
                byte[] hash = reader.ReadBytes(32), raw = new byte[size];
                using (var gzip = new GZipStream(source, CompressionMode.Decompress, true))
                {
                    int offset = 0;
                    while (offset < size)
                    {
                        int read = gzip.Read(raw, offset, size - offset);
                        if (read == 0) throw Bad("Truncated route data.");
                        offset += read;
                    }
                    if (gzip.ReadByte() != -1) throw Bad("Expanded data exceeds declared size.");
                }
                using (var sha = SHA256.Create())
                {
                    var actual = sha.ComputeHash(raw);
                    for (int i = 0; i < 32; i++) if (hash[i] != actual[i]) throw Bad("Route checksum failed.");
                }
                using (var buffer = new MemoryStream(raw, false))
                using (var r = new BinaryReader(buffer, Utf8))
                {
                    if (r.ReadInt32() != 1) throw Bad("Unsupported route format version.");
                    var route = new Route
                    {
                        Id = ReadText(r, 32),
                        Name = ReadText(r, 80),
                        Map = ReadText(r, 256),
                        GameVersion = ReadText(r, 80),
                        CreatedUtc = ReadText(r, 80),
                        SampleRate = r.ReadInt32()
                    };
                    int bones = r.ReadInt32(), count = r.ReadInt32();
                    float duration = r.ReadSingle();
                    if (bones < 1 || bones > MaxBones || count < 2 || count > MaxFrames) throw Bad("Invalid bone or sample count.");
                    int width = 7 + bones * 10;
                    if ((long)count * (6 + width * 4) > buffer.Length - buffer.Position) throw Bad("Truncated samples.");
                    route.Bones = new string[bones];
                    for (int i = 0; i < bones; i++) route.Bones[i] = ReadText(r, 1024);
                    if ((long)count * (6 + width * 4) != buffer.Length - buffer.Position) throw Bad("Invalid sample payload length.");
                    route.Frames.Capacity = count;
                    for (int i = 0; i < count; i++)
                    {
                        var frame = new Frame { Time = r.ReadSingle(), State = r.ReadByte(), Cut = ReadBool(r), Pose = new float[width] };
                        if (BitConverter.IsLittleEndian)
                        {
                            Buffer.BlockCopy(raw, (int)buffer.Position, frame.Pose, 0, width * sizeof(float));
                            buffer.Position += width * sizeof(float);
                        }
                        else for (int j = 0; j < width; j++) frame.Pose[j] = r.ReadSingle();
                        route.Frames.Add(frame);
                    }
                    Validate(route);
                    if (route.Duration != duration) throw Bad("Route duration does not match its samples.");
                    return route;
                }
            }
        }

        // Only labels are read here, for a responsive library. Decode verifies the whole file on selection.
        public static RouteSummary ReadSummary(Stream file)
        {
            using (var outer = new BinaryReader(file, Utf8, true))
            {
                if (outer.ReadUInt32() != 0x31475253) throw Bad("Invalid route header.");
                int size = outer.ReadInt32();
                if (size < 40 || size > MaxRawBytes || outer.ReadBytes(32).Length != 32) throw Bad("Invalid route size.");
                using (var gzip = new GZipStream(file, CompressionMode.Decompress, true))
                using (var r = new BinaryReader(gzip, Utf8))
                {
                    if (r.ReadInt32() != 1) throw Bad("Unsupported route version.");
                    var result = new RouteSummary { Id = ReadText(r, 32), Name = ReadText(r, 80), Map = ReadText(r, 256) };
                    ReadText(r, 80); ReadText(r, 80);
                    int rate = r.ReadInt32(), bones = r.ReadInt32(), count = r.ReadInt32();
                    result.Duration = r.ReadSingle();
                    if (!Guid.TryParseExact(result.Id, "N", out _) || rate < 10 || rate > 60 || bones < 1 || bones > MaxBones || count < 2 || count > MaxFrames || !Finite(result.Duration) || result.Duration <= 0 || result.Duration > 600)
                        throw Bad("Invalid route summary.");
                    return result;
                }
            }
        }

        public static string ToCode(Route route)
        {
            var file = Encode(route);
            if (((long)file.Length + 2) / 3 * 4 + Prefix.Length > MaxClipboardChars)
                throw Bad("This route is too large for clipboard sharing. Send its .sroute file instead.");
            return Prefix + Convert.ToBase64String(file);
        }

        public static Route FromCode(string code)
        {
            if (code == null || code.Length > MaxClipboardChars + 256) throw Bad("Clipboard code is too large. Import the .sroute file instead.");
            code = code.Trim();
            if (code.Length > MaxClipboardChars || !code.StartsWith(Prefix, StringComparison.Ordinal)) throw Bad("Clipboard does not contain a STRAFTAT route code.");
            return Decode(Convert.FromBase64String(code.Substring(Prefix.Length)));
        }

        public static void Validate(Route route)
        {
            if (route == null) throw Bad("No route selected.");
            if (!Guid.TryParseExact(route.Id, "N", out _)) throw Bad("Invalid route ID.");
            CheckText(route.Name, 80); CheckText(route.Map, 256); CheckText(route.GameVersion, 80); CheckText(route.CreatedUtc, 80);
            if (string.IsNullOrWhiteSpace(route.Name) || string.IsNullOrWhiteSpace(route.Map)) throw Bad("Route needs a name and map.");
            if (route.SampleRate < 10 || route.SampleRate > 60) throw Bad("Invalid sample rate.");
            if (route.Bones == null || route.Bones.Length < 1 || route.Bones.Length > MaxBones) throw Bad("Invalid rig.");
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in route.Bones) { CheckText(b, 1024); if (!paths.Add(b)) throw Bad("Duplicate bone paths."); }
            if (route.Frames.Count < 2 || route.Frames.Count > MaxFrames) throw Bad("Record at least two samples.");
            int width = 7 + route.Bones.Length * 10;
            if ((long)route.Frames.Count * (width * 4 + 6) + 300000 > MaxRawBytes) throw Bad("Route sample limit exceeded.");
            float last = -1;
            foreach (var f in route.Frames)
            {
                if (f == null || !Finite(f.Time) || f.Time < 0 || f.Time <= last || f.Time > 600 || f.State > 31) throw Bad("Invalid sample timeline or movement state.");
                if (last == -1 && f.Time != 0) throw Bad("First sample must start at zero.");
                last = f.Time;
                if (f.Pose == null || f.Pose.Length != width) throw Bad("Invalid pose size.");
                foreach (float v in f.Pose) if (!Finite(v) || Math.Abs(v) > 100000) throw Bad("Invalid pose coordinate.");
                CheckRotation(f.Pose, 3);
                for (int j = 7; j < width; j += 10)
                {
                    CheckRotation(f.Pose, j + 3);
                    // Capture stores world (lossy) scale, not a normalized size multiplier.
                    // Imported rigs and parent transforms can exceed 100, including by rounding.
                    // The finite +/-100000 pose bound above also applies to every scale component.
                }
            }
        }

        static void CheckRotation(float[] p, int j)
        {
            float n = p[j] * p[j] + p[j + 1] * p[j + 1] + p[j + 2] * p[j + 2] + p[j + 3] * p[j + 3];
            if (n < 0.9f || n > 1.1f) throw Bad("Invalid pose rotation.");
        }
        static int PayloadSize(Route route)
        {
            long size = 20 + (long)route.Frames.Count * (6 + (7 + route.Bones.Length * 10) * sizeof(float));
            foreach (string text in new[] { route.Id, route.Name, route.Map, route.GameVersion, route.CreatedUtc }) size += 2 + Utf8.GetByteCount(text);
            foreach (string bone in route.Bones) size += 2 + Utf8.GetByteCount(bone);
            if (size > MaxRawBytes) throw Bad("Route exceeds the 64 MiB limit.");
            return (int)size;
        }
        static bool ReadBool(BinaryReader r) { byte b = r.ReadByte(); if (b > 1) throw Bad("Invalid cut flag."); return b == 1; }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static void CheckText(string text, int max)
        {
            if (text == null || text.Length > max) throw Bad("Invalid metadata length.");
            foreach (char c in text) if (char.IsControl(c)) throw Bad("Control characters are not allowed in metadata.");
        }
        static void WriteText(BinaryWriter w, string text) { var b = Utf8.GetBytes(text); w.Write((ushort)b.Length); w.Write(b); }
        static string ReadText(BinaryReader r, int max)
        {
            int length = r.ReadUInt16(); if (length > max * 4) throw Bad("Metadata is too long.");
            var b = r.ReadBytes(length); if (b.Length != length) throw Bad("Truncated metadata.");
            var text = Utf8.GetString(b); CheckText(text, max); return text;
        }
        static InvalidDataException Bad(string message) => new InvalidDataException(message);
    }
}
