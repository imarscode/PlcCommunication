using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PlcCommunication.Core;
using PlcCommunication.DataConvert;
using PlcCommunication.Diagnostics;
using PlcCommunication.Utilities;

namespace PlcCommunication.Protocols.Omron
{
    /// <summary>
    /// Omron FINS协议客户端，基于TCP（FINS/TCP）。
    /// 与Omron CJ、CS、CP和NJ系列PLC通信。
    /// 默认端口：9600（TCP）。
    /// </summary>
    public class OmronFinsNet : NetworkDeviceBase
    {
        /// <summary>FINS目标网络地址。默认0。</summary>
        public byte DstNetwork { get; set; } = 0;

        /// <summary>FINS目标节点（IP最后一段）。默认1。</summary>
        public byte DstNode { get; set; } = 1;

        /// <summary>FINS目标单元地址。默认0（CPU）。</summary>
        public byte DstUnit { get; set; } = 0;

        /// <summary>FINS源网络地址。默认0。</summary>
        public byte SrcNetwork { get; set; } = 0;

        /// <summary>FINS源节点（由握手获取）。</summary>
        public byte SrcNode { get; set; } = 0;

        /// <summary>FINS源单元地址。默认0（CPU）。</summary>
        public byte SrcUnit { get; set; } = 0;

        /// <summary>FINS命令：内存区域读取。</summary>
        private static readonly byte[] FinsCommandRead = { 0x01, 0x01 };

        /// <summary>FINS命令：内存区域写入。</summary>
        private static readonly byte[] FinsCommandWrite = { 0x01, 0x02 };

        /// <summary>
        /// 创建新的Omron FINS客户端。
        /// </summary>
        public OmronFinsNet(string ipAddress, int port = 9600)
        {
            IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            ByteTransform = new ReverseBytesTransform();
            ResponseHeaderLength = 4; // FINS/TCP：4字节长度前缀
        }

        public OmronFinsNet() : this("127.0.0.1") { }

        // =====================================================================
        // 连接 - TCP + FINS/TCP 握手
        // =====================================================================

        /// <inheritdoc/>
        public override async Task<OperateResult> ConnectAsync()
        {
            if (IsConnected)
                return OperateResult.Success();

            try
            {
                Trace(TraceLevel.Info, $"Connecting to Omron FINS at {IpAddress}:{Port}...");

                _tcpClient?.Close();
                _tcpClient = new TcpClient();

                // TCP连接，带超时
                var connectTask = _tcpClient.ConnectAsync(IpAddress, Port);
                var timeoutTask = Task.Delay(ConnectTimeout);
                var completed = await Task.WhenAny(connectTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    CleanupConnection();
                    return OperateResult.Fail("FINS TCP connection timeout", -1001);
                }
                await connectTask;

                _stream = _tcpClient.GetStream();

                // FINS/TCP握手：发送连接请求，获取服务端节点地址
                var handshakeResult = await FinsTcpHandshake();
                if (!handshakeResult.IsSuccess)
                {
                    CleanupConnection();
                    return handshakeResult;
                }

                IsConnected = true;
                Trace(TraceLevel.Info, $"FINS connected to {IpAddress}:{Port}, SrcNode={SrcNode}, DstNode={DstNode}");
                return OperateResult.Success();
            }
            catch (Exception ex)
            {
                CleanupConnection();
                Trace(TraceLevel.Error, $"FINS connection failed: {ex.Message}");
                return OperateResult.Fail($"FINS connection failed: {ex.Message}", ex, -1000);
            }
        }

