namespace RouteRunner.Core
{
    public static class HexColour
    {
        // RGB/RGBA, shorthand or full length. The caller retains opacity for RGB-only input.
        public static bool TryParse(string text, out uint rgba, out bool includesAlpha)
        {
            rgba = 0; includesAlpha = false;
            if (text == null) return false;
            text = text.Trim();
            int start = text.StartsWith("#", System.StringComparison.Ordinal) ? 1 : 0;
            int length = text.Length - start;
            if (length != 3 && length != 4 && length != 6 && length != 8) return false;
            uint result = 0;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (digit < 0) return false;
                result = length <= 4 ? (result << 8) | (uint)(digit * 17) : (result << 4) | (uint)digit;
            }
            includesAlpha = length == 4 || length == 8;
            rgba = includesAlpha ? result : (result << 8) | 255;
            return true;
        }
    }
}
