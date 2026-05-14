using System;
using System.Threading.Tasks;
using PlcCommunication.Core;
using PlcCommunication.DataConvert;

namespace PlcCommunication.Protocols.Modbus
{
    /// <summary>
    /// Modbus TCP 客户端。通过 TCP/IP 与 Modbus 设备通信。
    /// 支持功能码 0x01-0x10 读写线圈和寄存器。
    /// </summary>
    public class ModbusTcpNet : NetworkDeviceBase
    {
        private ushort _transactionId = 0;

        /// <summary>Modbus 单元/从站标识符。默认为 1。</summary>
        public byte StationId { get; set; } = 1;

        /// <summary>
        /// 创建新的 Modbus TCP 客户端。
        /// </summary>
        /// <param name="ipAddress">PLC IP 地址或主机名。</param>
        /// <param name="port">TCP 端口号（默认为 502）。</param>
        /// <param name="stationId">单元 ID（默认为 1）。</param>
        public ModbusTcpNet(string ipAddress, int port = 502, byte stationId = 1)
        {
            IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            StationId = stationId;
            ByteTransform = new ReverseBytesTransform();
            ResponseHeaderLength = 6; // MBAP 头：2 字节事务ID + 2 字节协议ID + 2 字节长度
        }

        /// <summary>
        /// 使用默认设置（127.0.0.1:502）创建新的 Modbus TCP 客户端。
        /// </summary>
        public ModbusTcpNet() : this("127.0.0.1") { }

        /// <inheritdoc/>
        protected override byte[] BuildReadCommand(string address, ushort length)
        {
            ushort startAddress = ModbusHelper.ParseAddress(address);
            _transactionId++;

            byte[] command = new byte[12];
            // MBAP 头
            command[0] = (byte)((_transactionId >> 8) & 0xFF);
            command[1] = (byte)(_transactionId & 0xFF);
            command[2] = 0; // 协议 ID 高字节
            command[3] = 0; // 协议 ID 低字节
            command[4] = 0; // 长度高字节（后续 6 字节）
            command[5] = 6; // 长度低字节
            command[6] = StationId;
            // ReadAsync 的 length 参数为字节数，Modbus 寄存器数量 = ceil(length / 2)
            int registerCount = (length + 1) / 2;
            if (registerCount < 1) registerCount = 1;

            command[7] = ModbusFunction.ReadHoldingRegisters;
            command[8] = (byte)((startAddress >> 8) & 0xFF);
            command[9] = (byte)(startAddress & 0xFF);
            command[10] = (byte)((registerCount >> 8) & 0xFF);
            command[11] = (byte)(registerCount & 0xFF);

            return command;
        }

        /// <inheritdoc/>
        protected override byte[] BuildWriteCommand(string address, byte[] data)
        {
            ushort startAddress = ModbusHelper.ParseAddress(address);
            _transactionId++;

            int byteCount = data.Length;
            int registerCount = (byteCount + 1) / 2;
            int paddedByteCount = registerCount * 2;

            // 将数据对齐到寄存器边界
            byte[] paddedData = new byte[paddedByteCount];
            Buffer.BlockCopy(data, 0, paddedData, 0, byteCount);

            byte[] command = new byte[13 + paddedByteCount];
            command[0] = (byte)((_transactionId >> 8) & 0xFF);
            command[1] = (byte)(_transactionId & 0xFF);
            command[2] = 0;
            command[3] = 0;
            int length = 7 + paddedByteCount;
            command[4] = (byte)((length >> 8) & 0xFF);
            command[5] = (byte)(length & 0xFF);
            command[6] = StationId;
            command[7] = ModbusFunction.WriteMultipleRegisters;
            command[8] = (byte)((startAddress >> 8) & 0xFF);
            command[9] = (byte)(startAddress & 0xFF);
            command[10] = (byte)((registerCount >> 8) & 0xFF);
            command[11] = (byte)(registerCount & 0xFF);
            command[12] = (byte)paddedByteCount;

            Buffer.BlockCopy(paddedData, 0, command, 13, paddedByteCount);
            return command;
        }