        /// <summary>
        /// FINS/TCP 握手过程。
        /// 发送 FINS/TCP 连接请求（命令码0x0000）携带客户端节点地址，
        /// 接收服务端返回的服务端节点地址。
        /// </summary>
        private async Task<OperateResult> FinsTcpHandshake()
        {
            try
            {
                // 构建FINS/TCP连接请求
                // 格式：长度(4) + 命令码(2) + 错误码(2) + 客户端节点地址(1) + 保留(3)
                byte[] request = new byte[12];
                // 长度（大端序）：8字节（命令码+错误码+节点+保留）
                request[0] = 0x00; request[1] = 0x00; request[2] = 0x00; request[3] = 0x08;
                // 命令码：0x0000（连接请求）
                request[4] = 0x00; request[5] = 0x00;
                // 错误码：0x0000
                request[6] = 0x00; request[7] = 0x00;
                // 客户端节点地址：使用0让PLC自动分配
                request[8] = 0x00; // 自动分配
                // 保留
                request[9] = 0x00; request[10] = 0x00; request[11] = 0x00;

                Trace(TraceLevel.Verbose, $"[FINS Handshake] Sending connection request: {SoftBasic.BytesToHexString(request)}");

                using var sendCts = new CancellationTokenSource(SendTimeout);
                await _stream!.WriteAsync(request, 0, request.Length, sendCts.Token);
                await _stream.FlushAsync(sendCts.Token);

                // 接收FINS/TCP连接响应
                byte[] response = new byte[24];
                using var recvCts = new CancellationTokenSource(ReceiveTimeout);
                int read = await ReadStreamAsync(_stream, response, 0, 24, recvCts.Token);

                Trace(TraceLevel.Verbose, $"[FINS Handshake] Response: {SoftBasic.BytesToHexString(SoftBasic.ArraySelect(response, 0, read))}");

                if (read < 16)
                    return OperateResult.Fail("FINS/TCP handshake response too short");

                // 检查命令码
                ushort respCommand = (ushort)((response[4] << 8) | response[5]);
                if (respCommand != 0x0000)
                    return OperateResult.Fail($"FINS/TCP handshake unexpected command: 0x{respCommand:X4}");

                // 检查错误码
                ushort respError = (ushort)((response[6] << 8) | response[7]);
                if (respError != 0)
                    return OperateResult.Fail($"FINS/TCP handshake error: 0x{respError:X4}");

                // 获取服务端节点地址（响应字节8）
                byte serverNode = response[8];

                // 获取客户端节点地址（响应字节16，某些PLC在响应的后半段返回分配的节点号）
                byte clientNode = response[16];

                // 更新源和目标节点
                SrcNode = clientNode;
                DstNode = serverNode;

                Trace(TraceLevel.Info, $"FINS/TCP handshake complete: ClientNode={SrcNode}, ServerNode={DstNode}");
                return OperateResult.Success();
            }
            catch (OperationCanceledException)
            {
                return OperateResult.Fail("FINS/TCP handshake timeout", -1001);
            }
            catch (Exception ex)
            {
                return OperateResult.Fail($"FINS/TCP handshake error: {ex.Message}", ex, -1000);
            }
        }

        // =====================================================================
        // 命令构建
        // =====================================================================

