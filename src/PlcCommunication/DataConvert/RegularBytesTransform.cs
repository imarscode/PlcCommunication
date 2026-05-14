using System;
using System.Text;

namespace PlcCommunication.DataConvert
{
    /// <summary>
    /// 小端字节序转换（Intel顺序）。用于三菱MC和罗克韦尔CIP协议。
    /// </summary>
    public class RegularBytesTransform : IByteTransform
    {
        public short TransInt16(byte[] buffer, int offset)
        {
            return BitConverter.ToInt16(buffer, offset);
        }

        public ushort TransUInt16(byte[] buffer, int offset)
        {
            return BitConverter.ToUInt16(buffer, offset);
        }

        public int TransInt32(byte[] buffer, int offset)
        {
            return BitConverter.ToInt32(buffer, offset);
        }

        public uint TransUInt32(byte[] buffer, int offset)
        {
            return BitConverter.ToUInt32(buffer, offset);
        }

        public long TransInt64(byte[] buffer, int offset)
        {
            return BitConverter.ToInt64(buffer, offset);
        }

        public ulong TransUInt64(byte[] buffer, int offset)
        {
            return BitConverter.ToUInt64(buffer, offset);
        }

        public float TransSingle(byte[] buffer, int offset)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        public double TransDouble(byte[] buffer, int offset)
        {
            return BitConverter.ToDouble(buffer, offset);
        }

        public string TransString(byte[] buffer, int offset, int length, Encoding encoding)
        {
            return encoding.GetString(buffer, offset, length);
        }

        public byte[] GetBytes(short value)
        {
            return BitConverter.GetBytes(value);
        }

        public byte[] GetBytes(ushort value)
        {
            return BitConverter.GetBytes(value);
        }

        public byte[] GetBytes(int value)
        {
            return BitConverter.GetBytes(value);
        }

        public byte[] GetBytes(uint value)
        {
            return BitConverter.GetBytes(value);
        }

        public byte[] GetBytes(long value)
        {
            return BitConverter.GetBytes(value);
        }

        public byte[] GetBytes(ulong value)
        {
            return BitConverter.GetBytes(value);
        }

        public byte[] GetBytes(float value)
        {
            return BitConverter.GetBytes(value);
        }

        public byte[] GetBytes(double value)
        {
            return BitConverter.GetBytes(value);
        }
    }
}
