using System;

namespace RouteRunner.Core
{
    public static class RouteContent
    {
        // Both inputs come from validated routes. Compare stored values, including signed zero, without recompressing them.
        public static bool Matches(Route a, Route b)
        {
            if (a == null || b == null || a.Id != b.Id || a.Name != b.Name || a.Map != b.Map ||
                a.GameVersion != b.GameVersion || a.CreatedUtc != b.CreatedUtc || a.SampleRate != b.SampleRate ||
                a.Bones.Length != b.Bones.Length || a.Frames.Count != b.Frames.Count) return false;
            for (int i = 0; i < a.Bones.Length; i++) if (a.Bones[i] != b.Bones[i]) return false;
            for (int i = 0; i < a.Frames.Count; i++)
            {
                var x = a.Frames[i]; var y = b.Frames[i];
                if (BitConverter.SingleToInt32Bits(x.Time) != BitConverter.SingleToInt32Bits(y.Time) ||
                    x.State != y.State || x.Cut != y.Cut || x.Pose.Length != y.Pose.Length) return false;
                for (int j = 0; j < x.Pose.Length; j++)
                    if (BitConverter.SingleToInt32Bits(x.Pose[j]) != BitConverter.SingleToInt32Bits(y.Pose[j])) return false;
            }
            return true;
        }
    }
}
