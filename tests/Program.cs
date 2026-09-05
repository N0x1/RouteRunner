using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using RouteRunner.Core;

class Program
{
    static int passed;
    static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--benchmark")) { RunBenchmarks(); return 0; }
            RunTests();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: RouteRunner developer tests");
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static void RunTests()
    {
        Test("Hex RGB accepts prefix, case and whitespace while identifying absent alpha", () =>
        {
            foreach (string text in new[] { "#45F454", "45f454", "  #45f454  " })
            {
                Assert(HexColour.TryParse(text, out uint colour, out bool alpha));
                Assert(colour == 0x45F454FFu && !alpha);
            }
        });
        Test("Hex RGBA and shorthand preserve colour channels and explicit transparent alpha", () =>
        {
            Assert(HexColour.TryParse("#45F45480", out uint colour, out bool alpha) && colour == 0x45F45480u && alpha);
            Assert(HexColour.TryParse("0f8", out colour, out alpha) && colour == 0x00FF88FFu && !alpha);
            Assert(HexColour.TryParse("#0f80", out colour, out alpha) && colour == 0x00FF8800u && alpha);
            Assert(HexColour.TryParse("000000", out colour, out alpha) && colour == 0x000000FFu && !alpha);
            Assert(HexColour.TryParse("ffffffff", out colour, out alpha) && colour == uint.MaxValue && alpha);
        });
        Test("Invalid hex is rejected rather than applied as black", () =>
        {
            foreach (string text in new[] { null, "", "#", "12", "12345", "1234567", "123456789", "##abc", "#GG00FF", "1 3456", "-ff0000" })
                Assert(!HexColour.TryParse(text, out _, out _));
        });
        Test("Hide HUD suppresses active status and notifications without hiding the panel; disabled mod hides both", () =>
        {
            Assert(OverlayState.Mode(true, false, true, true, 1, 8, false) == OverlayMode.Hidden);
            Assert(OverlayState.Mode(true, false, false, false, 1, 8, false) == OverlayMode.Hidden);
            Assert(OverlayState.Mode(true, true, true, true, 1, 8, false) == OverlayMode.Panel);
            Assert(OverlayState.Mode(false, true, true, true, 1, 8, true) == OverlayMode.Hidden);
            Assert(OverlayState.Mode(true, false, true, false, 1, 8, true) == OverlayMode.Hud);
        });
        Test("Idle overlay shuts down after notifications; active routes and panel retain the right UI mode", () =>
        {
            Assert(OverlayState.Mode(true, false, false, false, 0, 0) == OverlayMode.Hidden);
            Assert(OverlayState.Mode(true, false, false, false, 7, 8) == OverlayMode.Hud);
            Assert(OverlayState.Mode(true, false, false, false, 8, 8) == OverlayMode.Hidden);
            Assert(OverlayState.Mode(true, true, false, false, 9, 8) == OverlayMode.Panel);
            Assert(OverlayState.Mode(true, false, true, false, 9, 8) == OverlayMode.Hud);
            Assert(OverlayState.Mode(true, false, false, true, 9, 8) == OverlayMode.Hud);
            Assert(OverlayState.Mode(false, true, true, true, 7, 8) == OverlayMode.Hidden);
        });
        Test("Binary pose and metadata round trip", () =>
        {
            var r = Example(); var copy = RouteCodec.Decode(RouteCodec.Encode(r));
            Assert(copy.Map == r.Map && copy.Name == r.Name && copy.Duration == r.Duration);
            Assert(copy.Frames[1].Pose.SequenceEqual(r.Frames[1].Pose));
            Assert(copy.Frames[1].Cut && copy.Frames[1].State == 13);
        });
        Test("Clipboard round trip including surrounding whitespace", () => Assert(RouteCodec.FromCode(" \r\n" + RouteCodec.ToCode(Example()) + "\n").Map == Example().Map));
        Test("Bulk pose codec preserves the original wire payload including UTF8 and signed zero", () =>
        {
            var r = Example(); r.Bones[0] = "Rig/" + string.Concat(Enumerable.Repeat("é😀", 200));
            r.Frames[0].Pose[0] = BitConverter.Int32BitsToSingle(int.MinValue);
            r.Frames[1].Pose[14] = 100.00001f;
            using var raw = new MemoryStream();
            using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
            {
                void Text(string value) { byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
                writer.Write(1); Text(r.Id); Text(r.Name); Text(r.Map); Text(r.GameVersion); Text(r.CreatedUtc);
                writer.Write(r.SampleRate); writer.Write(r.Bones.Length); writer.Write(r.Frames.Count); writer.Write(r.Duration);
                foreach (string bone in r.Bones) Text(bone);
                foreach (var frame in r.Frames)
                { writer.Write(frame.Time); writer.Write(frame.State); writer.Write(frame.Cut); foreach (float value in frame.Pose) writer.Write(value); }
            }
            Assert(Unpack(RouteCodec.Encode(r)).SequenceEqual(raw.ToArray()));
            var restored = RouteCodec.Decode(Pack(raw.ToArray()));
            Assert(BitConverter.SingleToInt32Bits(restored.Frames[0].Pose[0]) == int.MinValue);
            Assert(RouteContent.Matches(r, restored));
        });
        Test("Stale edit comparison detects changes in metadata, state, paths and exact pose bits", () =>
        {
            var original = Example();
            foreach (Action<Route> change in new Action<Route>[] {
                r => r.Name += " edited", r => r.Frames[1].State ^= 1, r => r.Frames[1].Cut = false,
                r => r.Bones[0] += " changed", r => r.Frames[0].Pose[0] = BitConverter.Int32BitsToSingle(int.MinValue),
                r => r.Frames[1].Time += .1f })
            {
                var copy = RouteCodec.Decode(RouteCodec.Encode(original));
                Assert(RouteContent.Matches(original, copy)); change(copy); Assert(!RouteContent.Matches(original, copy));
            }
        });
        Test("Cached route view retains filtering, order and the display limit across invalidation", () =>
        {
            var entries = Enumerable.Range(0, 205).Select(i => new RouteSummary { Name = "Take " + i, Map = i == 0 ? "Other" : "Arena", FilePath = "file" + i }).ToList();
            var view = new RouteListView();
            Assert(view.Refresh(entries, "Arena", "", false) && view.Rows.Count == 200 && view.HasMore);
            Assert(view.Rows[0].Entry == entries[1]);
            Assert(!view.Refresh(entries, "Arena", "", false));
            Assert(view.Refresh(entries, "Other", "", false) && view.Rows.Count == 1 && !view.HasMore);
            Assert(view.Refresh(entries, "Other", "TAKE 204 ARENA", true) && view.Rows.Count == 1);
            Assert(view.Rows[0].Entry == entries[204]);
            Assert(view.Refresh(entries.ToList(), "Other", "TAKE 204 ARENA", true));
            view.Refresh(entries, "Other", "missing", true); Assert(view.Rows.Count == 0);
        });
        Test("Unchanged route-view redraw checks allocate no managed memory", () =>
        {
            var entries = new[] { new RouteSummary { Name = "Take", Map = "Arena", FilePath = "test" } };
            var view = new RouteListView(); view.Refresh(entries, "Arena", "", false);
            for (int i = 0; i < 100; i++) view.Refresh(entries, "Arena", "", false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) Assert(!view.Refresh(entries, "Arena", "", false));
            Assert(GC.GetAllocatedBytesForCurrentThread() == before);
        });
        Test("Streamed file comparison handles boundaries, changed bytes and different lengths", () => WithLibrary(library =>
        {
            var bytes = Enumerable.Range(0, 20001).Select(i => (byte)i).ToArray();
            string one = Path.Combine(library.Imports, "one"), two = Path.Combine(library.Imports, "two");
            File.WriteAllBytes(one, bytes); File.WriteAllBytes(two, bytes);
            Assert(FileContent.Matches(one, bytes) && FileContent.Matches(one, two));
            bytes[16384] ^= 1;
            Assert(!FileContent.Matches(one, bytes));
            File.WriteAllBytes(two, bytes); Assert(!FileContent.Matches(one, two));
            File.WriteAllBytes(two, new byte[2]); Assert(!FileContent.Matches(one, two));
        }));
        Test("Mod Menu key names bridge letter, digit, modifier, navigation and numpad bindings", () =>
        {
            foreach (var pair in new[] { ("A", "A"), ("F7", "F7"), ("Digit0", "Alpha0"), ("Digit9", "Alpha9"),
                ("NumpadEnter", "KeypadEnter"), ("NumpadPlus", "KeypadPlus"), ("Numpad5", "Keypad5"),
                ("Enter", "Return"), ("LeftCtrl", "LeftControl"), ("RightCtrl", "RightControl"),
                ("LeftMeta", "LeftWindows"), ("RightMeta", "RightWindows"), ("ContextMenu", "Menu"),
                ("Backquote", "BackQuote"), ("PrintScreen", "Print"), ("NumLock", "Numlock"), ("PageUp", "PageUp") })
            { Assert(KeyNames.Legacy(pair.Item1) == pair.Item2); Assert(KeyNames.InputSystem(pair.Item2) == pair.Item1); }
        });
        Test("World bone scales above 100 survive file and clipboard sharing unchanged", () =>
        {
            var r = Example();
            r.Frames[0].Pose[14] = 100.00001f;
            r.Frames[1].Pose[15] = 250f;
            r.Frames[2].Pose[16] = -1000f;
            var fileCopy = RouteCodec.Decode(RouteCodec.Encode(r));
            var codeCopy = RouteCodec.FromCode(RouteCodec.ToCode(r));
            for (int i = 0; i < r.Frames.Count; i++)
            {
                Assert(fileCopy.Frames[i].Pose.SequenceEqual(r.Frames[i].Pose));
                Assert(codeCopy.Frames[i].Pose.SequenceEqual(r.Frames[i].Pose));
            }
        });
        Test("Nonfinite and excessive bone scales remain rejected on save and import", () =>
        {
            foreach (float scale in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001f, -100001f })
            {
                var r = Example(); r.Frames[0].Pose[14] = scale;
                Reject(() => RouteCodec.Encode(r));
                var raw = Unpack(RouteCodec.Encode(Example()));
                BitConverter.GetBytes(scale).CopyTo(raw, raw.Length - 4);
                Reject(() => RouteCodec.Decode(Pack(raw)));
            }
        });
        Test("Library header matches full route", () =>
        {
            var r = Example(); using var stream = new MemoryStream(RouteCodec.Encode(r)); var h = RouteCodec.ReadSummary(stream);
            Assert(h.Map == r.Map && h.Name == r.Name && h.Duration == r.Duration);
        });
        Test("Corrupt checksum rejected", () => { var bytes = RouteCodec.Encode(Example()); bytes[8] ^= 1; Reject(() => RouteCodec.Decode(bytes)); });
        Test("Wrong format and truncated files rejected", () =>
        {
            var b = RouteCodec.Encode(Example()); b[0] = 0; Reject(() => RouteCodec.Decode(b));
            var valid = RouteCodec.Encode(Example());
            foreach (int n in new[] { 0, 20, 50, valid.Length / 2 }) Reject(() => RouteCodec.Decode(valid.Take(n).ToArray()));
        });
        Test("Oversized expansion and mismatched declared size rejected", () =>
        {
            var b = RouteCodec.Encode(Example()); BitConverter.GetBytes(int.MaxValue).CopyTo(b, 4); Reject(() => RouteCodec.Decode(b));
            b = RouteCodec.Encode(Example()); BitConverter.GetBytes(60).CopyTo(b, 4); Reject(() => RouteCodec.Decode(b));
        });
        Test("NaN and infinity rejected on read as well as write", () =>
        {
            var r = Example(); r.Frames[0].Pose[0] = float.NaN; Reject(() => RouteCodec.Encode(r));
            var raw = Unpack(RouteCodec.Encode(Example())); BitConverter.GetBytes(float.PositiveInfinity).CopyTo(raw, raw.Length - 4);
            Reject(() => RouteCodec.Decode(Pack(raw)));
        });
        Test("Nonmonotonic timeline, nonzero origin, and invalid rotations rejected", () =>
        {
            var r = Example(); r.Frames[1].Time = 0; Reject(() => RouteCodec.Encode(r));
            r = Example(); r.Frames[0].Time = .01f; Reject(() => RouteCodec.Encode(r));
            r = Example(); r.Frames[0].Pose[6] = 0; Reject(() => RouteCodec.Encode(r));
        });
        Test("Invalid counts rejected before sample allocation", () =>
        {
            var raw = Unpack(RouteCodec.Encode(Example()));
            using var ms = new MemoryStream(raw); using var br = new BinaryReader(ms);
            br.ReadInt32(); for (int i = 0; i < 5; i++) { int length = br.ReadUInt16(); ms.Position += length; }
            ms.Position += 4; BitConverter.GetBytes(int.MaxValue).CopyTo(raw, (int)ms.Position);
            Reject(() => RouteCodec.Decode(Pack(raw)));
        });
        Test("Duplicate paths and malformed clipboard input rejected", () =>
        {
            var r = Example(); r.Bones = new[] { "same", "same" }; Reject(() => RouteCodec.Encode(r));
            Reject(() => RouteCodec.FromCode("not a route")); Reject(() => RouteCodec.FromCode(RouteCodec.Prefix + "???"));
            Reject(() => RouteCodec.FromCode(new string('x', RouteCodec.MaxClipboardChars + 257)));
        });
        Test("Metadata limits and control characters rejected", () =>
        {
            var r = Example(); r.Name = new string('x', 81); Reject(() => RouteCodec.Encode(r));
            r = Example(); r.Name = "bad\nname"; Reject(() => RouteCodec.Encode(r));
        });
        Test("Sample lookup supports end, reverse seek, and boundaries", () =>
        {
            var r = Example(); Assert(Timeline.FindFrame(r.Frames, -1) == 0); Assert(Timeline.FindFrame(r.Frames, .5f) == 0);
            Assert(Timeline.FindFrame(r.Frames, 1) == 1); Assert(Timeline.FindFrame(r.Frames, 100) == 2); Assert(Timeline.FindFrame(r.Frames, .25f) == 0);
        });
        Test("Teleport boundaries are held, regular movement interpolates", () =>
        {
            var r = Example(); Assert(Timeline.Blend(r.Frames[0], r.Frames[1], .5f) == 0);
            Assert(Math.Abs(Timeline.Blend(r.Frames[1], r.Frames[2], 1.5f) - .5f) < .001);
        });
        Test("Map identity avoids slash, reserved filename, case and suffix collisions", () =>
        {
            Assert(RouteLibrary.MapKey("A/B") != RouteLibrary.MapKey("AB")); Assert(RouteLibrary.MapKey("Arena") != RouteLibrary.MapKey("arena"));
            Assert(RouteLibrary.MapKey("Arena") != RouteLibrary.MapKey("Arena_alt"));
            Assert(RouteLibrary.MapKey("../../CON") != "CON" && !RouteLibrary.MapKey("../../CON").Contains(".."));
        });
        Test("Save, reload, import ID collision, export, and corrupted file isolation", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "RouteRunnerTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var library = new RouteLibrary(folder); var r = Example(); string one = library.Save(r);
                Assert(library.Save(r) == one);
                var other = Example(); other.Id = r.Id; other.Name = "Different take"; string two = library.Save(other);
                Assert(one != two && library.Load(one).Name == r.Name);
                string export = library.Export(r); Assert(File.Exists(export));
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(one), "broken.sroute"), new byte[60]);
                int warnings = 0; var list = library.List(_ => warnings++); Assert(list.Count == 2 && warnings == 1);
                Assert(!Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories).Any());
            }
            finally { Directory.Delete(folder, true); }
        });
        Test("Representative 60-second, 96-bone recording round trip", () =>
        {
            var r = Example(); r.Bones = Enumerable.Range(0, 96).Select(i => "bone/" + i).ToArray(); r.Frames.Clear();
            for (int i = 0; i <= 1800; i++)
            {
                var f = MakeFrame(i / 30f, 96); f.Pose[0] = i * .13f;
                for (int b = 0; b < 96; b++) { int j = 7 + b*10; f.Pose[j] = (float)Math.Sin(i*.03+b)*.2f; f.Pose[j+1] = b*.01f; }
                r.Frames.Add(f);
            }
            byte[] file = RouteCodec.Encode(r); var copy = RouteCodec.Decode(file);
            Assert(copy.Frames.Count == 1801 && copy.Duration == 60);
            Console.WriteLine("  Synthetic 60s / 96 bones: " + file.Length.ToString("N0") + " bytes compressed.");
        });
        Test("Trim keeps exact poses and state, resets time and first cut, and leaves original unchanged", () =>
        {
            var original = Example();
            var before = RouteCodec.Encode(original);
            var trim = RouteEditor.Trim(original, 1, 2);
            Assert(trim.Id == original.Id && trim.Map == original.Map && trim.Bones.SequenceEqual(original.Bones));
            Assert(trim.Duration == 1 && trim.Frames.Count == 2 && trim.Frames[0].Time == 0 && !trim.Frames[0].Cut);
            Assert(trim.Frames[0].State == original.Frames[1].State && trim.Frames[0].Pose.SequenceEqual(original.Frames[1].Pose));
            Assert(trim.Frames[1].Cut == original.Frames[2].Cut);
            Assert(RouteCodec.Decode(RouteCodec.Encode(trim)).Duration == 1);
            trim.Frames[0].Pose[0] = 500; trim.Bones[0] = "different";
            Assert(before.SequenceEqual(RouteCodec.Encode(original)));
        });
        Test("Trim rejects empty, reversed and out-of-range selections", () =>
        {
            foreach (var bounds in new[] { (-1, 2), (0, 3), (1, 1), (2, 1) })
            {
                bool rejected = false;
                try { RouteEditor.Trim(Example(), bounds.Item1, bounds.Item2); } catch (ArgumentException) { rejected = true; }
                Assert(rejected);
            }
        });
        Test("Trim preserves internal teleports and uneven sample timing", () =>
        {
            var r = Example(); r.Frames[1].Time = .17f; r.Frames[2].Time = 1.23f;
            var trimmed = RouteEditor.Trim(r, 0, 2);
            Assert(trimmed.Frames[1].Cut && trimmed.Frames[1].Time == .17f && trimmed.Duration == 1.23f);
        });
        Test("Bulk deletion and undo span maps without touching imports or exports", () => WithLibrary(library =>
        {
            var one = Example(); string first = library.Save(one);
            var two = Example(); two.Map = "Other map"; string second = library.Save(two);
            var three = Example(); string keep = library.Save(three);
            string exported = library.Export(one);
            string incoming = Path.Combine(library.Imports, "take.sroute"); File.Copy(first, incoming);
            var batch = library.Delete(new[] { first, second, first });
            Assert(batch.Items.Count == 2 && batch.Errors.Count == 0);
            Assert(!File.Exists(first) && !File.Exists(second) && File.Exists(keep));
            Assert(File.Exists(exported) && File.Exists(incoming) && library.List(_ => { }).Count == 1);
            Assert(library.Restore(batch) == 2 && batch.Items.Count == 0);
            Assert(library.Load(first).Id == one.Id && library.Load(second).Id == two.Id);
        }));
        Test("Clear all discovers files beyond the library UI limit and includes malformed routes", () => WithLibrary(library =>
        {
            string first = library.Save(Example());
            string folder = Path.GetDirectoryName(first);
            for (int i = 0; i < 5001; i++) File.WriteAllBytes(Path.Combine(folder, i + ".sroute"), Array.Empty<byte>());
            Assert(library.RouteFiles().Length == 5002);
            var batch = library.Delete(library.RouteFiles());
            Assert(batch.Items.Count == 5002 && library.RouteFiles().Length == 0);
        }));
        Test("Delete validates all paths before modifying any and undo never overwrites", () => WithLibrary(library =>
        {
            var r = Example(); string file = library.Save(r);
            string incoming = Path.Combine(library.Imports, "keep.sroute"); File.Copy(file, incoming);
            RejectIO(() => library.Delete(new[] { file, incoming }));
            RejectIO(() => library.Delete(new[] { Path.Combine(library.Root, "Maps", "..", "Exports", "keep.sroute") }));
            Assert(File.Exists(file) && File.Exists(incoming));
            var batch = library.Delete(new[] { file });
            File.WriteAllText(file, "new content");
            Assert(library.Restore(batch) == 0 && batch.Items.Count == 1 && batch.Errors.Count == 1);
            Assert(File.ReadAllText(file) == "new content" && File.Exists(batch.Items[0].RecoveryPath));
        }));
        Test("Trim replaces a route atomically, retains backup, and refuses stale edits", () => WithLibrary(library =>
        {
            var original = Example(); string path = library.Save(original);
            var trimmed = RouteEditor.Trim(original, 1, 2);
            library.Replace(trimmed, path, original);
            Assert(library.Load(path).Duration == 1 && library.List(_ => { }).Count == 1);
            var backup = Directory.GetFiles(Path.Combine(library.Root, "Backups"), "*.sroute").Single();
            Assert(library.Load(backup).Duration == original.Duration);
            RejectIO(() => library.Replace(original, path, original));
            Assert(library.Load(path).Duration == 1);
            Assert(!Directory.EnumerateFiles(library.Root, "*.tmp", SearchOption.AllDirectories).Any());
        }));
        Test("Legacy migration copies routes and sharing files once, preserves originals and current settings", () => WithLibrary(library =>
        {
            string oldRoot = Path.Combine(library.Root, "old"); var old = new RouteLibrary(oldRoot);
            var route = Example(); string oldPath = old.Save(route); string oldExport = old.Export(route);
            File.Copy(oldPath, Path.Combine(old.Imports, "received.sroute"));
            Assert(LegacyMigration.CopyRoutes(oldRoot, library, _ => { }) == 1);
            Assert(File.Exists(oldPath) && library.List(_ => { }).Count == 1);
            Assert(File.Exists(Path.Combine(library.Imports, "received.sroute")));
            Assert(File.Exists(Path.Combine(library.Exports, Path.GetFileName(oldExport))));
            library.Delete(library.RouteFiles());
            Assert(LegacyMigration.CopyRoutes(oldRoot, library, _ => { }) == 0 && library.RouteFiles().Length == 0);
            string legacyConfig = Path.Combine(oldRoot, "old.cfg"), config = Path.Combine(library.Root, "new.cfg");
            File.WriteAllText(legacyConfig, "custom binding");
            Assert(LegacyMigration.CopyConfig(legacyConfig, config) && File.ReadAllText(config) == "custom binding");
            File.WriteAllText(config, "updated setting");
            Assert(!LegacyMigration.CopyConfig(legacyConfig, config) && File.ReadAllText(config) == "updated setting");
        }));
        Console.WriteLine($"PASS: {passed} tests.");
    }
    static Route Example()
    {
        var r = new Route { Name = "Café wall-jump", Map = "Arena_alt", GameVersion = "test", Bones = new[] { "graphics:0/body:0" } };
        r.Frames.Add(MakeFrame(0, 1)); r.Frames.Add(MakeFrame(1, 1)); r.Frames.Add(MakeFrame(2, 1));
        r.Frames[1].Cut = true; r.Frames[1].State = 13; return r;
    }
    static void RunBenchmarks()
    {
        var route = Example(); route.Id = new string('a', 32); route.CreatedUtc = "2026-09-05T00:00:00Z";
        route.Bones = Enumerable.Range(0, 96).Select(i => "bone/" + i).ToArray(); route.Frames.Clear();
        for (int i = 0; i <= 1800; i++)
        {
            var frame = MakeFrame(i / 30f, 96); frame.Pose[0] = i * .13f;
            for (int b = 0; b < 96; b++) { int j = 7 + b * 10; frame.Pose[j] = (float)Math.Sin(i * .03 + b) * .2f; frame.Pose[j + 1] = b * .01f; }
            route.Frames.Add(frame);
        }
        byte[] encoded = RouteCodec.Encode(route);
        Console.WriteLine("Fixture: 60s, 96 bones, 1801 frames; .NET " + Environment.Version + "; bytes=" + encoded.Length);
        Console.WriteLine("File SHA256=" + Convert.ToHexString(SHA256.HashData(encoded)));
        Bench("Encode", () => RouteCodec.Encode(route));
        Bench("Decode", () => RouteCodec.Decode(encoded));
        Bench("Trim", () => RouteEditor.Trim(route, 150, 1650));
    }
    static void Bench(string label, Func<object> action)
    {
        for (int i = 0; i < 3; i++) GC.KeepAlive(action());
        var elapsed = new double[7]; var allocated = new long[7];
        for (int i = 0; i < 7; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            object result = action(); watch.Stop();
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            elapsed[i] = watch.Elapsed.TotalMilliseconds; GC.KeepAlive(result);
        }
        Array.Sort(elapsed); Array.Sort(allocated);
        Console.WriteLine(label + ": median_ms=" + elapsed[3].ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "; allocated_bytes=" + allocated[3]);
    }
    static Frame MakeFrame(float time, int bones)
    {
        var f = new Frame { Time = time, Pose = new float[7 + bones * 10] }; f.Pose[6] = 1;
        for (int i = 0; i < bones; i++) { int j = 7+i*10; f.Pose[j+6] = 1; f.Pose[j+7] = f.Pose[j+8] = f.Pose[j+9] = 1; }
        return f;
    }
    static byte[] Unpack(byte[] file)
    {
        using var source = new MemoryStream(file, 40, file.Length - 40); using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var raw = new MemoryStream(); gzip.CopyTo(raw); return raw.ToArray();
    }
    static byte[] Pack(byte[] raw)
    {
        using var file = new MemoryStream(); using (var w = new BinaryWriter(file, System.Text.Encoding.UTF8, true))
        { w.Write(0x31475253); w.Write(raw.Length); w.Write(SHA256.HashData(raw)); }
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal, true)) gzip.Write(raw);
        return file.ToArray();
    }
    static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static void WithLibrary(Action<RouteLibrary> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "RouteRunnerTests-" + Guid.NewGuid().ToString("N"));
        try { test(new RouteLibrary(root)); }
        finally { Directory.Delete(root, true); }
    }
    static void RejectIO(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new Exception("Unsafe or stale file operation was accepted.");
    }
    static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    static void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException || e is FormatException || e is EndOfStreamException) { return; } throw new Exception("Invalid data was accepted."); }
}