        /// <inheritdoc/>
        protected override byte[] BuildReadCommand(string address, ushort length)
        {
            var (areaCode, startAddress, bitOffset, isBitArea) = ParseAddress(address);

            // FINS帧：ICF(1) + RSV(1) + GCT(1) + DNA(1) + DA1(1) + DA2(1) + 
            //          SNA(1) + SA1(1) + SA2(1) + SID(1) + 命令(2) + 数据(6)
            // 数据：区域码(1) + 起始地址(2) + 位偏移(1) + 字数/位数(2)

            byte[] finsFrame = new byte[18]; // 10(FINS头) + 2(命令) + 6(数据)
            int idx = 0;

            // FINS头部
            finsFrame[idx++] = 0xC0; // ICF：信息帧，需要响应（0xC0=需要响应，0x80=不需要）
            finsFrame[idx++] = 0x00; // RSV
            finsFrame[idx++] = 0x02; // GCT（网关计数）
            finsFrame[idx++] = DstNetwork;  // DNA：目标网络地址
            finsFrame[idx++] = DstNode;     // DA1：目标节点
            finsFrame[idx++] = DstUnit;     // DA2：目标单元
            finsFrame[idx++] = SrcNetwork;  // SNA：源网络地址
            finsFrame[idx++] = SrcNode;     // SA1：源节点
            finsFrame[idx++] = SrcUnit;     // SA2：源单元
            finsFrame[idx++] = 0x00;        // SID：服务ID

            // 命令码
            finsFrame[idx++] = FinsCommandRead[0];
            finsFrame[idx++] = FinsCommandRead[1];

            // 数据部分
            finsFrame[idx++] = areaCode;           // 内存区域码
            finsFrame[idx++] = (byte)((startAddress >> 8) & 0xFF); // 起始地址高字节
            finsFrame[idx++] = (byte)(startAddress & 0xFF);        // 起始地址低字节
            finsFrame[idx++] = (byte)bitOffset;                     // 位偏移

            // 读取字数/位数（大端序）
            ushort readCount;
            if (isBitArea)
            {
                // 位区域：length表示位数
                readCount = length;
            }
            else
            {
                // 字区域：length表示字数（每个字2字节）
                readCount = (ushort)((length + 1) / 2);
            }

            finsFrame[idx++] = (byte)((readCount >> 8) & 0xFF); // 字数/位数高字节
            finsFrame[idx++] = (byte)(readCount & 0xFF);        // 字数/位数低字节

            // 封装到FINS/TCP帧中：4字节长度 + FINS帧
            byte[] tcpFrame = new byte[4 + finsFrame.Length];
            // 长度字段（大端序）
            tcpFrame[0] = (byte)((finsFrame.Length >> 24) & 0xFF);
            tcpFrame[1] = (byte)((finsFrame.Length >> 16) & 0xFF);
            tcpFrame[2] = (byte)((finsFrame.Length >> 8) & 0xFF);
            tcpFrame[3] = (byte)(finsFrame.Length & 0xFF);
            Buffer.BlockCopy(finsFrame, 0, tcpFrame, 4, finsFrame.Length);

            return tcpFrame;
        }

        /// <inheritdoc/>
        protected override byte[] BuildWriteCommand(string address, byte[] data)
        {
            var (areaCode, startAddress, bitOffset, isBitArea) = ParseAddress(address);

            // FINS帧：头部(10) + 命令(2) + 数据头部(4) + 写入数据
            byte[] finsFrame = new byte[16 + data.Length];
            int idx = 0;

            // FINS头部
            finsFrame[idx++] = 0xC0; // ICF：信息帧，需要响应
            finsFrame[idx++] = 0x00;
            finsFrame[idx++] = 0x02;
            finsFrame[idx++] = DstNetwork;
            finsFrame[idx++] = DstNode;
            finsFrame[idx++] = DstUnit;
            finsFrame[idx++] = SrcNetwork;
            finsFrame[idx++] = SrcNode;
            finsFrame[idx++] = SrcUnit;
            finsFrame[idx++] = 0x00;

            // 命令码
            finsFrame[idx++] = FinsCommandWrite[0];
            finsFrame[idx++] = FinsCommandWrite[1];

            // 数据部分：区域码 + 起始地址 + 位偏移
            finsFrame[idx++] = areaCode;
            finsFrame[idx++] = (byte)((startAddress >> 8) & 0xFF);
            finsFrame[idx++] = (byte)(startAddress & 0xFF);
            finsFrame[idx++] = (byte)bitOffset;

            // 写入数据
            Buffer.BlockCopy(data, 0, finsFrame, idx, data.Length);

            // FINS/TCP包装
            byte[] tcpFrame = new byte[4 + finsFrame.Length];
            tcpFrame[0] = (byte)((finsFrame.Length >> 24) & 0xFF);
            tcpFrame[1] = (byte)((finsFrame.Length >> 16) & 0xFF);
            tcpFrame[2] = (byte)((finsFrame.Length >> 8) & 0xFF);
            tcpFrame[3] = (byte)(finsFrame.Length & 0xFF);
            Buffer.BlockCopy(finsFrame, 0, tcpFrame, 4, finsFrame.Length);

            return tcpFrame;
        }

