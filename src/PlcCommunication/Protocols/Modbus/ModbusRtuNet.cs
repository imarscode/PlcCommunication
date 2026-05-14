using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlcCommunication.Core;
using PlcCommunication.DataConvert;

namespace PlcCommunication.Protocols.Modbus
{
    /// <summary>
    /// Modbus RTU 客户端。使用 RTU 帧格式与 Modbus 设备通信。
    /// 设计用于通过 TCP（串口转以太网转换器）或任何流式传输。
    /// 消息使用 CRC-16 校验，无 MBAP 头。
    /// </summary>
    public class ModbusRtuNet : NetworkDeviceBase
    {
        /// <summary>Modbus 从站地址。默认为 1。</summary>
        public byte StationId { get; set; } = 1;

        /// <summary>
        /// 创建新的 Modbus RTU 客户端。
        /// </summary>
        public ModbusRtuNet(string ipAddress, int port = 502, byte stationId = 1)
        {
            IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            StationId = stationId;
            ByteTransform = new ReverseBytesTransform();
            // 无法从头部确定响应长度 — 我们重写了 ReceiveAsync
            ResponseHeaderLength = 1;
        }

        /// <summary>
        /// 使用默认设置创建。
        /// </summary>
        public ModbusRtuNet() : this("127.0.0.1") { }

        /// <inheritdoc/>
        protected override byte[] BuildReadCommand(string address, ushort length)
        {
            ushort startAddress = ModbusHelper.ParseAddress(address);

            // ReadAsync 的 length 参数为字节数，Modbus 寄存器数量 = ceil(length / 2)
            int registerCount = (length + 1) / 2;
            if (registerCount < 1) registerCount = 1;

            byte[] pdu = new byte[6];
            pdu[0] = StationId;
            pdu[1] = ModbusFunction.ReadHoldingRegisters;
            pdu[2] = (byte)((startAddress >> 8) & 0xFF);
            pdu[3] = (byte)(startAddress & 0xFF);
            pdu[4] = (byte)((registerCount >> 8) & 0xFF);
            pdu[5] = (byte)(registerCount & 0xFF);

            byte[] crc = ModbusHelper.CalculateCRC16(pdu);
            byte[] command = new byte[pdu.Length + crc.Length];
            Buffer.BlockCopy(pdu, 0, command, 0, pdu.Length);
            Buffer.BlockCopy(crc, 0, command, pdu.Length, crc.Length);

            return command;
        }

        /// <inheritdoc/>
        protected override byte[] BuildWriteCommand(string address, byte[] data)
        {
            ushort startAddress = ModbusHelper.ParseAddress(address);

            int byteCount = data.Length;
            int registerCount = (byteCount + 1) / 2;
            int paddedByteCount = registerCount * 2;

            byte[] paddedData = new byte[paddedByteCount];
            Buffer.BlockCopy(data, 0, paddedData, 0, byteCount);

            byte[] pdu = new byte[6 + paddedByteCount];
            pdu[0] = StationId;
            pdu[1] = ModbusFunction.WriteMultipleRegisters;
            pdu[2] = (byte)((startAddress >> 8) & 0xFF);
            pdu[3] = (byte)(startAddress & 0xFF);
            pdu[4] = (byte)((registerCount >> 8) & 0xFF);
            pdu[5] = (byte)(registerCount & 0xFF);

            Buffer.BlockCopy(paddedData, 0, pdu, 6, paddedByteCount);

            byte[] crc = ModbusHelper.CalculateCRC16(pdu);
            byte[] command = new byte[pdu.Length + crc.Length];
            Buffer.BlockCopy(pdu, 0, command, 0, pdu.Length);
            Buffer.BlockCopy(crc, 0, command, pdu.Length, crc.Length);

            return command;
        }

        /// <inheritdoc/>
        protected override OperateResult<byte[]> CheckResponse(byte[] command, byte[] response)
        {
            if (response == null || response.Length < 5)
                return OperateResult.Fail<byte[]>("Response too short for Modbus RTU");

            // 验证 CRC
            if (!ModbusHelper.VerifyCRC16(response))
                return OperateResult.Fail<byte[]>("CRC mismatch in response", -1002);

            // 检查功能码是否存在错误
            byte functionCode = response[1];
            if ((functionCode & 0x80) != 0)
            {
                byte exceptionCode = response.Length > 2 ? response[2] : (byte)0;
                return OperateResult.Fail<byte[]>($"Modbus exception {exceptionCode}", -exceptionCode);
            }

            // 提取数据（跳过地址 + 功能码）
            int dataOffset;
            int dataLength;

            if (functionCode == ModbusFunction.ReadHoldingRegisters ||
                functionCode == ModbusFunction.ReadInputRegisters ||
                functionCode == ModbusFunction.ReadCoils ||
                functionCode == ModbusFunction.ReadDiscreteInputs)
            {
                // 响应：地址(1) + 功能码(1) + 字节数(1) + 数据(N) + CRC(2)
                dataOffset = 3;
                dataLength = response.Length - 5; // Remove address + FC + count + CRC
            }
            else
            {
                // 写入响应：地址(1) + 功能码(1) + 起始地址(2) + 数量(2) + CRC(2)
                dataOffset = 2;
                dataLength = response.Length - 4; // Remove address + FC + CRC
            }

            if (dataLength < 0) dataLength = 0;
            byte[] result = new byte[dataLength];
            Buffer.BlockCopy(response, dataOffset, result, 0, dataLength);
            return OperateResult.Success(result);
        }

