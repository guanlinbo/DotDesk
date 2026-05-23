using System;
using System.Collections.Generic;
using System.IO;

namespace DotDesk.Controller.Network
{
    /// <summary>
    /// 将 RTP H264 payload 还原为 Annex-B NAL。
    /// WebRTC 收到的 H264 经常是 FU-A 分片或 STAP-A 聚合包，不能直接丢给 FFmpeg。
    /// </summary>
    public sealed class H264RtpDepacketizer
    {
        private readonly MemoryStream _fuBuffer = new();
        private bool _assemblingFu;

        public IEnumerable<byte[]> Depacketize(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                yield break;

            if (HasStartCode(payload))
            {
                yield return payload;
                yield break;
            }

            int nalType = payload[0] & 0x1F;

            // 单 NAL 包：1-23。
            if (nalType > 0 && nalType < 24)
            {
                yield return ToAnnexB(payload, 0, payload.Length);
                yield break;
            }

            // STAP-A：一个 RTP payload 内聚合多个 NAL。
            if (nalType == 24)
            {
                int offset = 1;
                while (offset + 2 <= payload.Length)
                {
                    int size = (payload[offset] << 8) | payload[offset + 1];
                    offset += 2;
                    if (size <= 0 || offset + size > payload.Length)
                        yield break;

                    yield return ToAnnexB(payload, offset, size);
                    offset += size;
                }

                yield break;
            }

            // FU-A：大 NAL 分片。只有 end 分片到达时返回完整 NAL。
            if (nalType == 28 && payload.Length >= 2)
            {
                byte fuIndicator = payload[0];
                byte fuHeader = payload[1];
                bool start = (fuHeader & 0x80) != 0;
                bool end = (fuHeader & 0x40) != 0;
                byte originalNalHeader = (byte)((fuIndicator & 0xE0) | (fuHeader & 0x1F));

                if (start)
                {
                    _fuBuffer.SetLength(0);
                    WriteStartCode(_fuBuffer);
                    _fuBuffer.WriteByte(originalNalHeader);
                    _fuBuffer.Write(payload, 2, payload.Length - 2);
                    _assemblingFu = true;
                    yield break;
                }

                if (!_assemblingFu)
                    yield break;

                _fuBuffer.Write(payload, 2, payload.Length - 2);
                if (end)
                {
                    _assemblingFu = false;
                    yield return _fuBuffer.ToArray();
                    _fuBuffer.SetLength(0);
                }

                yield break;
            }
        }

        public void Reset()
        {
            _assemblingFu = false;
            _fuBuffer.SetLength(0);
        }

        private static byte[] ToAnnexB(byte[] payload, int offset, int count)
        {
            var output = new byte[count + 4];
            output[0] = 0;
            output[1] = 0;
            output[2] = 0;
            output[3] = 1;
            Buffer.BlockCopy(payload, offset, output, 4, count);
            return output;
        }

        private static void WriteStartCode(Stream stream)
        {
            stream.WriteByte(0);
            stream.WriteByte(0);
            stream.WriteByte(0);
            stream.WriteByte(1);
        }

        private static bool HasStartCode(byte[] data)
        {
            if (data.Length >= 4 &&
                data[0] == 0 && data[1] == 0 &&
                data[2] == 0 && data[3] == 1)
                return true;

            return data.Length >= 3 &&
                   data[0] == 0 && data[1] == 0 &&
                   data[2] == 1;
        }
    }
}
