using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlcCommunication.Core;
using PlcCommunication.DataConvert;
using PlcCommunication.Diagnostics;

namespace PlcCommunication.Protocols.Mitsubishi
{
    /// <summary>
    /// 三菱MC(Melsec)协议客户端，使用基于TCP的3E二进制帧。
    /// 支持Q、L、QnA和FX系列PLC。
    /// 默认端口：5006（TCP）。
    /// </summary>
    public class MitsubishiMcNet : NetworkDeviceBase
    {
        /// <summary>PLC网络编号。默认0。</summary>
        public byte NetworkNumber { get; set; } = 0;

        /// <summary>PLC编号。默认0xFF（未指定）。</summary>
        public byte PLCNumber { get; set; } = 0xFF;

        /// <summary>IO编号。默认0x03FF。</summary>
        public ushort IONumber { get; set; } = 0x03FF;

        /// <summary>站号。默认0。</summary>
        public byte StationNumber { get; set; } = 0;

        /// <summary>监控定时器，以100ms为单位。默认10（1秒）。</summary>
        public ushort Timer { get; set; } = 10;

        // 用于存储最后一次读取请求的点数，以便推断响应长度
        private int _lastReadWordCount;

        /// <summary>
        /// 三菱MC 3E二进制帧设备代码映射。
        /// 使用标准二进制编码，非ASCII值。
        /// </summary>
        private static byte GetDeviceCode(char device)
        {
            return device switch
            {
                'D' => 0xA8,   // 数据寄存器
                'M' => 0x90,   // 内部继电器
                'X' => 0x9C,   // 输入
                'Y' => 0x9D,   // 输出
                'L' => 0x92,   // 锁存继电器
                'F' => 0x93,   // 报警器
                'V' => 0x94,   // 边沿继电器
                'B' => 0xA0,   // 链接继电器
                'R' => 0xAF,   // 文件寄存器
                'S' => 0x98,   // 步进继电器
                'T' => 0xC2,   // 定时器当前值
                'C' => 0xC5,   // 计数器当前值
                'W' => 0xB4,   // 链接寄存器
                'Z' => 0xCC,   // 变址寄存器
                _ => 0xA8      // 默认为数据寄存器D
            };
        }

        /// <summary>
        /// 创建新的三菱MC客户端。
        /// </summary>
        public MitsubishiMcNet(string ipAddress, int port = 5006)
        {
            IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            ByteTransform = new RegularBytesTransform();
            ResponseHeaderLength = 9;
        }

        public MitsubishiMcNet() : this("127.0.0.1") { }

        /// <inheritdoc/>
        protected override byte[] BuildReadCommand(string address, ushort length)
        {
            var (devCode, subCode, devAddress) = ParseAddress(address);

            // 字读取：将字节数转换为字数（每个字2字节）
            int wordCount = (length + 1) / 2;
            if (wordCount < 1) wordCount = 1;

            // 保存用于推断响应长度
            _lastReadWordCount = wordCount;

            // 标准3E二进制帧布局：
            // 子头部(2) + 网络号(1) + PC号(1) + IO号(2) + 站号(1) + 
            // 请求数据长度(2) + 监控定时器(2) + 请求数据(8)
            byte[] command = new byte[19];
            int idx = 0;

            // 子头部：50 00（3E二进制读取）
            command[idx++] = 0x50;
            command[idx++] = 0x00;

            // 访问路径
            command[idx++] = NetworkNumber;
            command[idx++] = PLCNumber;
            command[idx++] = (byte)(IONumber & 0xFF);
            command[idx++] = (byte)((IONumber >> 8) & 0xFF);
            command[idx++] = StationNumber;

            // 请求数据长度（定时器2字节 + 请求数据8字节 = 10字节）
            command[idx++] = 0x0A;
            command[idx++] = 0x00;

            // 监控定时器
            command[idx++] = (byte)(Timer & 0xFF);
            command[idx++] = (byte)((Timer >> 8) & 0xFF);

            // 请求数据：子命令(2) + 设备代码(1) + 设备扩展(1) + 起始地址(3) + 点数(2)
            command[idx++] = 0x01; // 子命令低字节：字读取
            command[idx++] = 0x00;

            command[idx++] = devCode;
            command[idx++] = subCode;

            // 起始地址（3字节，小端序）
            command[idx++] = (byte)(devAddress & 0xFF);
            command[idx++] = (byte)((devAddress >> 8) & 0xFF);
            command[idx++] = (byte)((devAddress >> 16) & 0xFF);

            // 读取点数（小端序）
            command[idx++] = (byte)(wordCount & 0xFF);
            command[idx++] = (byte)((wordCount >> 8) & 0xFF);

            return command;
        }

