using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlcCommunication.Core;
using PlcCommunication.DataConvert;
using PlcCommunication.Diagnostics;
using PlcCommunication.Utilities;

namespace PlcCommunication.Protocols.AllenBradley
{
    /// <summary>
    /// Allen-Bradley CIP（EtherNet/IP）协议客户端。
    /// 支持ControlLogix、CompactLogix、MicroLogix和SLC系列PLC。
    /// 使用基于标签的寻址（例如"MyTag"、"MyTag[0].Member"）。
    /// 默认端口：44818。
    /// 
    /// EIP封装头部所有多字节字段使用大端序（网络字节序）。
    /// </summary>
    public class AllenBradleyCipNet : NetworkDeviceBase
    {
        private uint _sessionHandle;

        /// <summary>CIP会话注册的连接超时时间。</summary>
        public int SessionTimeout { get; set; } = 10000;

        /// <summary>PLC槽号（用于背板/机架式PLC）。默认0。</summary>
        public byte Slot { get; set; } = 0;

        public AllenBradleyCipNet(string ipAddress, int port = 44818)
        {
            IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            ByteTransform = new RegularBytesTransform();
            ResponseHeaderLength = 24; // EIP封装头部
            ConnectTimeout = 10000;
        }

        public AllenBradleyCipNet() : this("127.0.0.1") { }

        // =====================================================================
        // 连接 - TCP + 会话注册
        // =====================================================================

        /// <inheritdoc/>
        public override async Task<OperateResult> ConnectAsync()
        {
            if (IsConnected)
                return OperateResult.Success();

            try
            {
                Trace(TraceLevel.Info, $"Connecting to AB CIP at {IpAddress}:{Port}...");

                _tcpClient?.Close();
                _tcpClient = new TcpClient();

                var connectTask = _tcpClient.ConnectAsync(IpAddress, Port);
                var timeoutTask = Task.Delay(ConnectTimeout);
                var completed = await Task.WhenAny(connectTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    CleanupConnection();
                    return OperateResult.Fail("CIP TCP connection timeout", -1001);
                }
                await connectTask;
                _stream = _tcpClient.GetStream();

                // 注册CIP会话
                var regResult = await RegisterSession();
                if (!regResult.IsSuccess)
                {
                    CleanupConnection();
                    return regResult;
                }

                IsConnected = true;
                Trace(TraceLevel.Info, $"CIP connected to {IpAddress}:{Port}, session=0x{_sessionHandle:X8}");
                return OperateResult.Success();
            }
            catch (Exception ex)
            {
                CleanupConnection();
                Trace(TraceLevel.Error, $"CIP connection failed: {ex.Message}");
                return OperateResult.Fail($"CIP connection failed: {ex.Message}", ex, -1000);
            }
        }

        private async Task<OperateResult> RegisterSession()
        {
            try
            {
                // 注册会话请求
                byte[] request = BuildEIPHeader(0x0065, 4);
                request[24] = 0x01; // 协议版本低字节
                request[25] = 0x00; // 协议版本高字节
                request[26] = 0x00;
                request[27] = 0x00;

                Trace(TraceLevel.Verbose, $"[CIP Register Session]");
                using var sendCts = new CancellationTokenSource(SendTimeout);
                await _stream!.WriteAsync(request, 0, request.Length, sendCts.Token);
                await _stream.FlushAsync(sendCts.Token);

                // 读取响应（28字节：24头部 + 4数据）
                byte[] response = new byte[28];
                using var recvCts = new CancellationTokenSource(ReceiveTimeout);
                int read = await ReadStreamAsync(_stream, response, 0, 28, recvCts.Token);

                if (read < 28)
                    return OperateResult.Fail("Short CIP Register Session response");

                // 提取会话句柄（字节4-7，大端序）
                _sessionHandle = (uint)((response[4] << 24) | (response[5] << 16) | 
                                        (response[6] << 8) | response[7]);

                // 检查状态（字节8-11，大端序）
                uint status = (uint)((response[8] << 24) | (response[9] << 16) | 
                                     (response[10] << 8) | response[11]);
                if (status != 0)
                    return OperateResult.Fail($"CIP session registration failed: status=0x{status:X8}");

                Trace(TraceLevel.Info, $"CIP session registered: 0x{_sessionHandle:X8}");
                return OperateResult.Success();
            }
            catch (OperationCanceledException)
            {
                return OperateResult.Fail("CIP session registration timeout", -1001);
            }
            catch (Exception ex)
            {
                return OperateResult.Fail($"CIP session registration failed: {ex.Message}", ex, -1000);
            }
        }

        // =====================================================================
        // 命令构建
        // =====================================================================

