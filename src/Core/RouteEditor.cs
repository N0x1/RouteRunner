using System;

namespace RouteRunner.Core
{
    public static class RouteEditor
    {
        // Snap to recorded samples: keeps exact poses, cuts and movement state without inventing frames.
        public static Route Trim(Route source, int first, int last)
        {
            RouteCodec.Validate(source);
            if (first < 0 || last >= source.Frames.Count || last <= first)
                throw new ArgumentException("Keep at least two recorded samples.");
            var result = new Route
            {
                Id = source.Id,
                Name = source.Name,
                Map = source.Map,
                GameVersion = source.GameVersion,
                CreatedUtc = source.CreatedUtc,
                SampleRate = source.SampleRate,
                Bones = (string[])source.Bones.Clone()
            };
            float origin = source.Frames[first].Time;
            result.Frames.Capacity = last - first + 1;
            for (int i = first; i <= last; i++)
            {
                var frame = source.Frames[i];
                result.Frames.Add(new Frame
                {
                    Time = frame.Time - origin,
                    Pose = (float[])frame.Pose.Clone(),
                    State = frame.State,
                    Cut = i != first && frame.Cut
                });
            }
            RouteCodec.Validate(result);
            return result;
        }
    }
}