        /// <inheritdoc/>
        protected override byte[] BuildWriteCommand(string address, byte[] data)
        {
            var (devCode, subCode, devAddress) = ParseAddress(address);

            int wordCount = (data.Length + 1) / 2;
            int paddedLen = wordCount * 2;
            if (wordCount < 1) wordCount = 1;

            // 写入响应没有数据区域，设置为0
            _lastReadWordCount = 0;

            byte[] paddedData = new byte[paddedLen];
            Buffer.BlockCopy(data, 0, paddedData, 0, data.Length);

            // 3E帧：头部(11) + 请求体(10 + 数据)
            int requestDataLen = 10 + paddedLen;
            byte[] command = new byte[11 + requestDataLen];
            int idx = 0;

            // 子头部：50 01（3E二进制写入）
            command[idx++] = 0x50;
            command[idx++] = 0x01;

            // 访问路径
            command[idx++] = NetworkNumber;
            command[idx++] = PLCNumber;
            command[idx++] = (byte)(IONumber & 0xFF);
            command[idx++] = (byte)((IONumber >> 8) & 0xFF);
            command[idx++] = StationNumber;

            // 请求数据长度
            command[idx++] = (byte)(requestDataLen & 0xFF);
            command[idx++] = (byte)((requestDataLen >> 8) & 0xFF);

            // 监控定时器
            command[idx++] = (byte)(Timer & 0xFF);
            command[idx++] = (byte)((Timer >> 8) & 0xFF);

            // 子命令：字写入
            command[idx++] = 0x01;
            command[idx++] = 0x00;

            command[idx++] = devCode;
            command[idx++] = subCode;

            // 起始地址（3字节，小端序）
            command[idx++] = (byte)(devAddress & 0xFF);
            command[idx++] = (byte)((devAddress >> 8) & 0xFF);
            command[idx++] = (byte)((devAddress >> 16) & 0xFF);

            // 写入点数
            command[idx++] = (byte)(wordCount & 0xFF);
            command[idx++] = (byte)((wordCount >> 8) & 0xFF);

            // 写入数据
            Buffer.BlockCopy(paddedData, 0, command, idx, paddedLen);

            return command;
        }

        /// <inheritdoc/>
        protected override OperateResult<byte[]> CheckResponse(byte[] command, byte[] response)
        {
            if (response.Length < 9)
                return OperateResult.Fail<byte[]>($"MC response too short: {response.Length}");

            // 完成码位于偏移量7-8（小端序）
            ushort completeCode = (ushort)(response[7] | (response[8] << 8));

            if (completeCode != 0)
            {
                string errorDesc = GetErrorDescription(completeCode);
                return OperateResult.Fail<byte[]>($"MC error: 0x{completeCode:X4} - {errorDesc}", -completeCode);
            }

            // 提取数据（从字节9开始）
            if (response.Length <= 9)
                return OperateResult.Success(Array.Empty<byte>());

            int dataLen = response.Length - 9;
            byte[] result = new byte[dataLen];
            Buffer.BlockCopy(response, 9, result, 0, dataLen);
            return OperateResult.Success(result);
        }

