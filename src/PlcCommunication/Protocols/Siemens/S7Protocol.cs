using System;

namespace PlcCommunication.Protocols.Siemens
{
    /// <summary>S7区域标识符，用于S7内存模型。</summary>
    public static class S7Area
    {
        public const byte ProcessInputs = 0x81;   // I（输入）
        public const byte ProcessOutputs = 0x82;  // Q（输出）
        public const byte Merkers = 0x83;         // M（中间继电器）
        public const byte DataBlocks = 0x84;      // DB（数据块）
        public const byte Timers = 0x1D;           // T（定时器，S7-300/400）
        public const byte Counters = 0x1C;         // C（计数器，S7-300/400）
    }

    /// <summary>S7传输大小标识符。</summary>
    public static class S7TransportSize
    {
        public const byte Bit = 0x01;
        public const byte Byte = 0x02;
        public const byte Char = 0x03;
        public const byte Word = 0x04;
        public const byte Int = 0x05;
        public const byte DWord = 0x06;
        public const byte DInt = 0x07;
        public const byte Real = 0x09;
        public const byte Counter = 0x1C;
        public const byte Timer = 0x1D;
    }

    /// <summary>S7功能码，用于PDU请求。</summary>
    public static class S7Function
    {
        public const byte ReadVar = 0x04;
        public const byte WriteVar = 0x05;
        public const byte RequestData = 0x00; // PDU协商
        public const byte DownloadStart = 0x1D;
        public const byte UploadStart = 0x1E;
        public const byte PLCStop = 0x28;
    }

    /// <summary>S7消息类型（ROSCTR）。</summary>
    public static class S7MessageType
    {
        public const byte Job = 0x01;         // 请求
        public const byte Ack = 0x02;         // 确认（无数据）
        public const byte AckData = 0x03;     // 带数据的确认
        public const byte UserData = 0x07;    // 用户数据
    }

    /// <summary>COTP数据包ID。</summary>
    public static class COTP
    {
        public const byte TPDU_CR = 0xE0;  // 连接请求
        public const byte TPDU_CC = 0xD0;  // 连接确认
        public const byte TPDU_DT = 0xF0;  // 数据传输

        /// <summary>构建用于ISO-on-TCP的COTP连接请求数据包。</summary>
        public static byte[] BuildConnectionRequest()
        {
            // 标准S7连接请求数据包（22字节）
            return new byte[]
            {
                0x03, 0x00, 0x00, 0x16,   // ISO 8073 + 长度（22）
                0x11,                       // CR（连接请求）
                0xE0, 0x00, 0x00,          // 目标引用，源引用
                0x00,                       // 类别
                0x01,                       // 选项
                0xC0, 0x01, 0x0A,          // TPDU大小 = 1024
                0xC1, 0x02, 0x01, 0x00,    // 源引用
                0xC2, 0x02, 0x01, 0x00     // 目标引用
            };
        }

        /// <summary>检查COTP响应是否为有效的连接确认。</summary>
        public static bool IsConnectionConfirm(byte[] data)
        {
            return data != null &&
                   data.Length >= 7 &&
                   data[0] == 0x03 &&
                   data[5] == 0xD0;
        }
    }

    /// <summary>
    /// 解析后的S7地址数据，用于构建读/写命令。
    /// </summary>
    internal struct S7AddressData
    {
        public byte Area;        // S7区域码（0x81, 0x82, 0x83, 0x84）
        public int DBNumber;     // DB块号（非DB区域为0）
        public int ByteOffset;   // 字节偏移量
        public byte BitOffset;   // 位偏移量（0-7）
        public byte TransportSize; // 传输大小

        public S7AddressData(byte area, int dbNumber, int byteOffset, byte bitOffset, byte transportSize)
        {
            Area = area;
            DBNumber = dbNumber;
            ByteOffset = byteOffset;
            BitOffset = bitOffset;
            TransportSize = transportSize;
        }
    }
}