        // =====================================================================
        // 响应检查
        // =====================================================================

        /// <inheritdoc/>
        protected override OperateResult<byte[]> CheckResponse(byte[] command, byte[] response)
        {
            if (response.Length < 16)
                return OperateResult.Fail<byte[]>($"FINS response too short: {response.Length}");

            // 跳过4字节的FINS/TCP长度前缀
            int offset = 4;

            // FINS头部为10字节，命令码2字节，响应码2字节
            // 响应码位于 offset + 10 + 2 = offset + 12
            byte responseCodeHigh = response[offset + 12];
            byte responseCodeLow = response[offset + 13];
            ushort responseCode = (ushort)((responseCodeHigh << 8) | responseCodeLow);

            if (responseCode != 0)
            {
                string errorDesc = GetFinsErrorDescription(responseCode);
                return OperateResult.Fail<byte[]>($"FINS error: 0x{responseCode:X4} - {errorDesc}", -responseCode);
            }

            // 数据从 offset + 14 开始（头部10 + 命令2 + 响应码2）
            int dataStart = offset + 14;
            if (dataStart >= response.Length)
                return OperateResult.Success(Array.Empty<byte>());

            int dataLen = response.Length - dataStart;
            byte[] result = new byte[dataLen];
            Buffer.BlockCopy(response, dataStart, result, 0, dataLen);
            return OperateResult.Success(result);
        }

        /// <inheritdoc/>
        protected override int GetResponseLength(byte[] header)
        {
            if (header.Length < 4) return 0;
            // FINS/TCP长度字段为大端序
            int dataLen = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            return dataLen + 4; // 4字节长度前缀 + FINS帧数据
        }

        // =====================================================================
        // 地址解析
        // =====================================================================

        /// <summary>
        /// 解析Omron内存区域地址。
        /// 格式：D100（DM）、W100（工作区）、CIO100（CIO）、H100（保持区）等。
        /// 位寻址：D100.05
        /// </summary>
        private (byte areaCode, int startAddress, byte bitOffset, bool isBitArea) ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new PlcCommunicationException("Omron address cannot be empty");

            address = address.Trim().ToUpperInvariant();
            
            if (address.Length == 0)
                throw new PlcCommunicationException("Omron address cannot be empty");

            byte areaCode;
            int addrStart = 0;
            bool isBitArea = false;

            if (address.StartsWith("CIO"))
                { areaCode = 0xB0; addrStart = 3; }
            else if (address.StartsWith("WR") || address.StartsWith("W"))
                { areaCode = 0xB1; addrStart = address.StartsWith("WR") ? 2 : 1; }
            else if (address.StartsWith("HR") || address.StartsWith("H"))
                { areaCode = 0xB2; addrStart = address.StartsWith("HR") ? 2 : 1; }
            else if (address.StartsWith("AR") || address.StartsWith("A"))
                { areaCode = 0xB3; addrStart = address.StartsWith("AR") ? 2 : 1; }
            else if (address.StartsWith("CIO"))
                { areaCode = 0xB0; addrStart = 3; }
            else if (address.StartsWith("DM") || address.StartsWith("D"))
                { areaCode = 0x82; addrStart = address.StartsWith("DM") ? 2 : 1; }
            else if (address.StartsWith("E0_"))
                { areaCode = 0xA0; addrStart = 3; }
            else if (address.StartsWith("E1_"))
                { areaCode = 0xA1; addrStart = 3; }
            else if (address.StartsWith("E2_"))
                { areaCode = 0xA2; addrStart = 3; }
            else if (address.StartsWith("EM") || address.StartsWith("E"))
                { areaCode = 0x90; addrStart = address.StartsWith("EM") ? 2 : 1; }
            else if (address.StartsWith("T"))
                { areaCode = 0x09; addrStart = 1; } // 定时器PV/完成标志
            else if (address.StartsWith("C"))
                { areaCode = 0x09; addrStart = 1; } // 计数器PV/完成标志
            else
                throw new PlcCommunicationException($"Unknown Omron area: {address}");

