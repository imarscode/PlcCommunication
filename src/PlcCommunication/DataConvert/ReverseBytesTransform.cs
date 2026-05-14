using System;
using System.Text;

namespace PlcCommunication.DataConvert
{
    /// <summary>
    /// 大端字节序转换（网络顺序）。用于西门子S7和Modbus协议。
    /// </summary>
    public class ReverseBytesTransform : IByteTransform
    {
        public short TransInt16(byte[] buffer, int offset)
        {
            byte[] b = { buffer[offset + 1], buffer[offset] };
            return BitConverter.ToInt16(b, 0);
        }

        public ushort TransUInt16(byte[] buffer, int offset)
        {
            byte[] b = { buffer[offset + 1], buffer[offset] };
            return BitConverter.ToUInt16(b, 0);
        }

        public int TransInt32(byte[] buffer, int offset)
        {
            byte[] b = { buffer[offset + 3], buffer[offset + 2], buffer[offset + 1], buffer[offset] };
            return BitConverter.ToInt32(b, 0);
        }

        public uint TransUInt32(byte[] buffer, int offset)
        {
            byte[] b = { buffer[offset + 3], buffer[offset + 2], buffer[offset + 1], buffer[offset] };
            return BitConverter.ToUInt32(b, 0);
        }

        public long TransInt64(byte[] buffer, int offset)
        {
            byte[] b = new byte[8];
            for (int i = 0; i < 8; i++)
                b[i] = buffer[offset + 7 - i];
            return BitConverter.ToInt64(b, 0);
        }

        public ulong TransUInt64(byte[] buffer, int offset)
        {
            byte[] b = new byte[8];
            for (int i = 0; i < 8; i++)
                b[i] = buffer[offset + 7 - i];
            return BitConverter.ToUInt64(b, 0);
        }

        public float TransSingle(byte[] buffer, int offset)
        {
            byte[] b = { buffer[offset + 3], buffer[offset + 2], buffer[offset + 1], buffer[offset] };
            return BitConverter.ToSingle(b, 0);
        }

        public double TransDouble(byte[] buffer, int offset)
        {
            byte[] b = new byte[8];
            for (int i = 0; i < 8; i++)
                b[i] = buffer[offset + 7 - i];
            return BitConverter.ToDouble(b, 0);
        }

        public string TransString(byte[] buffer, int offset, int length, Encoding encoding)
        {
            return encoding.GetString(buffer, offset, length);
        }

        public byte[] GetBytes(short value)
        {
            byte[] b = BitConverter.GetBytes(value);
            Array.Reverse(b);
            return b;
        }

        public byte[] GetBytes(ushort value)
        {
            byte[] b = BitConverter.GetBytes(value);
            Array.Reverse(b);
            return b;
        }

        public byte[] GetBytes(int value)
        {
            byte[] b = BitConverter.GetBytes(value);
            Array.Reverse(b);
            return b;
        }

        public byte[] GetBytes(uint value)
        {
            byte[] b = BitConverter.GetBytes(value);
            Array.Reverse(b);
            return b;
        }

        public byte[] GetBytes(long value)
        {
            byte[] b = BitConverter.GetBytes(value);
            Array.Reverse(b);
            return b;
        }

        public byte[] GetBytes(ulong value)
        {
            byte[] b = BitConverter.GetBytes(value);
            Array.Reverse(b);
            return b;
        }

        public byte[] GetBytes(float value)
        {
            byte[] b = BitConverter.GetBytes(value);
            Array.Reverse(b);
            return b;
        }

        public byte[] GetBytes(double value)
        {
            byte[] b = BitConverter.GetBytes(value);
            Array.Reverse(b);
            return b;
        }
    }
}
