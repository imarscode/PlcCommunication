using System;
using PlcCommunication.Core;

namespace PlcCommunication.DataConvert
{
    /// <summary>
    /// 辅助方法，将 <see cref="IByteTransform"/> 操作与
    /// <see cref="OperateResult{T}"/> 返回模式结合。
    /// </summary>
    public static class ByteTransformHelper
    {
        public static OperateResult<short> TransInt16(IByteTransform transform, byte[] buffer, int offset)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<short>("Buffer is null");
                if (offset + 2 > buffer.Length)
                    return OperateResult.Fail<short>("Buffer too short for Int16");
                return OperateResult.Success(transform.TransInt16(buffer, offset));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<short>("Int16 conversion failed", ex);
            }
        }

        public static OperateResult<ushort> TransUInt16(IByteTransform transform, byte[] buffer, int offset)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<ushort>("Buffer is null");
                if (offset + 2 > buffer.Length)
                    return OperateResult.Fail<ushort>("Buffer too short for UInt16");
                return OperateResult.Success(transform.TransUInt16(buffer, offset));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<ushort>("UInt16 conversion failed", ex);
            }
        }

        public static OperateResult<int> TransInt32(IByteTransform transform, byte[] buffer, int offset)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<int>("Buffer is null");
                if (offset + 4 > buffer.Length)
                    return OperateResult.Fail<int>("Buffer too short for Int32");
                return OperateResult.Success(transform.TransInt32(buffer, offset));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<int>("Int32 conversion failed", ex);
            }
        }

        public static OperateResult<uint> TransUInt32(IByteTransform transform, byte[] buffer, int offset)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<uint>("Buffer is null");
                if (offset + 4 > buffer.Length)
                    return OperateResult.Fail<uint>("Buffer too short for UInt32");
                return OperateResult.Success(transform.TransUInt32(buffer, offset));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<uint>("UInt32 conversion failed", ex);
            }
        }

        public static OperateResult<long> TransInt64(IByteTransform transform, byte[] buffer, int offset)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<long>("Buffer is null");
                if (offset + 8 > buffer.Length)
                    return OperateResult.Fail<long>("Buffer too short for Int64");
                return OperateResult.Success(transform.TransInt64(buffer, offset));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<long>("Int64 conversion failed", ex);
            }
        }

        public static OperateResult<ulong> TransUInt64(IByteTransform transform, byte[] buffer, int offset)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<ulong>("Buffer is null");
                if (offset + 8 > buffer.Length)
                    return OperateResult.Fail<ulong>("Buffer too short for UInt64");
                return OperateResult.Success(transform.TransUInt64(buffer, offset));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<ulong>("UInt64 conversion failed", ex);
            }
        }

        public static OperateResult<double> TransDouble(IByteTransform transform, byte[] buffer, int offset)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<double>("Buffer is null");
                if (offset + 8 > buffer.Length)
                    return OperateResult.Fail<double>("Buffer too short for Double");
                return OperateResult.Success(transform.TransDouble(buffer, offset));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<double>("Double conversion failed", ex);
            }
        }

        public static OperateResult<float> TransSingle(IByteTransform transform, byte[] buffer, int offset)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<float>("Buffer is null");
                if (offset + 4 > buffer.Length)
                    return OperateResult.Fail<float>("Buffer too short for Single");
                return OperateResult.Success(transform.TransSingle(buffer, offset));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<float>("Single conversion failed", ex);
            }
        }

        public static OperateResult<string> TransString(IByteTransform transform, byte[] buffer, int offset, int length, System.Text.Encoding encoding)
        {
            try
            {
                if (buffer == null)
                    return OperateResult.Fail<string>("Buffer is null");
                if (offset + length > buffer.Length)
                    return OperateResult.Fail<string>("Buffer too short for string");
                return OperateResult.Success(transform.TransString(buffer, offset, length, encoding));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<string>("String conversion failed", ex);
            }
        }
    }
}
