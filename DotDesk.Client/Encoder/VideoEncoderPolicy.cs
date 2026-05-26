using System;

namespace DotDesk.Client.Encoder
{
    public static class VideoEncoderPolicy
    {
        public static int CalculateGopSize(int targetFps, VideoConnectionMode mode)
        {
            int fps = Math.Clamp(targetFps, 1, 60);
            int multiplier = mode == VideoConnectionMode.Relay ? 10 : 5;

            int min = fps * (mode == VideoConnectionMode.Relay ? 8 : 4);
            int max = fps * (mode == VideoConnectionMode.Relay ? 12 : 6);
            int preferred = fps * multiplier;

            return Math.Clamp(preferred, Math.Max(min, 8), Math.Max(max, 16));
        }

        public static int CalculateMinKeyFrameInterval(int targetFps, VideoConnectionMode mode)
        {
            int fps = Math.Clamp(targetFps, 1, 60);
            return mode == VideoConnectionMode.Relay
                ? Math.Max(fps * 6, 24)
                : Math.Max(fps * 3, 16);
        }
    }
}
