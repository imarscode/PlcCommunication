using System.Text;

namespace PlcCommunication.DataConvert
{
    /// <summary>
    /// 定义基本类型与字节数组之间的字节序转换。
    /// 每种协议设置适当的实现（小端序或大端序）。
    /// </summary>
    public interface IByteTransform
    {
        short TransInt16(byte[] buffer, int offset);
        ushort TransUInt16(byte[] buffer, int offset);
        int TransInt32(byte[] buffer, int offset);
        uint TransUInt32(byte[] buffer, int offset);
        long TransInt64(byte[] buffer, int offset);
        ulong TransUInt64(byte[] buffer, int offset);
        float TransSingle(byte[] buffer, int offset);
        double TransDouble(byte[] buffer, int offset);
        string TransString(byte[] buffer, int offset, int length, Encoding encoding);

        byte[] GetBytes(short value);
        byte[] GetBytes(ushort value);
        byte[] GetBytes(int value);
        byte[] GetBytes(uint value);
        byte[] GetBytes(long value);
        byte[] GetBytes(ulong value);
        byte[] GetBytes(float value);
        byte[] GetBytes(double value);
    }
}
