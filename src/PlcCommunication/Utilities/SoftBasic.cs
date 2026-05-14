using System;
using System.Collections.Generic;
using System.Text;

namespace PlcCommunication.Utilities
{
    /// <summary>
    /// 用于字节/十六进制/数组操作的通用工具方法。
    /// </summary>
    public static class SoftBasic
    {
        /// <summary>
        /// 将字节数组转换为十六进制字符串，可选择分隔符。
        /// </summary>
        public static string BytesToHexString(byte[] data, char separator = ' ')
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0 && separator != '\0')
                    sb.Append(separator);
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 将字节格式化为带有ASCII预览的十六进制转储（类似Wireshark）。
        /// </summary>
        public static string ByteToHexStringDump(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "(empty)";

            var sb = new StringBuilder();
            sb.AppendLine($"Hex dump ({data.Length} bytes):");

            int offset = 0;
            while (offset < data.Length)
            {
                sb.Append(offset.ToString("X4"));
                sb.Append(": ");

                // 十六进制部分
                int remaining = Math.Min(16, data.Length - offset);
                for (int i = 0; i < 16; i++)
                {
                    if (i < remaining)
                        sb.Append(data[offset + i].ToString("X2"));
                    else
                        sb.Append("  ");

                    if (i == 7)
                        sb.Append("  ");
                    else
                        sb.Append(' ');
                }

                sb.Append(" ");

                // ASCII 部分
                for (int i = 0; i < remaining; i++)
                {
                    byte b = data[offset + i];
                    sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }

                sb.AppendLine();
                offset += 16;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 将十六进制字符串转换为字节数组。接受"0x"前缀，可带或不带分隔符。
        /// </summary>
        public static byte[] HexStringToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return Array.Empty<byte>();

            hex = hex.Replace(" ", "").Replace("-", "");
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);

            if (hex.Length % 2 != 0)
                hex = "0" + hex;

            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return result;
        }

        /// <summary>拼接两个字节数组。</summary>
        public static byte[] SpliceArray(byte[] first, byte[] second)
        {
            if (first == null) return second ?? Array.Empty<byte>();
            if (second == null) return first ?? Array.Empty<byte>();

            byte[] result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        /// <summary>拼接多个字节数组。</summary>
        public static byte[] SpliceByteArray(params byte[][] arrays)
        {
            if (arrays == null || arrays.Length == 0)
                return Array.Empty<byte>();

            int totalLength = 0;
            for (int i = 0; i < arrays.Length; i++)
            {
                if (arrays[i] != null)
                    totalLength += arrays[i].Length;
            }

            byte[] result = new byte[totalLength];
            int offset = 0;
            for (int i = 0; i < arrays.Length; i++)
            {
                if (arrays[i] != null)
                {
                    Buffer.BlockCopy(arrays[i], 0, result, offset, arrays[i].Length);
                    offset += arrays[i].Length;
                }
            }
            return result;
        }

        /// <summary>安全地提取子数组。</summary>
        public static byte[] ArraySelect(byte[] array, int offset, int length)
        {
            if (array == null) return Array.Empty<byte>();
            if (offset < 0 || length <= 0 || offset + length > array.Length)
                return Array.Empty<byte>();

            byte[] result = new byte[length];
            Buffer.BlockCopy(array, offset, result, 0, length);
            return result;
        }

        /// <summary>将一个字节值转换为8个布尔位的数组。</summary>
        public static bool[] ByteToBoolArray(byte value)
        {
            bool[] result = new bool[8];
            for (int i = 0; i < 8; i++)
            {
                result[i] = ((value >> i) & 0x01) == 0x01;
            }
            return result;
        }

        /// <summary>将8个布尔位的数组转换为一个字节。</summary>
        public static byte BoolArrayToByte(bool[] values)
        {
            byte result = 0;
            for (int i = 0; i < 8 && i < values.Length; i++)
            {
                if (values[i])
                    result |= (byte)(1 << i);
            }
            return result;
        }
    }
}
