using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PlcCommunication.Core;
using PlcCommunication.DataConvert;
using PlcCommunication.Diagnostics;
using PlcCommunication.Utilities;

namespace PlcCommunication.Protocols.Siemens
{
    /// <summary>
    /// 西门子S7 PLC通信客户端。支持S7-200到S7-1500，
    /// 通过ISO-on-TCP（RFC 1006）在端口102上进行通信。
    /// </summary>
    public class SiemensS7Net : NetworkDeviceBase
    {
        private readonly SiemensPLCS _plcType;
        private ushort _pduRef = 1;
        private int _pduLength = 240;

        /// <summary>本地TSAP，用于连接。对于PG通常为"10.00"。</summary>
        public string LocalTSAP { get; set; } = "10.00";

        /// <summary>远程TSAP，用于连接。对于S7-300 CPU插槽2通常为"02.00"。</summary>
        public string RemoteTSAP { get; set; } = "02.00";

        /// <summary>
        /// 创建新的西门子S7客户端。
        /// </summary>
        /// <param name="plcType">PLC型号（影响PDU大小协商和TSAP默认值）。</param>
        /// <param name="ipAddress">PLC IP地址。</param>
        /// <param name="port">TCP端口（默认102）。</param>
        public SiemensS7Net(SiemensPLCS plcType, string ipAddress, int port = 102)
        {
            _plcType = plcType;
            IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            ByteTransform = new ReverseBytesTransform();
            ResponseHeaderLength = 4; // TPKT头部4字节

            // 根据PLC型号设置默认PDU大小
            _pduLength = plcType switch
            {
                SiemensPLCS.S200 => 240,
                SiemensPLCS.S300 => 240,
                SiemensPLCS.S400 => 480,
                SiemensPLCS.S1200 => 240,
                SiemensPLCS.S1500 => 960,
                _ => 240
            };

            // 根据PLC型号设置默认TSAP
            (LocalTSAP, RemoteTSAP) = plcType switch
            {
                SiemensPLCS.S200 => ("10.00", "00.00"),
                SiemensPLCS.S300 => ("10.00", "02.00"),
                SiemensPLCS.S400 => ("10.00", "02.00"),
                SiemensPLCS.S1200 => ("10.00", "01.00"),
                SiemensPLCS.S1500 => ("10.00", "01.00"),
                _ => ("10.00", "02.00")
            };
        }

        /// <summary>
        /// 创建新的S7客户端（默认为S7-1200，127.0.0.1）。
        /// </summary>
        public SiemensS7Net()
            : this(SiemensPLCS.S1200, "127.0.0.1") { }

        // =====================================================================
        // 连接 - TCP + COTP + PDU协商
        // =====================================================================

        /// <inheritdoc/>
        public override async Task<OperateResult> ConnectAsync()
        {
            if (IsConnected)
                return OperateResult.Success();

            try
            {
                Trace(TraceLevel.Info, $"Connecting to Siemens S7 at {IpAddress}:{Port}...");

                _tcpClient?.Close();
                _tcpClient = new TcpClient();

                // TCP连接，带超时
                var connectTask = _tcpClient.ConnectAsync(IpAddress, Port);
                var timeoutTask = Task.Delay(ConnectTimeout);
                var completed = await Task.WhenAny(connectTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    CleanupConnection();
                    return OperateResult.Fail("TCP connection timeout", -1001);
                }
                await connectTask;

                _stream = _tcpClient.GetStream();

                // 步骤1：COTP连接请求
                var cotpResult = await SendCOTPConnectionRequest();
                if (!cotpResult.IsSuccess)
                {
                    CleanupConnection();
                    return cotpResult;
                }

                // 步骤2：PDU大小协商（S7建立通信）
                var pduResult = await NegotiatePDUSize();
                if (!pduResult.IsSuccess)
                {
                    CleanupConnection();
                    return pduResult;
                }

                IsConnected = true;
                Trace(TraceLevel.Info, $"S7 connected to {IpAddress}:{Port}, PDU size={_pduLength}");
                return OperateResult.Success();
            }
            catch (Exception ex)
            {
                CleanupConnection();
                Trace(TraceLevel.Error, $"S7 connection failed: {ex.Message}");
                return OperateResult.Fail($"Connection failed: {ex.Message}", ex, -1000);
            }
        }