        /// <inheritdoc/>
        protected override byte[] BuildReadCommand(string address, ushort length)
        {
            string tagName = ParseTagName(address);
            int elementCount = 1; // 默认：读取1个元素
            int tagLen = tagName.Length;

            // 构建CIP请求：服务(1) + 路径大小(1) + 路径(可变) + 元素计数(2)
            // 路径 = 0x91（符号）+ 标签长度 + ASCII字节

            // 计算路径大小（以字为单位，向上取整）
            int pathBodyLen = 2 + tagLen; // 0x91 + 标签长度 + 标签名称
            int pathWords = (pathBodyLen + 1) / 2; // 向上取整到偶数个字节
            int pathPaddedLen = pathWords * 2;

            // CIP消息
            int cipMsgLen = 1 + 1 + pathPaddedLen + 2; // 服务 + 路径大小 + 路径 + 元素计数
            byte[] cipMsg = new byte[cipMsgLen];
            int idx = 0;
            cipMsg[idx++] = 0x4C; // 服务：读取数据
            cipMsg[idx++] = (byte)pathWords; // 路径大小（以字为单位）

            // 逻辑段：符号标签
            cipMsg[idx++] = 0x91; // ANSI扩展符号段
            cipMsg[idx++] = (byte)tagLen; // 标签名称长度
            byte[] tagBytes = Encoding.ASCII.GetBytes(tagName);
            Buffer.BlockCopy(tagBytes, 0, cipMsg, idx, tagLen);
            idx += tagLen;

            // 填充到字边界
            int padding = pathPaddedLen - pathBodyLen;
            if (padding > 0)
            {
                cipMsg[idx++] = 0x00;
            }

            // 元素计数（小端序 - CIP数据部分使用小端序）
            cipMsg[idx++] = (byte)(elementCount & 0xFF);
            cipMsg[idx++] = (byte)((elementCount >> 8) & 0xFF);

            // 封装到SendRRData命令中
            return BuildSendRRData(cipMsg);
        }

        /// <inheritdoc/>
        protected override byte[] BuildWriteCommand(string address, byte[] data)
        {
            string tagName = ParseTagName(address);
            int tagLen = tagName.Length;

            // 路径
            int pathBodyLen = 2 + tagLen;
            int pathWords = (pathBodyLen + 1) / 2;
            int pathPaddedLen = pathWords * 2;

            // CIP消息：服务(1) + 路径大小(1) + 路径(可变) + 元素计数(2) + 数据
            int cipMsgLen = 1 + 1 + pathPaddedLen + 2 + data.Length;
            byte[] cipMsg = new byte[cipMsgLen];
            int idx = 0;
            cipMsg[idx++] = 0x52; // 服务：写入数据
            cipMsg[idx++] = (byte)pathWords;

            cipMsg[idx++] = 0x91;
            cipMsg[idx++] = (byte)tagLen;
            byte[] tagBytes = Encoding.ASCII.GetBytes(tagName);
            Buffer.BlockCopy(tagBytes, 0, cipMsg, idx, tagLen);
            idx += tagLen;

            int padding = pathPaddedLen - pathBodyLen;
            if (padding > 0)
                cipMsg[idx++] = 0x00;

            // 元素计数
            cipMsg[idx++] = 0x01; // 1个元素
            cipMsg[idx++] = 0x00;

            // 数据
            Buffer.BlockCopy(data, 0, cipMsg, idx, data.Length);

            return BuildSendRRData(cipMsg);
        }

        /// <summary>构建封装了CIP消息的EIP SendRRData（0x006F）命令。</summary>
        private byte[] BuildSendRRData(byte[] cipMessage)
        {
            // 封装头部（24字节） + SendRRData特定内容
            // SendRRData封装了接口句柄 + 超时 + CIP消息

            // 用于无连接发送：
            // [EIP头部] [接口句柄(4)] [超时(4)] [CIP消息]
            int totalLen = 24 + 4 + 4 + cipMessage.Length;
            byte[] packet = new byte[totalLen];

            // SendRRData的EIP头部
            byte[] header = BuildEIPHeader(0x006F, totalLen - 24);
            Buffer.BlockCopy(header, 0, packet, 0, 24);

            // 接口句柄（未连接时通常为0，小端序）
            packet[24] = 0x00; packet[25] = 0x00;
            packet[26] = 0x00; packet[27] = 0x00;

            // 超时（毫秒，小端序 - 此字段为接口超时，使用小端序）
            int timeout = 3000;
            packet[28] = (byte)(timeout & 0xFF);
            packet[29] = (byte)((timeout >> 8) & 0xFF);
            packet[30] = (byte)((timeout >> 16) & 0xFF);
            packet[31] = (byte)((timeout >> 24) & 0xFF);

            // CIP消息
            Buffer.BlockCopy(cipMessage, 0, packet, 32, cipMessage.Length);

            return packet;
        }