        /// <inheritdoc/>
        protected override async Task<OperateResult<byte[]>> ReceiveAsync(CancellationToken ct)
        {
            if (_stream == null)
                return OperateResult.Fail<byte[]>("Not connected");

            try
            {
                // 读取前 2 个字节：地址 + 功能码
                byte[] header = new byte[2];
                int read = await ReadStreamAsync(_stream, header, 0, 2, ct);
                if (read != 2)
                    return OperateResult.Fail<byte[]>($"Expected 2 header bytes, got {read}");

                byte functionCode = header[1];

                // 根据功能码确定剩余字节数
                int remaining;
                bool isError = (functionCode & 0x80) != 0;

                if (isError)
                {
                    remaining = 2; // 异常码(1) + CRC(2) = 功能码后共 3 字节，目前已读 0 字节
                    // 实际：已经读到 header = 地址 + 功能码。错误响应增加：异常码(1) + CRC(2)
                    // 地址之后总计：功能码 + 异常码 + CRC = 4 字节。已读 2 字节。剩余 = 2。
                    remaining = 2;
                }
                else if (functionCode == ModbusFunction.ReadCoils ||
                         functionCode == ModbusFunction.ReadDiscreteInputs ||
                         functionCode == ModbusFunction.ReadHoldingRegisters ||
                         functionCode == ModbusFunction.ReadInputRegisters)
                {
                    // 接下来读取字节数
                    byte[] bc = new byte[1];
                    read = await ReadStreamAsync(_stream, bc, 0, 1, ct);
                    if (read != 1)
                        return OperateResult.Fail<byte[]>("Failed to read byte count");

                    byte byteCount = bc[0];
                    remaining = byteCount + 2; // 数据 + CRC
                }
                else if (functionCode == ModbusFunction.WriteSingleCoil ||
                         functionCode == ModbusFunction.WriteSingleRegister)
                {
                    // 响应 = 地址(1) + 功能码(1) + 输出地址(2) + 输出值(2) + CRC(2) = 共 8 字节
                    // 已读地址(1) + 功能码(1) = 2 字节，剩余 = 6 字节
                    remaining = 6;
                }
                else
                {
                    // 写入多个：地址(2) + 数量(2) + CRC(2) = 6
                    remaining = 6;
                }

                // 读取剩余字节
                byte[] body = new byte[remaining];
                read = await ReadStreamAsync(_stream, body, 0, remaining, ct);
                if (read != remaining)
                    return OperateResult.Fail<byte[]>($"Expected {remaining} bytes, got {read}");

                // 合并数据
                byte[] fullResponse = new byte[2 + remaining];
                Buffer.BlockCopy(header, 0, fullResponse, 0, 2);
                Buffer.BlockCopy(body, 0, fullResponse, 2, remaining);
                return OperateResult.Success(fullResponse);
            }
            catch (OperationCanceledException)
            {
                return OperateResult.Fail<byte[]>("Receive timeout", -1001);
            }
            catch (Exception ex)
            {
                return OperateResult.Fail<byte[]>("Receive error", ex, -1000);
            }
        }

        /// <inheritdoc/>
        protected override int GetResponseLength(byte[] header)
        {
            return 0; // 未使用 — ReceiveAsync 已完全重写
        }

        /// <inheritdoc/>
        public override async Task<OperateResult<bool>> ReadBoolAsync(string address)
        {
            ushort coilAddress = ModbusHelper.ParseAddress(address);

            byte[] pdu = new byte[5];
            pdu[0] = StationId;
            pdu[1] = ModbusFunction.ReadCoils;
            pdu[2] = (byte)((coilAddress >> 8) & 0xFF);
            pdu[3] = (byte)(coilAddress & 0xFF);
            pdu[4] = 1; // 数量

            byte[] crc = ModbusHelper.CalculateCRC16(pdu);
            byte[] command = new byte[pdu.Length + crc.Length];
            Buffer.BlockCopy(pdu, 0, command, 0, pdu.Length);
            Buffer.BlockCopy(crc, 0, command, pdu.Length, crc.Length);

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
            ushort coilAddress = ModbusHelper.ParseAddress(address);

            byte[] pdu = new byte[6];
            pdu[0] = StationId;
            pdu[1] = ModbusFunction.WriteSingleCoil;
            pdu[2] = (byte)((coilAddress >> 8) & 0xFF);
            pdu[3] = (byte)(coilAddress & 0xFF);
            pdu[4] = value ? (byte)0xFF : (byte)0x00;
            pdu[5] = 0x00;

            byte[] crc = ModbusHelper.CalculateCRC16(pdu);

            byte[] command = new byte[pdu.Length + crc.Length];
            Buffer.BlockCopy(pdu, 0, command, 0, pdu.Length);
            Buffer.BlockCopy(crc, 0, command, pdu.Length, crc.Length);

            var result = await ReadFromCoreServerAsync(command);
            return result.IsSuccess
                ? OperateResult.Success()
                : OperateResult.Fail(result.Message, result.ErrorCode);
        }
    }
}
