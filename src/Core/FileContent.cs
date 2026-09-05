using System.IO;

namespace RouteRunner.Core
{
    public static class FileContent
    {
        public static bool Matches(string file, byte[] bytes)
        {
            using (var input = File.OpenRead(file))
            using (var expected = new MemoryStream(bytes, false)) return Matches(input, expected);
        }
        public static bool Matches(string first, string second)
        {
            using (var a = File.OpenRead(first))
            using (var b = File.OpenRead(second)) return Matches(a, b);
        }
        static bool Matches(Stream a, Stream b)
        {
            if (a.Length != b.Length) return false;
            var left = new byte[8192]; var right = new byte[8192];
            int read;
            while ((read = a.Read(left, 0, left.Length)) > 0)
            {
                int offset = 0;
                while (offset < read)
                {
                    int count = b.Read(right, offset, read - offset);
                    if (count == 0) return false;
                    offset += count;
                }
                for (int i = 0; i < read; i++) if (left[i] != right[i]) return false;
            }
            return b.ReadByte() == -1;
        }
    }
}
