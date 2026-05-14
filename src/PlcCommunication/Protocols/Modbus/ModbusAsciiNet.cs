using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlcCommunication.Core;
using PlcCommunication.DataConvert;
using PlcCommunication.Utilities;

namespace PlcCommunication.Protocols.Modbus
{
    /// <summary>
    /// Modbus ASCII 客户端。使用 ASCII 帧格式与 Modbus 设备通信
    /// （以 ':' 开头，CR/LF 结束，LRC 校验）。设计用于 TCP 封装
    /// （串口转以太网转换器）或直接串口。
    /// </summary>
    public class ModbusAsciiNet : NetworkDeviceBase
    {
        /// <summary>Modbus 从站地址。默认为 1。</summary>
        public byte StationId { get; set; } = 1;

        public ModbusAsciiNet(string ipAddress, int port = 502, byte stationId = 1)
        {
            IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            StationId = stationId;
            ByteTransform = new ReverseBytesTransform();
            ResponseHeaderLength = 1;
        }

        public ModbusAsciiNet() : this("127.0.0.1") { }

        /// <summary>编码 Modbus ASCII 帧：":" + hex(地址 + 功能码 + 数据 + LRC) + CR + LF。</summary>
        private byte[] EncodeAsciiFrame(byte[] pdu)
        {
            byte lrc = ModbusHelper.CalculateLRC(pdu);
            byte[] fullFrame = new byte[pdu.Length + 1];
            Buffer.BlockCopy(pdu, 0, fullFrame, 0, pdu.Length);
            fullFrame[pdu.Length] = lrc;

            string hex = SoftBasic.BytesToHexString(fullFrame, '\0');
            string asciiFrame = ":" + hex + "\r\n";
            return Encoding.ASCII.GetBytes(asciiFrame);
        }

        /// <summary>解码 Modbus ASCII 帧，去除 ':' 和 CR/LF，返回经过 LRC 验证的二进制数据。</summary>
        private OperateResult<byte[]> DecodeAsciiFrame(byte[] asciiData)
        {
            string asciiStr = Encoding.ASCII.GetString(asciiData).Trim();

            if (!asciiStr.StartsWith(":"))
                return OperateResult.Fail<byte[]>("Invalid ASCII frame: missing start character");

            if (!asciiStr.EndsWith("\r\n") && !asciiStr.EndsWith("\n"))
                return OperateResult.Fail<byte[]>("Invalid ASCII frame: missing end character");

            // 提取十六进制部分（在 ':' 和 CR/LF 之间）
            int endIdx = asciiStr.IndexOf('\r');
            if (endIdx < 0) endIdx = asciiStr.IndexOf('\n');
            if (endIdx < 0) endIdx = asciiStr.Length;
            // 处理包含 \r\n 的情况
            if (endIdx < 0) endIdx = asciiStr.Length;

            string hexPart = asciiStr.Substring(1, endIdx - 1);

            // 移除任何剩余的 \r 或 \n 字符
            hexPart = hexPart.TrimEnd('\r', '\n');

            byte[] binary = SoftBasic.HexStringToBytes(hexPart);
            if (binary.Length < 3)
                return OperateResult.Fail<byte[]>("Frame too short");

            // 验证 LRC
            if (!ModbusHelper.VerifyLRC(binary))
                return OperateResult.Fail<byte[]>("LRC mismatch");

            // 移除 LRC 字节
            byte[] result = new byte[binary.Length - 1];
            Buffer.BlockCopy(binary, 0, result, 0, binary.Length - 1);
            return OperateResult.Success(result);
        }

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

            return EncodeAsciiFrame(pdu);
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

            return EncodeAsciiFrame(pdu);
        }

        /// <inheritdoc/>
        protected override async Task<OperateResult<byte[]>> ReceiveAsync(CancellationToken ct)
        {
            if (_stream == null)
                return OperateResult.Fail<byte[]>("Not connected");

            try
            {
                // 持续读取直到找到 ':' 后跟数据和 CR/LF
                var buffer = new System.Collections.Generic.List<byte>();
                bool foundStart = false;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ReceiveTimeout);

                while (!ct.IsCancellationRequested)
                {
                    byte[] oneByte = new byte[1];
                    int read = await _stream.ReadAsync(oneByte, 0, 1, ct);
                    if (read == 0)
                        break;

                    byte b = oneByte[0];

                    if (!foundStart)
                    {
                        if (b == ':')
                        {
                            foundStart = true;
                            buffer.Add(b);
                        }
                        continue;
                    }

                    buffer.Add(b);

                    // 检查帧是否结束（CR/LF）
                    if (buffer.Count >= 2 && b == '\n')
                        break;
                }

                if (!foundStart || buffer.Count < 4)
                    return OperateResult.Fail<byte[]>("Invalid ASCII response");

                return DecodeAsciiFrame(buffer.ToArray());
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
        protected override OperateResult<byte[]> CheckResponse(byte[] command, byte[] response)
        {
            // 响应已经是解码后的二进制数据（无 LRC，无 ASCII 封装）
            if (response.Length < 2)
                return OperateResult.Fail<byte[]>("Response too short");

            byte functionCode = response[1];
            if ((functionCode & 0x80) != 0)
            {
                byte exceptionCode = response.Length > 2 ? response[2] : (byte)0;
                return OperateResult.Fail<byte[]>($"Modbus exception {exceptionCode}", -exceptionCode);
            }

            // 提取数据
            if (functionCode == ModbusFunction.ReadHoldingRegisters ||
                functionCode == ModbusFunction.ReadInputRegisters ||
                functionCode == ModbusFunction.ReadCoils ||
                functionCode == ModbusFunction.ReadDiscreteInputs)
            {
                int dataOffset = 3; // 地址 + 功能码 + 字节数
                int dataLength = response.Length - dataOffset;
                if (dataLength < 0) dataLength = 0;
                byte[] result = new byte[dataLength];
                Buffer.BlockCopy(response, dataOffset, result, 0, dataLength);
                return OperateResult.Success(result);
            }

            return OperateResult.Success(Array.Empty<byte>());
        }

        /// <inheritdoc/>
        protected override int GetResponseLength(byte[] header)
        {
            return 0; // 未使用 - ReceiveAsync 已重写
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
            pdu[4] = 1;

            byte[] command = EncodeAsciiFrame(pdu);
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

            byte[] command = EncodeAsciiFrame(pdu);
            var result = await ReadFromCoreServerAsync(command);
            return result.IsSuccess
                ? OperateResult.Success()
                : OperateResult.Fail(result.Message, result.ErrorCode);
        }
    }
}
