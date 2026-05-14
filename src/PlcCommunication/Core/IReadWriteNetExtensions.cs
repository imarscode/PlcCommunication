using System;
using System.Text;
using System.Threading.Tasks;
using PlcCommunication.DataConvert;

namespace PlcCommunication.Core
{
    /// <summary>
    /// 在任何 <see cref="IReadWriteNet"/> 上提供类型化读/写操作的扩展方法。
    /// 这些方法使用设备的 <see cref="IByteTransform"/> 进行数据转换。
    /// </summary>
    public static class IReadWriteNetExtensions
    {
        // ---- Int16 ----

        public static async Task<OperateResult<short>> ReadInt16Async(this IReadWriteNet device, string address)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, 2);
            if (!read.IsSuccess)
                return OperateResult.Fail<short>(read.Message, read.ErrorCode);

            return ByteTransformHelper.TransInt16(GetTransform(device), read.Content, 0);
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, short value)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            byte[] data = GetTransform(device).GetBytes(value);
            return await device.WriteAsync(address, data);
        }

        // ---- UInt16 ----

        public static async Task<OperateResult<ushort>> ReadUInt16Async(this IReadWriteNet device, string address)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, 2);
            if (!read.IsSuccess)
                return OperateResult.Fail<ushort>(read.Message, read.ErrorCode);
            return ByteTransformHelper.TransUInt16(GetTransform(device), read.Content, 0);
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, ushort value)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            byte[] data = GetTransform(device).GetBytes(value);
            return await device.WriteAsync(address, data);
        }

        // ---- Int32 ----

        public static async Task<OperateResult<int>> ReadInt32Async(this IReadWriteNet device, string address)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, 4);
            if (!read.IsSuccess)
                return OperateResult.Fail<int>(read.Message, read.ErrorCode);
            return ByteTransformHelper.TransInt32(GetTransform(device), read.Content, 0);
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, int value)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            byte[] data = GetTransform(device).GetBytes(value);
            return await device.WriteAsync(address, data);
        }

        // ---- UInt32 ----

        public static async Task<OperateResult<uint>> ReadUInt32Async(this IReadWriteNet device, string address)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, 4);
            if (!read.IsSuccess)
                return OperateResult.Fail<uint>(read.Message, read.ErrorCode);
            return ByteTransformHelper.TransUInt32(GetTransform(device), read.Content, 0);
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, uint value)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            byte[] data = GetTransform(device).GetBytes(value);
            return await device.WriteAsync(address, data);
        }

        // ---- Int64 ----

        public static async Task<OperateResult<long>> ReadInt64Async(this IReadWriteNet device, string address)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, 8);
            if (!read.IsSuccess)
                return OperateResult.Fail<long>(read.Message, read.ErrorCode);

            try
            {
                return OperateResult.Success(GetTransform(device).TransInt64(read.Content, 0));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<long>("Int64 conversion failed", ex);
            }
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, long value)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            byte[] data = GetTransform(device).GetBytes(value);
            return await device.WriteAsync(address, data);
        }

        // ---- UInt64 ----

        public static async Task<OperateResult<ulong>> ReadUInt64Async(this IReadWriteNet device, string address)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, 8);
            if (!read.IsSuccess)
                return OperateResult.Fail<ulong>(read.Message, read.ErrorCode);

            try
            {
                return OperateResult.Success(GetTransform(device).TransUInt64(read.Content, 0));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<ulong>("UInt64 conversion failed", ex);
            }
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, ulong value)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            byte[] data = GetTransform(device).GetBytes(value);
            return await device.WriteAsync(address, data);
        }

        // ---- Float ----

        public static async Task<OperateResult<float>> ReadFloatAsync(this IReadWriteNet device, string address)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, 4);
            if (!read.IsSuccess)
                return OperateResult.Fail<float>(read.Message, read.ErrorCode);
            return ByteTransformHelper.TransSingle(GetTransform(device), read.Content, 0);
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, float value)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            byte[] data = GetTransform(device).GetBytes(value);
            return await device.WriteAsync(address, data);
        }

        // ---- Double ----

        public static async Task<OperateResult<double>> ReadDoubleAsync(this IReadWriteNet device, string address)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, 8);
            if (!read.IsSuccess)
                return OperateResult.Fail<double>(read.Message, read.ErrorCode);

            try
            {
                return OperateResult.Success(GetTransform(device).TransDouble(read.Content, 0));
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<double>("Double conversion failed", ex);
            }
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, double value)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            byte[] data = GetTransform(device).GetBytes(value);
            return await device.WriteAsync(address, data);
        }

        // ---- String ----

        public static async Task<OperateResult<string>> ReadStringAsync(this IReadWriteNet device, string address, ushort length, Encoding? encoding = null)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            encoding ??= Encoding.ASCII;
            var read = await device.ReadAsync(address, length);
            if (!read.IsSuccess)
                return OperateResult.Fail<string>(read.Message, read.ErrorCode);
            return ByteTransformHelper.TransString(GetTransform(device), read.Content, 0, length, encoding);
        }

        public static async Task<OperateResult> WriteAsync(this IReadWriteNet device, string address, string value, Encoding? encoding = null)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            encoding ??= Encoding.ASCII;
            byte[] data = encoding.GetBytes(value ?? string.Empty);
            return await device.WriteAsync(address, data);
        }

        // ---- Bool 数组 ----

        public static async Task<OperateResult<bool[]>> ReadBoolArrayAsync(this IReadWriteNet device, string address, ushort length)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var read = await device.ReadAsync(address, length);
            if (!read.IsSuccess)
                return OperateResult.Fail<bool[]>(read.Message, read.ErrorCode);

            bool[] result = new bool[read.Content.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = read.Content[i] != 0;
            }
            return OperateResult.Success(result);
        }

        // ---- 辅助方法 ----

        private static IByteTransform GetTransform(IReadWriteNet device)
        {
            if (device is NetworkDeviceBase baseDevice)
                return baseDevice.ByteTransform;
            throw new PlcCommunicationException(
                "Typed read/write requires the device to inherit from NetworkDeviceBase.");
        }
    }
}