            string addrPart = address.Substring(addrStart);
            int bitOffset = 0;

            // 处理位寻址（例如 D100.05）
            int dotIndex = addrPart.IndexOf(".");
            if (dotIndex >= 0)
            {
                bitOffset = int.Parse(addrPart.Substring(dotIndex + 1));
                addrPart = addrPart.Substring(0, dotIndex);
                isBitArea = true;

                // 位区域码映射
                if (areaCode == 0x82) areaCode = 0x02; // DM位
                else if (areaCode == 0xB0) areaCode = 0x30; // CIO位
                else if (areaCode == 0xB1) areaCode = 0x31; // WR位
                else if (areaCode == 0xB2) areaCode = 0x32; // HR位
                else if (areaCode == 0xB3) areaCode = 0x33; // AR位
            }

            int startAddr = int.Parse(addrPart);

            return (areaCode, startAddr, (byte)bitOffset, isBitArea);
        }

        /// <summary>获取FINS错误码描述。</summary>
        private static string GetFinsErrorDescription(ushort errorCode)
        {
            // 主响应码（高字节）
            byte mainCode = (byte)((errorCode >> 8) & 0xFF);
            byte subCode = (byte)(errorCode & 0xFF);

            return mainCode switch
            {
                0x00 => "正常完成",
                0x01 => $"本地节点错误: {GetLocalNodeError(subCode)}",
                0x02 => $"目标节点错误: {GetTargetNodeError(subCode)}",
                0x03 => $"通信控制器错误: {GetCommControllerError(subCode)}",
                0x04 => $"不可执行: {GetNotExecutableError(subCode)}",
                0x05 => $"目的地不可达: {GetDestinationUnreachableError(subCode)}",
                0x20 => "无法识别的命令",
                0x21 => "不支持该命令",
                0x22 => "远程节点未在路由表中",
                0x23 => "路由器不可用",
                0x24 => "目的地节点未在路由表中",
                0x25 => "通信端口错误",
                0x26 => "目的地节点忙",
                _ => $"未知错误 (0x{errorCode:X4})"
            };
        }

        private static string GetLocalNodeError(byte sub) => sub switch
        {
            0x01 => "运行模式错误",
            0x02 => "本地节点不是内插板/CPU单元",
            0x03 => "单元/地址不存在",
            0x04 => "访问权限错误",
            _ => $"子错误码 0x{sub:X2}"
        };

        private static string GetTargetNodeError(byte sub) => sub switch
        {
            0x01 => "命令不被支持",
            0x02 => "单元/地址不存在",
            0x03 => "访问权限错误",
            0x04 => "远程节点错误",
            0x05 => "地址超出范围",
            0x06 => "数据超出范围",
            _ => $"子错误码 0x{sub:X2}"
        };

        private static string GetCommControllerError(byte sub) => sub switch
        {
            0x01 => "通信控制器错误",
            0x02 => "CPU单元错误",
            0x04 => "单元/地址不存在",
            0x05 => "CPU单元正在运行",
            0x06 => "CPU单元未运行",
            0x07 => "CPU单元监视定时器错误",
            _ => $"子错误码 0x{sub:X2}"
        };

        private static string GetNotExecutableError(byte sub) => sub switch
        {
            0x01 => "服务被取消",
            0x02 => "服务不可执行",
            0x03 => "路由表无效",
            0x04 => "节点地址无效",
            _ => $"子错误码 0x{sub:X2}"
        };

        private static string GetDestinationUnreachableError(byte sub) => sub switch
        {
            0x01 => "网络不可达",
            0x02 => "节点不可达",
            0x03 => "单元不可达",
            0x04 => "通信端口不可达",
            _ => $"子错误码 0x{sub:X2}"
        };
    }
}
