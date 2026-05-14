using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlcCommunication.Core;
using PlcCommunication.DataConvert;
using PlcCommunication.Utilities;

namespace PlcCommunication.Protocols.Omron
{
    /// <summary>
    /// Omron HostLink协议客户端（串行/RS-232C封装于TCP之上）。
    /// 使用带FCS校验和的ASCII命令。
    /// </summary>
    public class OmronHostLinkNet : NetworkDeviceBase
    {
        /// <summary>单元号（00-31）。默认00。</summary>
        public string UnitNumber { get; set; } = "00";

        /// <summary>头部代码："RR"表示读取，"WR"表示写入。</summary>
        private const string ReadHeader = "RR";
        private const string WriteHeader = "WR";

        public OmronHostLinkNet(string ipAddress, int port = 9600)
        {
            IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            ByteTransform = new ReverseBytesTransform();
            ResponseHeaderLength = 1;
        }

        public OmronHostLinkNet() : this("127.0.0.1") { }

        private byte[] BuildHostLinkFrame(string header, string data)
        {
            // @ + 单元号 + 头部 + 数据 + FCS + * + CR
            string frame = "@" + UnitNumber + header + data;
            byte[] frameBytes = Encoding.ASCII.GetBytes(frame);
            byte fcs = CalculateFCS(frameBytes);
            string fullFrame = frame + fcs.ToString("X2") + "*\r\n";
            return Encoding.ASCII.GetBytes(fullFrame);
        }

        private static byte CalculateFCS(byte[] data)
        {
            byte fcs = 0;
            foreach (byte b in data)
            {
                fcs ^= b;
            }
            return fcs;
        }

        /// <inheritdoc/>
        protected override byte[] BuildReadCommand(string address, ushort length)
        {
            var (areaCode, startAddress) = ParseAddress(address);

            // HostLink读取：@ + 单元号 + RR + 区域码 + 起始地址(4位十六进制) + 字节数(4位十六进制)
            string data = areaCode + startAddress.ToString("X4") + length.ToString("X4");
            return BuildHostLinkFrame(ReadHeader, data);
        }

        /// <inheritdoc/>
        protected override byte[] BuildWriteCommand(string address, byte[] data)
        {
            var (areaCode, startAddress) = ParseAddress(address);

            string hexData = BitConverter.ToString(data).Replace("-", "");
            string reqData = areaCode + startAddress.ToString("X4") + hexData;
            return BuildHostLinkFrame(WriteHeader, reqData);
        }

        /// <inheritdoc/>
        protected override async Task<OperateResult<byte[]>> ReceiveAsync(CancellationToken ct)
        {
            if (_stream == null)
                return OperateResult.Fail<byte[]>("Not connected");

            try
            {
                var buffer = new System.Collections.Generic.List<byte>();
                bool foundStart = false;

                while (!ct.IsCancellationRequested)
                {
                    byte[] oneByte = new byte[1];
                    int read = await ReadStreamAsync(_stream, oneByte, 0, 1, ct);
                    if (read == 0) break;

                    byte b = oneByte[0];

                    if (!foundStart)
                    {
                        if (b == '@')
                        {
                            foundStart = true;
                            buffer.Add(b);
                        }
                        continue;
                    }

                    buffer.Add(b);
                    if (b == '\n') break;
                }

                if (!foundStart || buffer.Count < 7)
                    return OperateResult.Fail<byte[]>("Invalid HostLink response");

                return DecodeHostLinkResponse(buffer.ToArray());
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

        private OperateResult<byte[]> DecodeHostLinkResponse(byte[] asciiFrame)
        {
            string frame = Encoding.ASCII.GetString(asciiFrame).Trim();
            // 格式：@ + 单元号 + 头部 + FC + 数据 + FCS + * + CR
            if (frame.Length < 7 || !frame.StartsWith("@") || !frame.Contains("*"))
                return OperateResult.Fail<byte[]>("Invalid HostLink format");

            int starIdx = frame.IndexOf('*');
            string body = frame.Substring(1, starIdx - 1); // 不包含 @、*、CR/LF

            if (body.Length < 6) // 单元号(2) + 头部(2) + FC(2) = 最小值
                return OperateResult.Fail<byte[]>("Invalid HostLink body");

            string unitNo = body.Substring(0, 2);
            string header = body.Substring(2, 2);
            string fcs = body.Substring(body.Length - 2); // 最后2个字符是FCS

            // 检查响应头部
            if (header == "RR" || header == "WR")
            {
                // 正常响应
            }

            // 提取数据（头部之后，FCS之前）
            string dataPart = body.Substring(4, body.Length - 6);
            byte[] result = SoftBasic.HexStringToBytes(dataPart);
            return OperateResult.Success(result);
        }

        /// <inheritdoc/>
        protected override OperateResult<byte[]> CheckResponse(byte[] command, byte[] response)
        {
            if (response == null)
                return OperateResult.Fail<byte[]>("Null response");
            return OperateResult.Success(response);
        }

        /// <inheritdoc/>
        protected override int GetResponseLength(byte[] header)
        {
            return 0; // 未使用
        }

        /// <summary>
        /// 解析Omron HostLink地址。
        /// DM地址："D100" -> 区域"DM"，地址100
        /// CIO地址："CIO100" -> 区域"CIO"，地址100
        /// </summary>
        private (string areaCode, int address) ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new PlcCommunicationException("HostLink address cannot be empty");

            address = address.Trim().ToUpperInvariant();
            
            if (address.Length == 0)
                throw new PlcCommunicationException("HostLink address cannot be empty");

            if (address.StartsWith("D"))
            {
                string num = address.Substring(1);
                return ("DM", int.Parse(num));
            }
            if (address.StartsWith("CIO"))
            {
                string num = address.Substring(3);
                return ("CIO", int.Parse(num));
            }
            if (address.StartsWith("W"))
            {
                string num = address.Substring(1);
                return ("WR", int.Parse(num));
            }
            if (address.StartsWith("H"))
            {
                string num = address.Substring(1);
                return ("HR", int.Parse(num));
            }
            if (address.StartsWith("A"))
            {
                string num = address.Substring(1);
                return ("AR", int.Parse(num));
            }

            throw new PlcCommunicationException($"Unknown HostLink area: {address}");
        }
    }
}