        /// <summary>
        /// 构建24字节的EIP封装头部。
        /// 所有字段使用大端序（网络字节序），这是EIP规范要求。
        /// </summary>
        private byte[] BuildEIPHeader(ushort command, int dataLength)
        {
            byte[] header = new byte[24];

            // 命令码（大端序）
            header[0] = (byte)((command >> 8) & 0xFF);
            header[1] = (byte)(command & 0xFF);

            // 数据长度（大端序）
            header[2] = (byte)((dataLength >> 8) & 0xFF);
            header[3] = (byte)(dataLength & 0xFF);

            // 会话句柄（大端序）
            header[4] = (byte)((_sessionHandle >> 24) & 0xFF);
            header[5] = (byte)((_sessionHandle >> 16) & 0xFF);
            header[6] = (byte)((_sessionHandle >> 8) & 0xFF);
            header[7] = (byte)(_sessionHandle & 0xFF);

            // 状态（全零）
            // 发送方上下文（全零）
            // 选项（全零）
            // 所有字段均已初始化为零

            return header;
        }

        // =====================================================================
        // 响应检查
        // =====================================================================

        /// <inheritdoc/>
        protected override OperateResult<byte[]> CheckResponse(byte[] command, byte[] response)
        {
            if (response.Length < 28)
                return OperateResult.Fail<byte[]>($"CIP response too short: {response.Length}");

            // 检查EIP状态（字节8-11，大端序）
            uint eipStatus = (uint)((response[8] << 24) | (response[9] << 16) | 
                                     (response[10] << 8) | response[11]);
            if (eipStatus != 0)
                return OperateResult.Fail<byte[]>($"EIP error: status=0x{eipStatus:X8}");

            // CIP消息从偏移量32开始（24 EIP头部 + 4接口句柄 + 4超时）
            int cipOffset = 32;
            if (cipOffset + 4 > response.Length)
                return OperateResult.Fail<byte[]>("CIP response missing CIP data");

            // CIP回复：服务(1) + 保留(1) + 通用状态(1) + 扩展状态字数(1) + [扩展状态] + 数据
            byte generalStatus = response[cipOffset + 2];
            byte extStatusWords = response[cipOffset + 3]; // 扩展状态字数

            if (generalStatus != 0)
            {
                string errorDesc = GetCipErrorDescription(generalStatus);
                // 读取扩展状态以提供更多错误信息
                if (extStatusWords > 0 && cipOffset + 4 + extStatusWords * 2 <= response.Length)
                {
                    ushort extStatus = (ushort)(response[cipOffset + 4] | (response[cipOffset + 5] << 8));
                    errorDesc += $" (ext: 0x{extStatus:X4})";
                }
                return OperateResult.Fail<byte[]>($"CIP error: 0x{generalStatus:X2} - {errorDesc}", -generalStatus);
            }

            // 数据起始位置：Service(1) + Reserved(1) + Status(1) + ExtStatusWords(1) + ExtStatus(N*2)
            int dataStart = cipOffset + 4 + (extStatusWords * 2);
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
            // EIP头部长度字段为大端序
            return (header[2] << 8) | header[3];
        }

        // =====================================================================
        // 地址解析
        // =====================================================================

        /// <summary>从地址字符串中提取标签名称（移除数组/括号表示法用于路径）。</summary>
        private static string ParseTagName(string address)
        {
            if (string.IsNullOrEmpty(address))
                throw new PlcCommunicationException("CIP tag address cannot be empty");

            // 目前直接将地址作为标签名称使用
            // 完整的数组/成员支持需要更复杂的EPATH编码
            return address.Trim();
        }

        /// <summary>获取CIP通用状态码描述。</summary>
        private static string GetCipErrorDescription(byte statusCode)
        {
            return statusCode switch
            {
                0x00 => "成功",
                0x01 => "连接失败",
                0x02 => "资源不可用",
                0x03 => "无效参数值",
                0x04 => "路径段错误",
                0x05 => "路径目的地未知",
                0x06 => "部分传输",
                0x07 => "连接丢失",
                0x08 => "服务不支持",
                0x09 => "无效属性值",
                0x0A => "属性列表错误",
                0x0B => "已处于请求的模式/状态",
                0x0C => "对象状态冲突",
                0x0D => "已存在",
                0x0E => "属性不支持",
                0x0F => "多个请求错误",
                0x10 => "无法修改属性",
                0x11 => "请求超长",
                0x12 => "服务不支持于对象",
                0x13 => "不一致数据",
                0x14 => "数据不存在",
                0x15 => "属性不支持获取",
                0x16 => "属性不支持设置",
                0x17 => "无效状态",
                0x18 => "超时",
                0x19 => "键在路径中不存在",
                0x1A => "路径超出段数限制",
                0x1B => "处理属性列表错误",
                0x1C => "位量不足",
                0x1D => "属性不支持获取单个",
                0x1E => "属性不支持设置单个",
                0x1F => "没有找到匹配项",
                0x20 => "异步错误",
                0x21 => "异步状态",
                0x22 => "异步触发器已启用",
                0xFF => "通用错误",
                _ => $"未知错误 (0x{statusCode:X2})"
            };
        }
    }
}
