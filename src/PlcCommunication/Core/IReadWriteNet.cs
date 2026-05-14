using System.Threading.Tasks;

namespace PlcCommunication.Core
{
    /// <summary>
    /// 所有 PLC 通信客户端的核心契约。
    /// 实现者提供原始字节级别的读/写和连接生命周期。
    /// 类型化的便捷方法（ReadInt32Async、WriteFloatAsync 等）
    /// 作为 <see cref="IReadWriteNetExtensions"/> 中的扩展方法提供。
    /// </summary>
    public interface IReadWriteNet
    {
        // ---- 连接 ----

        /// <summary>连接到 PLC 设备。</summary>
        Task<OperateResult> ConnectAsync();

        /// <summary>断开与 PLC 设备的连接。</summary>
        Task<OperateResult> DisconnectAsync();

        /// <summary>设备当前是否已连接。</summary>
        bool IsConnected { get; }

        // ---- 字节级操作 ----

        /// <summary>从指定地址读取原始字节。</summary>
        /// <param name="address">协议特定的地址字符串。</param>
        /// <param name="length">要读取的字节数。</param>
        Task<OperateResult<byte[]>> ReadAsync(string address, ushort length);

        /// <summary>将原始字节写入指定地址。</summary>
        Task<OperateResult> WriteAsync(string address, byte[] data);

        // ---- 位级操作 ----

        /// <summary>从位地址读取单个布尔值。</summary>
        Task<OperateResult<bool>> ReadBoolAsync(string address);

        /// <summary>将单个布尔值写入位地址。</summary>
        Task<OperateResult> WriteAsync(string address, bool value);
    }
}