        private async Task<OperateResult> SendCOTPConnectionRequest()
        {
            try
            {
                byte[] crPacket = BuildCOTPPacket();
                Trace(TraceLevel.Verbose, $"[COTP CR] {SoftBasic.BytesToHexString(crPacket)}");

                using var sendCts = new CancellationTokenSource(SendTimeout);
                await _stream!.WriteAsync(crPacket, 0, crPacket.Length, sendCts.Token);
                await _stream.FlushAsync(sendCts.Token);

                // 先读取TPKT头部（4字节）获取响应长度
                byte[] tpktHeader = new byte[4];
                using var recvCts = new CancellationTokenSource(ReceiveTimeout);
                int headerRead = await ReadStreamAsync(_stream, tpktHeader, 0, 4, recvCts.Token);

                if (headerRead != 4 || tpktHeader[0] != 0x03)
                    return OperateResult.Fail("Invalid TPKT header in COTP CC");

                int ccLength = (tpktHeader[2] << 8) | tpktHeader[3];
                if (ccLength < 7)
                    return OperateResult.Fail("COTP CC response too short");

                // 读取剩余部分
                byte[] ccBody = new byte[ccLength - 4];
                if (ccBody.Length > 0)
                {
                    int bodyRead = await ReadStreamAsync(_stream, ccBody, 0, ccBody.Length, recvCts.Token);
                    if (bodyRead != ccBody.Length)
                        return OperateResult.Fail("Incomplete COTP CC response");
                }

                // 完整响应
                byte[] fullResponse = new byte[ccLength];
                Buffer.BlockCopy(tpktHeader, 0, fullResponse, 0, 4);
                if (ccBody.Length > 0)
                    Buffer.BlockCopy(ccBody, 0, fullResponse, 4, ccBody.Length);

                Trace(TraceLevel.Verbose, $"[COTP CC] {SoftBasic.BytesToHexString(fullResponse)}");

                // 检查COTP CC：第5字节应该是0xD0（连接确认）
                if (fullResponse[5] != 0xD0)
                    return OperateResult.Fail($"Invalid COTP CC: expected 0xD0, got 0x{fullResponse[5]:X2}");

                return OperateResult.Success();
            }
            catch (OperationCanceledException)
            {
                return OperateResult.Fail("COTP handshake timeout", -1001);
            }
            catch (Exception ex)
            {
                return OperateResult.Fail($"COTP handshake error: {ex.Message}", ex, -1000);
            }
        }

        /// <summary>
        /// 构建COTP连接请求（CR）包。
        /// 严格按照ISO 8073标准：
        /// TPKT头部(4) + COTP CR TPDU:
        ///   长度指示器(1) + CR标识0xE0(1) + 目标引用(2) + 源引用(2) + 类别选项(1) + 参数
        /// </summary>
        private byte[] BuildCOTPPacket()
        {
            // 解析TSAP字符串
            byte[] localTsap = ParseTSAP(LocalTSAP);
            byte[] remoteTsap = ParseTSAP(RemoteTSAP);

            // COTP CR TPDU内容（不含TPKT头部）：
            // LI(1) + 0xE0(1) + Dst-Ref(2) + Src-Ref(2) + Class(1) = 7字节
            // 参数：TPDU大小 C0 01 0A (3) + SrcTSAP C1 len data (2+len) + DstTSAP C2 len data (2+len)
            int cotpLen = 7 + 3 + (2 + localTsap.Length) + (2 + remoteTsap.Length);

            // TPKT头部(4) + COTP内容
            byte[] packet = new byte[4 + cotpLen];
            int idx = 0;

            // TPKT头部
            packet[idx++] = 0x03; // 版本
            packet[idx++] = 0x00; // 保留
            // TPKT长度（包含自身4字节，大端序）
            packet[idx++] = (byte)((packet.Length >> 8) & 0xFF);
            packet[idx++] = (byte)(packet.Length & 0xFF);

            // COTP CR TPDU
            packet[idx++] = (byte)cotpLen; // 长度指示器（COTP TPDU长度，不含自身和TPKT）
            packet[idx++] = 0xE0;           // CR标识（Connection Request = 0xE0）

            // 目标引用（2字节，大端序）- 通常为0x0000
            packet[idx++] = 0x00;
            packet[idx++] = 0x00;

            // 源引用（2字节，大端序）- 通常为0x0001
            packet[idx++] = 0x00;
            packet[idx++] = 0x01;

            // 类别选项（1字节）= 0x00
            packet[idx++] = 0x00;

            // 参数：TPDU大小
            packet[idx++] = 0xC0; // 参数码：TPDU大小
            packet[idx++] = 0x01; // 参数长度
            packet[idx++] = 0x0A; // TPDU大小 = 1024 (2^10)

            // 参数：源TSAP
            packet[idx++] = 0xC1; // 参数码：源TSAP
            packet[idx++] = (byte)localTsap.Length;
            Buffer.BlockCopy(localTsap, 0, packet, idx, localTsap.Length);
            idx += localTsap.Length;

            // 参数：目标TSAP
            packet[idx++] = 0xC2; // 参数码：目标TSAP
            packet[idx++] = (byte)remoteTsap.Length;
            Buffer.BlockCopy(remoteTsap, 0, packet, idx, remoteTsap.Length);
            idx += remoteTsap.Length;

            return packet;
        }