        /// <summary>
        /// 三菱3E协议响应没有长度字段，需要根据请求推断。
        /// 重写整个接收逻辑。
        /// </summary>
        protected override async Task<OperateResult<byte[]>> ReceiveAsync(CancellationToken ct)
        {
            if (_stream == null)
                return OperateResult.Fail<byte[]>("Not connected");

            try
            {
                // 先读取9字节头部
                byte[] header = new byte[9];
                int headerRead = await ReadStreamAsync(_stream, header, 0, 9, ct);
                if (headerRead != 9)
                    return OperateResult.Fail<byte[]>($"Expected 9 header bytes, got {headerRead}");

                // 检查完成码
                ushort completeCode = (ushort)(header[7] | (header[8] << 8));
                if (completeCode != 0)
                {
                    // 错误响应，无数据区域
                    return OperateResult.Success(header);
                }

                // 成功响应，需要读取数据区域
                // 数据长度 = 读取点数 * 2（每个字2字节）
                int expectedDataLen = _lastReadWordCount * 2;

                if (expectedDataLen > 0)
                {
                    byte[] data = new byte[expectedDataLen];
                    int dataRead = await ReadStreamAsync(_stream, data, 0, expectedDataLen, ct);
                    if (dataRead != expectedDataLen)
                        return OperateResult.Fail<byte[]>($"Expected {expectedDataLen} data bytes, got {dataRead}");

                    // 合并头部和数据
                    byte[] response = new byte[9 + expectedDataLen];
                    Buffer.BlockCopy(header, 0, response, 0, 9);
                    Buffer.BlockCopy(data, 0, response, 9, expectedDataLen);
                    return OperateResult.Success(response);
                }

                return OperateResult.Success(header);
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
            // 不使用基类的长度前缀逻辑
            return 0;
        }

        /// <summary>获取三菱MC错误码描述。</summary>
        private static string GetErrorDescription(ushort errorCode)
        {
            return errorCode switch
            {
                0xC001 => "无法识别的子头部",
                0xC002 => "无法识别的子命令",
                0xC003 => "通信方式错误",
                0xC004 => "无法识别的命令",
                0xC005 => "通信模式错误",
                0xC006 => "监视时间超出",
                0xC010 => "访问目标错误（网络号）",
                0xC011 => "访问目标错误（PC号）",
                0xC012 => "访问目标错误（请求目标模块IO号）",
                0xC013 => "访问目标错误（请求目标模块站号）",
                0xC020 => "访问目标重复启动",
                0xC021 => "无法执行（CPU模块运行中）",
                0xC022 => "无法执行（CPU模块调试中）",
                0xC023 => "无法执行（CPU模块停止中）",
                0xC024 => "无法执行（CPU模块出错）",
                0xC025 => "无法执行（其他站启动中）",
                0xC030 => "无法执行（模块通信断开）",
                0xC031 => "无法执行（其他站数据一致化处理中）",
                0xC040 => "无法执行（模块异常）",
                0xC050 => "模块信息获取错误",
                0xC060 => "无法执行（无法读取对象）",
                0xC061 => "无法执行（无法写入对象）",
                0xC062 => "无法执行（无法执行对象）",
                0xC100 => "参数不合法（命令数据长度）",
                0xC101 => "参数不合法（数据长度）",
                0xC102 => "参数不合法（设备代码）",
                0xC103 => "参数不合法（设备模式）",
                0xC104 => "参数不合法（设备编号/地址）",
                0xC105 => "参数不合法（点数）",
                0xC106 => "参数不合法（数据）",
                0xC200 => "写入禁止（写保护）",
                0xC201 => "写入禁止（模块运行中）",
                0xC202 => "写入禁止（模块停止中）",
                0xC300 => "读取/写入目标设备不存在",
                0xC301 => "读取/写入目标设备超出范围",
                0xC400 => "程序不存在（程序名不正确）",
                0xD001 => "通信被取消",
                0xD003 => "通信被取消（重试超过）",
                _ => $"未知错误码"
            };
        }

        private (byte devCode, byte subCode, int address) ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new PlcCommunicationException("MC address cannot be empty");

            address = address.Trim().ToUpperInvariant();
            
            if (address.Length == 0)
                throw new PlcCommunicationException("MC address cannot be empty");

            char device = address[0];
            string addrPart = address.Substring(1);

            // X和Y使用十六进制寻址
            if (device == 'X' || device == 'Y')
            {
                if (!int.TryParse(addrPart, System.Globalization.NumberStyles.HexNumber, null, out int hexAddr))
                    throw new PlcCommunicationException($"Invalid MC address: {address}");
                byte devCode = GetDeviceCode(device);
                return (devCode, (byte)0x01, hexAddr);
            }

            // 位设备带位偏移（如 M10.3）
            int dotIndex = addrPart.IndexOf('.');
            if (dotIndex >= 0)
            {
                string addrNum = addrPart.Substring(0, dotIndex);
                int bitOffset = int.Parse(addrPart.Substring(dotIndex + 1));
                if (!int.TryParse(addrNum, out int baseAddr))
                    throw new PlcCommunicationException($"Invalid MC address: {address}");
                
                byte devCode = GetDeviceCode(device);
                int fullAddr = baseAddr * 16 + bitOffset;
                return (devCode, (byte)0x01, fullAddr);
            }

            if (!int.TryParse(addrPart, out int addr))
                throw new PlcCommunicationException($"Invalid MC address: {address}");

            byte dCode = GetDeviceCode(device);
            return (dCode, (byte)0x00, addr);
        }
    }
}