        /// <inheritdoc/>
        protected override OperateResult<byte[]> CheckResponse(byte[] command, byte[] response)
        {
            if (response.Length < 9)
                return OperateResult.Fail<byte[]>($"Response too short: {response.Length} bytes");

            // 验证 MBAP：协议 ID 必须为 0
            if (response[2] != 0 || response[3] != 0)
                return OperateResult.Fail<byte[]>("Invalid protocol ID in response");

            byte functionCode = response[7];

            // 检查是否为 Modbus 异常响应（功能码的最高位被置位）
            if ((functionCode & 0x80) != 0)
            {
                byte exceptionCode = response.Length > 8 ? response[8] : (byte)0;
                string errorMsg = GetModbusErrorMessage(exceptionCode);
                return OperateResult.Fail<byte[]>($"Modbus exception {exceptionCode}: {errorMsg}", -exceptionCode);
            }

            // 对于读取响应，数据从字节 9 开始（6 MBAP + 1 功能码 + 1 字节计数）
            if (command[7] == ModbusFunction.ReadHoldingRegisters ||
                command[7] == ModbusFunction.ReadInputRegisters)
            {
                if (response.Length < 10)
                    return OperateResult.Fail<byte[]>("Response too short for register data");

                byte byteCount = response[8];
                if (byteCount <= 0 || response.Length < 9 + byteCount)
                    return OperateResult.Fail<byte[]>($"Invalid byte count in response: {byteCount}");

                byte[] data = new byte[byteCount];
                Buffer.BlockCopy(response, 9, data, 0, byteCount);
                return OperateResult.Success(data);
            }

            // 对于写入响应（请求的回显），直接返回空成功
            return OperateResult.Success(Array.Empty<byte>());
        }

        /// <inheritdoc/>
        protected override int GetResponseLength(byte[] header)
        {
            if (header.Length < 6) return 0;
            int length = (header[4] << 8) | header[5];
            return length + 6; // 包含 MBAP 头
        }

        /// <inheritdoc/>
        public override async Task<OperateResult<bool>> ReadBoolAsync(string address)
        {
            // 读取单个线圈（功能码 0x01）
            ushort coilAddress = ModbusHelper.ParseAddress(address);
            _transactionId++;

            byte[] command = new byte[12];
            command[0] = (byte)((_transactionId >> 8) & 0xFF);
            command[1] = (byte)(_transactionId & 0xFF);
            command[2] = 0; command[3] = 0;
            command[4] = 0; command[5] = 6;
            command[6] = StationId;
            command[7] = ModbusFunction.ReadCoils;
            command[8] = (byte)((coilAddress >> 8) & 0xFF);
            command[9] = (byte)(coilAddress & 0xFF);
            command[10] = 0; command[11] = 1; // 数量 = 1

            var result = await ReadFromCoreServerAsync(command);
            if (!result.IsSuccess)
                return OperateResult.Fail<bool>(result.Message, result.ErrorCode);

            return result.Content.Length > 0
                ? OperateResult.Success(result.Content[0] != 0)
                : OperateResult.Fail<bool>("Empty response");
        }

        /// <inheritdoc/>
        public override async Task<OperateResult> WriteAsync(string address, bool value)
        {
            // 写入单个线圈（功能码 0x05）
            ushort coilAddress = ModbusHelper.ParseAddress(address);
            _transactionId++;

            byte[] command = new byte[12];
            command[0] = (byte)((_transactionId >> 8) & 0xFF);
            command[1] = (byte)(_transactionId & 0xFF);
            command[2] = 0; command[3] = 0;
            command[4] = 0; command[5] = 6;
            command[6] = StationId;
            command[7] = ModbusFunction.WriteSingleCoil;
            command[8] = (byte)((coilAddress >> 8) & 0xFF);
            command[9] = (byte)(coilAddress & 0xFF);
            command[10] = value ? (byte)0xFF : (byte)0x00;
            command[11] = 0x00;

            var result = await ReadFromCoreServerAsync(command);
            return result.IsSuccess
                ? OperateResult.Success()
                : OperateResult.Fail(result.Message, result.ErrorCode);
        }

        private static string GetModbusErrorMessage(byte code)
        {
            return code switch
            {
                0x01 => "Illegal Function",
                0x02 => "Illegal Data Address",
                0x03 => "Illegal Data Value",
                0x04 => "Slave Device Failure",
                0x05 => "Acknowledge",
                0x06 => "Slave Device Busy",
                0x08 => "Memory Parity Error",
                0x0A => "Gateway Path Unavailable",
                0x0B => "Gateway Target Device Failed to Respond",
                _ => $"Unknown error (0x{code:X2})"
            };
        }
    }
}