        private static byte[] ParseTSAP(string tsap)
        {
            string[] parts = tsap.Split('.');
            if (parts.Length == 2)
            {
                return new byte[]
                {
                    byte.Parse(parts[0], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(parts[1], System.Globalization.NumberStyles.HexNumber)
                };
            }
            // 默认TSAP
            return new byte[] { 0x01, 0x00 };
        }

        /// <summary>
        /// S7 PDU大小协商（建立通信）。
        /// 请求包含完整参数：功能码 + PDU大小 + 最大AMQ调用数 + 最大AMQ应答数。
        /// </summary>
        private async Task<OperateResult> NegotiatePDUSize()
        {
            try
            {
                int requestedPDU = _pduLength;

                // S7建立通信请求
                // 头部(10) + 参数(8) = 18字节
                byte[] request = new byte[18];
                request[0] = 0x32; // 协议ID
                request[1] = 0x01; // 作业类型
                request[2] = 0x00; // 保留
                request[3] = 0x00; // 保留
                request[4] = (byte)((_pduRef >> 8) & 0xFF); // PDU引用高字节
                request[5] = (byte)(_pduRef++ & 0xFF);      // PDU引用低字节
                request[6] = 0x00; // 参数长度高字节
                request[7] = 0x08; // 参数长度低字节 = 8
                request[8] = 0x00; // 数据长度高字节
                request[9] = 0x00; // 数据长度低字节

                // 参数区域（8字节）：
                request[10] = 0xF0; // 功能码：建立通信
                request[11] = 0x00; // 保留

                // 请求的PDU大小（大端序）
                request[12] = (byte)((requestedPDU >> 8) & 0xFF);
                request[13] = (byte)(requestedPDU & 0xFF);

                // 最大AMQ调用数
                request[14] = 0x01;

                // 最大AMQ应答数
                request[15] = 0x01;

                // 保留
                request[16] = 0x00;
                request[17] = 0x00;

                Trace(TraceLevel.Verbose, $"[S7 PDU Neg] {SoftBasic.BytesToHexString(request)}");

                using var sendCts = new CancellationTokenSource(SendTimeout);
                await _stream!.WriteAsync(request, 0, request.Length, sendCts.Token);
                await _stream.FlushAsync(sendCts.Token);

                // 读取S7头部（4字节TPKT => 长度）
                byte[] header = new byte[4];
                using var recvCts = new CancellationTokenSource(ReceiveTimeout);
                int read = await ReadStreamAsync(_stream, header, 0, 4, recvCts.Token);
                if (read != 4)
                    return OperateResult.Fail("Failed to read S7 setup response header");

                int totalLength = (header[2] << 8) | header[3];
                if (totalLength <= 4)
                    return OperateResult.Fail("Invalid S7 setup response length");

                byte[] response = new byte[totalLength];
                Buffer.BlockCopy(header, 0, response, 0, 4);
                if (totalLength > 4)
                {
                    read = await ReadStreamAsync(_stream, response, 4, totalLength - 4, recvCts.Token);
                }

                Trace(TraceLevel.Verbose, $"[S7 PDU Resp] {SoftBasic.BytesToHexString(response)}");

                // 验证响应
                if (response.Length < 12)
                    return OperateResult.Fail("S7 setup response too short");

                if (response[4] != 0x32)
                    return OperateResult.Fail("Invalid S7 protocol ID in setup response");

                // 检查错误类型（字节10是功能码，应该是0xF0的反馈0x00表示成功）
                // 对于建立通信响应，消息类型应该是0x03（Ack Data）
                if (response[1] != 0x03)
                    return OperateResult.Fail($"Unexpected S7 message type in setup response: 0x{response[1]:X2}");

                // 解析协商后的PDU大小
                // 响应结构：TPKT(4) + S7头(6) + 参数长度(2) + 数据长度(2) + 参数 + 数据
                // 偏移量：4(TPKT) + 6(S7头) = 10
                int paramLen = (response[6] << 8) | response[7];
                int dataLen = (response[8] << 8) | response[9];

                // 数据区域起始 = 10 + paramLen
                int dataStart = 10 + paramLen;
                
                // 数据区域结构：返回码(1) + 传输大小(1) + 数据长度(2) + 数据
                if (dataStart + 4 <= response.Length)
                {
                    byte returnCode = response[dataStart];
                    if (returnCode != 0xFF)
                    {
                        // 非致命：PDU协商可能部分成功
                        Trace(TraceLevel.Warning, $"S7 PDU negotiation return code: 0x{returnCode:X2}");
                    }

                    // 数据长度字段
                    int respDataLen = (response[dataStart + 2] << 8) | response[dataStart + 3];

                    // PDU大小在数据区域：跳过返回码(1) + 传输大小(1) + 数据长度(2) = 4字节
                    if (dataStart + 6 <= response.Length)
                    {
                        int negotiatedPDU = (response[dataStart + 4] << 8) | response[dataStart + 5];
                        if (negotiatedPDU > 0 && negotiatedPDU <= 65535)
                        {
                            _pduLength = negotiatedPDU;
                            Trace(TraceLevel.Info, $"S7 negotiated PDU size: {_pduLength}");
                        }
                    }
                }

                return OperateResult.Success();
            }
            catch (OperationCanceledException)
            {
                return OperateResult.Fail("PDU negotiation timeout", -1001);
            }
            catch (Exception ex)
            {
                return OperateResult.Fail($"PDU negotiation error: {ex.Message}", ex, -1000);
            }
        }

        // =====================================================================
        // 命令构建
        // =====================================================================

        /// <inheritdoc/>
        protected override byte[] BuildReadCommand(string address, ushort length)
        {
            var addrData = S7AddressParser.Parse(address);

            // 位访问时，使用传输大小BIT且元素计数为1
            byte transportSize = addrData.TransportSize;
            int elementCount;

            if (addrData.TransportSize == S7TransportSize.Bit)
            {
                transportSize = S7TransportSize.Bit;
                elementCount = 1;
            }
            else if (addrData.TransportSize == S7TransportSize.Byte)
            {
                elementCount = length;
            }
            else if (addrData.TransportSize == S7TransportSize.Word)
            {
                elementCount = length / 2;
                if (elementCount < 1) elementCount = 1;
            }
            else if (addrData.TransportSize == S7TransportSize.DWord)
            {
                elementCount = length / 4;
                if (elementCount < 1) elementCount = 1;
            }
            else
            {
                elementCount = length;
            }

            // S7 PDU头部（10字节）+ 参数（14字节）
            byte[] command = new byte[10 + 14];
            command[0] = 0x32; // 协议
            command[1] = 0x01; // 作业
            command[2] = 0x00; // 保留
            command[3] = 0x00; // 保留
            command[4] = (byte)((_pduRef >> 8) & 0xFF);
            command[5] = (byte)(_pduRef++ & 0xFF);
            command[6] = 0x00; // 参数长度高字节
            command[7] = 0x0E; // 参数长度低字节 = 14
            command[8] = 0x00; // 数据长度高字节
            command[9] = 0x00; // 数据长度低字节

            // 参数区域（14字节）
            command[10] = 0x04; // 读取变量
            command[11] = 0x01; // 项目计数 = 1
            command[12] = 0x12; // 规格类型
            command[13] = 0x0A; // 规格长度 = 10
            command[14] = 0x10; // 语法ID：S7ANY
            command[15] = transportSize;

            // 元素计数（大端序）
            command[16] = (byte)((elementCount >> 8) & 0xFF);
            command[17] = (byte)(elementCount & 0xFF);

            // DB块号（大端序）
            command[18] = (byte)((addrData.DBNumber >> 8) & 0xFF);
            command[19] = (byte)(addrData.DBNumber & 0xFF);

            // 区域
            command[20] = addrData.Area;

            // 地址（3字节，大端序）
            int offset = addrData.ByteOffset;
            if (addrData.TransportSize == S7TransportSize.Bit)
            {
                // 位访问时，地址 = 字节偏移量 * 8 + 位偏移量
                offset = addrData.ByteOffset * 8 + addrData.BitOffset;
            }

            command[21] = (byte)((offset >> 16) & 0xFF);
            command[22] = (byte)((offset >> 8) & 0xFF);
            command[23] = (byte)(offset & 0xFF);

            return command;
        }

        /// <inheritdoc/>
        protected override byte[] BuildWriteCommand(string address, byte[] data)
        {
            var addrData = S7AddressParser.Parse(address);

            byte transportSize = addrData.TransportSize;
            int elementCount;

            if (addrData.TransportSize == S7TransportSize.Bit)
            {
                transportSize = S7TransportSize.Bit;
                elementCount = 1;
            }
            else if (addrData.TransportSize == S7TransportSize.Word)
            {
                elementCount = data.Length / 2;
                if (elementCount < 1) elementCount = 1;
            }
            else if (addrData.TransportSize == S7TransportSize.DWord)
            {
                elementCount = data.Length / 4;
                if (elementCount < 1) elementCount = 1;
            }
            else
            {
                elementCount = data.Length;
            }

            int paddedLength = data.Length;
            // 头部(10) + 参数(14) + 数据头部(4) + 数据
            byte[] command = new byte[10 + 14 + 4 + paddedLength];
            command[0] = 0x32;
            command[1] = 0x01;
            command[2] = 0x00;
            command[3] = 0x00;
            command[4] = (byte)((_pduRef >> 8) & 0xFF);
            command[5] = (byte)(_pduRef++ & 0xFF);
            command[6] = 0x00;
            command[7] = 0x0E; // 14字节参数
            command[8] = (byte)(((4 + paddedLength) >> 8) & 0xFF);
            command[9] = (byte)((4 + paddedLength) & 0xFF);

            // 参数区域（14字节 - 与读取相同但功能码为0x05）
            command[10] = 0x05; // 写入变量
            command[11] = 0x01;
            command[12] = 0x12;
            command[13] = 0x0A;
            command[14] = 0x10;
            command[15] = transportSize;

            command[16] = (byte)((elementCount >> 8) & 0xFF);
            command[17] = (byte)(elementCount & 0xFF);

            command[18] = (byte)((addrData.DBNumber >> 8) & 0xFF);
            command[19] = (byte)(addrData.DBNumber & 0xFF);
            command[20] = addrData.Area;

            int offset = addrData.ByteOffset;
            if (addrData.TransportSize == S7TransportSize.Bit)
            {
                offset = addrData.ByteOffset * 8 + addrData.BitOffset;
            }
            command[21] = (byte)((offset >> 16) & 0xFF);
            command[22] = (byte)((offset >> 8) & 0xFF);
            command[23] = (byte)(offset & 0xFF);

            // 数据头部
            command[24] = 0x00; // 返回码
            command[25] = transportSize;

            // 数据长度（大端序）
            int dataLenField = paddedLength;
            if (addrData.TransportSize == S7TransportSize.Bit)
                dataLenField = 1; // 位写入使用1

            command[26] = (byte)((dataLenField >> 8) & 0xFF);
            command[27] = (byte)(dataLenField & 0xFF);

            // 数据
            Buffer.BlockCopy(data, 0, command, 28, data.Length);

            return command;
        }

        // =====================================================================
        // 响应检查
        // =====================================================================

        /// <inheritdoc/>
        protected override OperateResult<byte[]> CheckResponse(byte[] command, byte[] response)
        {
            if (response.Length < 12)
                return OperateResult.Fail<byte[]>($"S7 response too short: {response.Length}");

            // 检查协议ID
            if (response[0] != 0x32)
                return OperateResult.Fail<byte[]>($"Invalid S7 protocol ID: 0x{response[0]:X2}");

            // 检查消息类型
            byte msgType = response[1];
            if (msgType == 0x02)
            {
                // S7 Ack，可能是错误
                if (response.Length >= 12)
                {
                    byte errClass = response[10];
                    byte errCode = response[11];
                    return OperateResult.Fail<byte[]>(
                        $"S7 error: class=0x{errClass:X2}, code=0x{errCode:X2} - {GetS7ErrorDescription(errClass, errCode)}");
                }
                return OperateResult.Fail<byte[]>("S7 error response (type 0x02)");
            }
            
            if (msgType != 0x03)
                return OperateResult.Fail<byte[]>($"Unexpected S7 message type: 0x{msgType:X2}");

            // 获取参数长度和数据长度
            int paramLen = (response[6] << 8) | response[7];
            int dataLen = (response[8] << 8) | response[9];

            // 检查数据区域的返回码
            int dataStart = 10 + paramLen;
            if (dataStart + 1 > response.Length)
                return OperateResult.Fail<byte[]>("S7 response missing data area");

            // 根据命令判断是读取还是写入响应
            bool isRead = command.Length > 10 && command[10] == 0x04;

            if (isRead)
            {
                // 读取响应数据区域：
                // [0]: 返回码（0xFF = 成功）
                // [1]: 传输大小
                // [2-3]: 数据长度
                // [4+]: 数据
                if (response[dataStart] != 0xFF)
                    return OperateResult.Fail<byte[]>(
                        $"S7 read error: return code 0x{response[dataStart]:X2} - {GetS7DataErrorDescription(response[dataStart])}");

                int respDataLen = (response[dataStart + 2] << 8) | response[dataStart + 3];
                int actualDataStart = dataStart + 4;

                if (actualDataStart + respDataLen > response.Length)
                    respDataLen = response.Length - actualDataStart;

                if (respDataLen <= 0)
                    return OperateResult.Success(Array.Empty<byte>());

                byte[] result = new byte[respDataLen];
                Buffer.BlockCopy(response, actualDataStart, result, 0, respDataLen);
                return OperateResult.Success(result);
            }
            else
            {
                // 写入响应：检查返回码
                if (response[dataStart] != 0xFF)
                    return OperateResult.Fail<byte[]>(
                        $"S7 write error: return code 0x{response[dataStart]:X2} - {GetS7DataErrorDescription(response[dataStart])}");

                return OperateResult.Success(Array.Empty<byte>());
            }
        }

        /// <inheritdoc/>
        protected override int GetResponseLength(byte[] header)
        {
            if (header.Length < 4) return 0;
            // TPKT长度字段（大端序），包含自身4字节
            return (header[2] << 8) | header[3];
        }

        // =====================================================================
        // 位操作 - S7通过传输大小BIT支持原生位访问
        // =====================================================================

        /// <inheritdoc/>
        public override async Task<OperateResult<bool>> ReadBoolAsync(string address)
        {
            // 确保地址有位后缀
            string addr = address;
            if (!addr.Contains('.'))
                addr += ".0"; // 默认为位0

            var read = await ReadAsync(addr, 1);
            if (!read.IsSuccess)
                return OperateResult.Fail<bool>(read.Message, read.ErrorCode);

            return read.Content.Length > 0
                ? OperateResult.Success(read.Content[0] != 0)
                : OperateResult.Fail<bool>("Empty response");
        }

        /// <inheritdoc/>
        public override async Task<OperateResult> WriteAsync(string address, bool value)
        {
            string addr = address;
            if (!addr.Contains('.'))
                addr += ".0";

            byte[] data = value ? new byte[] { 0x01 } : new byte[] { 0x00 };
            return await WriteAsync(addr, data);
        }

        /// <summary>获取协商后的PDU长度，用于参考信息。</summary>
        public int PduLength => _pduLength;

        // =====================================================================
        // S7 错误描述
        // =====================================================================

        /// <summary>获取S7通信错误描述（消息类型0x02的错误码）。</summary>
        private static string GetS7ErrorDescription(byte errClass, byte errCode)
        {
            return errClass switch
            {
                0x81 => $"应用关系错误 (0x{errCode:X2})",
                0x82 => $"对象定义错误 (0x{errCode:X2})",
                0x83 => $"无可用资源 (0x{errCode:X2})",
                0x84 => $"服务处理错误 (0x{errCode:X2})",
                0x85 => $"请求错误 (0x{errCode:X2})",
                0x87 => $"访问错误 (0x{errCode:X2})",
                _ => $"未知错误类 0x{errClass:X2}, 代码 0x{errCode:X2}"
            };
        }

        /// <summary>获取S7数据区域返回码描述。</summary>
        private static string GetS7DataErrorDescription(byte returnCode)
        {
            return returnCode switch
            {
                0x00 => "保留",
                0x01 => "硬件错误",
                0x03 => "访问对象不允许",
                0x05 => "地址超出范围",
                0x06 => "数据类型不支持",
                0x07 => "数据长度超出范围",
                0x0A => "对象不存在",
                0xFF => "成功",
                _ => $"未知返回码 0x{returnCode:X2}"
            };
        }
    }
}
