using System;
using System.Collections.Generic;

namespace RouteRunner.Core
{
    public sealed class RouteSummary
    {
        public string Id, Name, Map, FilePath;
        public float Duration;
    }
    public sealed class Route
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Practice route";
        public string Map = "";
        public string GameVersion = "";
        public string CreatedUtc = DateTime.UtcNow.ToString("O");
        public int SampleRate = 30;
        public string[] Bones = Array.Empty<string>();
        public readonly List<Frame> Frames = new List<Frame>();
        public float Duration => Frames.Count == 0 ? 0 : Frames[Frames.Count - 1].Time;
    }

    public sealed class Frame
    {
        public float Time;
        // Root world position + quaternion; then flat root-relative bone position/rotation and world scale.
        public float[] Pose;
        public byte State;
        public bool Cut;
    }

    public static class Timeline
    {
        public static int FindFrame(IReadOnlyList<Frame> frames, float time)
        {
            int lo = 0, hi = frames.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (frames[mid].Time <= time) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public static float Blend(Frame a, Frame b, float time)
        {
            if (b.Cut || b.Time <= a.Time) return 0;
            return Math.Max(0, Math.Min(1, (time - a.Time) / (b.Time - a.Time)));
        }
    }
}
