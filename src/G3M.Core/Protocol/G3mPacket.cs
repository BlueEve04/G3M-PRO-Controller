namespace G3M.Core.Protocol;

/// <summary>
/// 64 字节报文的构造与校验。
/// 布局：<c>[0]=0x04</c>，<c>[1..2]=小端16位校验和</c>，
/// <c>[3]=命令</c>，<c>[4]=长度</c>，<c>[5..6]=小端16位偏移</c>，<c>[8..]=载荷</c>。
/// 校验和是 <c>[3..63]</c> 的无进位 16 位求和。
/// </summary>
public static class G3mPacket
{
    /// <summary>按协议构造一个 64 字节报文。</summary>
    /// <param name="command">命令字。</param>
    /// <param name="length">载荷长度，写入报文第 4 字节。</param>
    /// <param name="offset">载荷偏移，写入报文第 5、6 字节（小端）。</param>
    /// <param name="payload">载荷内容，超出 <paramref name="length"/> 的部分会被截断。</param>
    public static byte[] Build(
        byte command, int length = 0, int offset = 0, ReadOnlySpan<byte> payload = default)
    {
        if (length is < 0 or > G3mCommands.ReportSize - G3mCommands.PayloadOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, "载荷长度必须在 0 到 56 之间");
        }

        if (offset is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset), offset, "偏移必须在 0 到 65535 之间");
        }

        var packet = new byte[G3mCommands.ReportSize];
        packet[0] = G3mCommands.ReportType;
        packet[3] = command;
        packet[4] = (byte)length;
        packet[5] = (byte)(offset & 0xFF);
        packet[6] = (byte)((offset >> 8) & 0xFF);

        if (length > 0 && !payload.IsEmpty)
        {
            int copy = Math.Min(length, payload.Length);
            payload[..copy].CopyTo(packet.AsSpan(G3mCommands.PayloadOffset));
        }

        ushort checksum = ComputeChecksum(packet);
        packet[1] = (byte)(checksum & 0xFF);
        packet[2] = (byte)(checksum >> 8);
        return packet;
    }

    /// <summary>计算报文 <c>[3..63]</c> 的无进位 16 位求和。</summary>
    public static ushort ComputeChecksum(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < G3mCommands.ReportSize)
        {
            throw new ArgumentException("报文长度不足 64 字节", nameof(packet));
        }

        ushort checksum = 0;
        for (int i = 3; i < G3mCommands.ReportSize; i++)
        {
            checksum = (ushort)(checksum + packet[i]);
        }

        return checksum;
    }

    /// <summary>校验报文第 1、2 字节是否与重算结果一致。</summary>
    public static bool VerifyChecksum(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < G3mCommands.ReportSize)
        {
            return false;
        }

        ushort expected = ComputeChecksum(packet);
        return packet[1] == (byte)(expected & 0xFF) && packet[2] == (byte)(expected >> 8);
    }
}
