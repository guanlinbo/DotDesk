using System;
using System.Diagnostics;
using System.IO;

namespace DotDesk.Client.Encoder
{
    public static class H264EncoderTest
    {
        public static void Run(int width = 1280, int height = 720, int fps = 30, int frames = 90)
        {
            Console.WriteLine($"=== H264 编码器测试（Sdcb.FFmpeg）===");
            Console.WriteLine($"分辨率: {width}x{height}  帧率: {fps}fps  帧数: {frames}");

            var outputPath = "test_output.h264";
            int nalCount = 0;
            int totalBytes = 0;

            using var fs = new FileStream(outputPath, FileMode.Create);
            using var enc = new H264Encoder(width, height, fps, bitrate: 2_000_000);

            enc.OnEncoded += (nal, isKey, pts) =>
            {
                nalCount++;
                totalBytes += nal.Length;
                fs.Write(nal, 0, nal.Length);
                if (isKey)
                    Console.WriteLine($"  [IDR] pts={pts}ms  size={nal.Length}B");
            };

            var bgra = new byte[width * height * 4];
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < frames; i++)
            {
                byte r = (byte)(i * 255 / frames);
                byte g = (byte)(255 - r);
                byte b = 128;

                for (int p = 0; p < width * height; p++)
                {
                    bgra[p * 4 + 0] = b;
                    bgra[p * 4 + 1] = g;
                    bgra[p * 4 + 2] = r;
                    bgra[p * 4 + 3] = 255;
                }

                enc.Encode(bgra);
            }

            enc.Flush();
            sw.Stop();

            Console.WriteLine($"\n编码完成：");
            Console.WriteLine($"  耗时    : {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  平均速度: {frames * 1000.0 / sw.ElapsedMilliseconds:F1} fps");
            Console.WriteLine($"  NAL 数量: {nalCount}");
            Console.WriteLine($"  总大小  : {totalBytes / 1024} KB");
            Console.WriteLine($"  输出文件: {Path.GetFullPath(outputPath)}");
            Console.WriteLine($"\n用 VLC 播放验证: vlc {outputPath}");
        }
    }
}