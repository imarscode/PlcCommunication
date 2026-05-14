using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PlcCommunication.Simulation
{
    /// <summary>
    /// PLC模拟器基类，提供TCP服务器基础功能。
    /// </summary>
    public abstract class PlcSimulatorBase : IDisposable
    {
        protected TcpListener? _listener;
        protected CancellationTokenSource? _cts;
        protected Task? _acceptTask;
        protected ConcurrentDictionary<TcpClient, Task> _clients = new();
        
        public int Port { get; }
        public bool IsRunning { get; protected set; }
        public string Name { get; }

        protected PlcSimulatorBase(string name, int port)
        {
            Name = name;
            Port = port;
        }

        public void Start()
        {
            if (IsRunning) return;
            
            _listener = new TcpListener(IPAddress.Any, Port);
            _cts = new CancellationTokenSource();
            _listener.Start();
            IsRunning = true;
            _acceptTask = AcceptLoop(_cts.Token);
            Console.WriteLine($"[{Name}] Started on port {Port}");
        }

        public void Stop()
        {
            if (!IsRunning) return;
            
            _cts?.Cancel();
            _listener?.Stop();
            
            // 关闭所有客户端连接
            foreach (var kvp in _clients)
            {
                try { kvp.Key.Close(); } catch { }
            }
            _clients.Clear();
            
            IsRunning = false;
            Console.WriteLine($"[{Name}] Stopped");
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener!.AcceptTcpClientAsync();
                    var clientTask = HandleClient(client, ct);
                    _clients[client] = clientTask;
                    
                    // 清理已完成的客户端
                    _ = clientTask.ContinueWith(_ => 
                    {
                        _clients.TryRemove(client, out _);
                    }, TaskScheduler.Default);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{Name}] Accept error: {ex.Message}");
                }
            }
        }

        protected abstract Task HandleClient(TcpClient client, CancellationToken ct);

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 三菱MC协议模拟器（3E二进制帧）。
    /// 支持D、M、X、Y等设备区域的读写。
    /// </summary>
    public class MitsubishiMcSimulator : PlcSimulatorBase
    {
        // 数据存储
        private readonly byte[] _dRegisters = new byte[32768];  // D寄存器，16384字
        private readonly byte[] _mRegisters = new byte[16384];   // M继电器，位寻址
        private readonly byte[] _xRegisters = new byte[2048];    // X输入
        private readonly byte[] _yRegisters = new byte[2048];    // Y输出

        public MitsubishiMcSimulator(int port = 5006) : base("MitsubishiMC", port)
        {
            // 初始化一些测试数据
            _dRegisters[0] = 0x12; _dRegisters[1] = 0x34;  // D0 = 0x3412
            _dRegisters[2] = 0x56; _dRegisters[3] = 0x78;  // D1 = 0x7856
            _dRegisters[4] = 0xAB; _dRegisters[5] = 0xCD;  // D2 = 0xCDAB
            _dRegisters[200] = 0x11; _dRegisters[201] = 0x22; // D100 = 0x2211
        }

        protected override async Task HandleClient(TcpClient client, CancellationToken ct)
        {
            Console.WriteLine($"[{Name}] Client connected: {client.Client.RemoteEndPoint}");
            var stream = client.GetStream();
            var buffer = new byte[4096];

            try
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    // 读取请求
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read == 0) break;

                    // 解析并处理请求
                    var response = ProcessRequest(buffer, read);
                    if (response != null)
                    {
                        await stream.WriteAsync(response, 0, response.Length, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Name}] Client error: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine($"[{Name}] Client disconnected");
            }
        }

        private byte[]? ProcessRequest(byte[] request, int length)
        {
            if (length < 11) return null;

            // 解析3E帧头
            byte subHeader1 = request[0];  // 0x50
            byte subHeader2 = request[1];  // 0x00(读) 或 0x01(写)
            
            if (subHeader1 != 0x50) return null;

            // 读取请求数据长度
            int requestDataLen = request[7] | (request[8] << 8);
            
            // 命令和数据在偏移11之后
            byte cmdLow = request[11];
            byte cmdHigh = request[12];
            byte devCode = request[13];
            byte subCode = request[14];
            int address = request[15] | (request[16] << 8) | (request[17] << 16);
            int count = request[18] | (request[19] << 8);

            Console.WriteLine($"[{Name}] Request: cmd=0x{cmdLow:X2}{cmdHigh:X2}, dev=0x{devCode:X2}, addr={address}, count={count}");

            if (subHeader2 == 0x00) // 读命令
            {
                return BuildReadResponse(devCode, subCode, address, count);
            }
            else if (subHeader2 == 0x01) // 写命令
            {
                return BuildWriteResponse(devCode, subCode, address, count, request, 20);
            }

            return BuildErrorResponse(0xC002); // 无法识别的命令
        }

        private byte[] BuildReadResponse(byte devCode, byte subCode, int address, int count)
        {
            // 获取数据区域
            byte[]? data = GetDataArea(devCode);
            if (data == null)
            {
                return BuildErrorResponse(0xC102); // 设备代码错误
            }

            // 检查地址范围
            int byteOffset = address * 2;
            int byteCount = count * 2;
            
            if (byteOffset + byteCount > data.Length)
            {
                return BuildErrorResponse(0xC104); // 地址超出范围
            }

            // 构建成功响应
            // 头部(9) + 数据
            byte[] response = new byte[9 + byteCount];
            
            // 子头部
            response[0] = 0xD0;
            response[1] = 0x00;
            
            // 网络号等（原样返回）
            response[2] = 0x00;
            response[3] = 0xFF;
            response[4] = 0x03;
            response[5] = 0xFF;
            response[6] = 0x00;
            
            // 完成码
            response[7] = 0x00;
            response[8] = 0x00;

            // 数据
            Buffer.BlockCopy(data, byteOffset, response, 9, byteCount);

            Console.WriteLine($"[{Name}] Read response: {byteCount} bytes");
            return response;
        }

        private byte[] BuildWriteResponse(byte devCode, byte subCode, int address, int count, byte[] request, int dataOffset)
        {
            byte[]? data = GetDataArea(devCode);
            if (data == null)
            {
                return BuildErrorResponse(0xC102);
            }

            int byteOffset = address * 2;
            int byteCount = count * 2;

            if (byteOffset + byteCount > data.Length)
            {
                return BuildErrorResponse(0xC104);
            }

            // 写入数据
            Buffer.BlockCopy(request, dataOffset, data, byteOffset, byteCount);

            // 构建成功响应（只有头部，无数据）
            byte[] response = new byte[9];
            response[0] = 0xD0;
            response[1] = 0x01;
            response[2] = 0x00;
            response[3] = 0xFF;
            response[4] = 0x03;
            response[5] = 0xFF;
            response[6] = 0x00;
            response[7] = 0x00;
            response[8] = 0x00;

            Console.WriteLine($"[{Name}] Write response: OK");
            return response;
        }

        private byte[]? GetDataArea(byte devCode)
        {
            return devCode switch
            {
                0xA8 => _dRegisters,  // D
                0x90 => _mRegisters,  // M
                0x9C => _xRegisters,  // X
                0x9D => _yRegisters,  // Y
                _ => null
            };
        }

        private byte[] BuildErrorResponse(ushort errorCode)
        {
            byte[] response = new byte[9];
            response[0] = 0xD0;
            response[1] = 0x00;
            response[2] = 0x00;
            response[3] = 0xFF;
            response[4] = 0x03;
            response[5] = 0xFF;
            response[6] = 0x00;
            response[7] = (byte)(errorCode & 0xFF);
            response[8] = (byte)((errorCode >> 8) & 0xFF);
            
            Console.WriteLine($"[{Name}] Error response: 0x{errorCode:X4}");
            return response;
        }
    }

    /// <summary>
    /// 西门子S7协议模拟器。
    /// 支持M、I、Q、DB区域的读写。
    /// </summary>
    public class SiemensS7Simulator : PlcSimulatorBase
    {
        // 数据存储
        private readonly byte[] _mArea = new byte[16384];   // M区
        private readonly byte[] _iArea = new byte[8192];    // I区
        private readonly byte[] _qArea = new byte[8192];    // Q区
        private readonly byte[][] _dbAreas = new byte[256][]; // DB块

        private int _pduSize = 240;

        public SiemensS7Simulator(int port = 102) : base("SiemensS7", port)
        {
            // 初始化DB1
            _dbAreas[1] = new byte[65536];
            
            // 初始化测试数据
            _mArea[0] = 0x12; _mArea[1] = 0x34;  // MB0 = 0x12, MW0 = 0x3412
            _mArea[2] = 0x56; _mArea[3] = 0x78;  // MB1 = 0x34, MB2 = 0x56
            
            _dbAreas[1][0] = 0xAA;
            _dbAreas[1][1] = 0xBB;
            _dbAreas[1][2] = 0xCC;
            _dbAreas[1][3] = 0xDD;
        }

        protected override async Task HandleClient(TcpClient client, CancellationToken ct)
        {
            Console.WriteLine($"[{Name}] Client connected: {client.Client.RemoteEndPoint}");
            var stream = client.GetStream();
            var buffer = new byte[4096];

            try
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read == 0) break;

                    var response = ProcessRequest(buffer, read);
                    if (response != null)
                    {
                        await stream.WriteAsync(response, 0, response.Length, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Name}] Client error: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine($"[{Name}] Client disconnected");
            }
        }

        private byte[]? ProcessRequest(byte[] request, int length)
        {
            if (length < 4) return null;

            // TPKT长度
            int tpktLen = (request[2] << 8) | request[3];
            
            // COTP
            byte cotpLen = request[4];
            byte cotpType = request[5];

            // S7消息起始位置
            int s7Offset = 7; // TPKT(4) + COTP DT(3)

            if (cotpType == 0xE0) // COTP Connection Request
            {
                return BuildCotpConnectionResponse();
            }
            else if (cotpType == 0xF0) // COTP DT (Data)
            {
                byte s7Protocol = request[s7Offset];
                byte s7MsgType = request[s7Offset + 1];
                
                if (s7MsgType == 0x01) // Job Request
                {
                    byte pguType = request[s7Offset + 5];
                    
                    if (pguType == 0xF0) // Setup communication
                    {
                        return BuildPduSetupResponse(request, s7Offset);
                    }
                    else if (pguType == 0x04) // Read
                    {
                        return BuildReadResponse(request, s7Offset);
                    }
                    else if (pguType == 0x05) // Write
                    {
                        return BuildWriteResponse(request, s7Offset);
                    }
                }
            }

            Console.WriteLine($"[{Name}] Unknown request format");
            return null;
        }

        private byte[] BuildCotpConnectionResponse()
        {
            // TPKT + COTP CC (Connection Confirm)
            byte[] response = new byte[22];
            
            // TPKT
            response[0] = 0x03;
            response[1] = 0x00;
            response[2] = 0x00;
            response[3] = 0x16; // 22 bytes

            // COTP CC
            response[4] = 0x11; // Length
            response[5] = 0xD0; // CC
            response[6] = 0x00; // Dest ref
            response[7] = 0x01;
            response[8] = 0x00; // Src ref
            response[9] = 0x01;
            response[10] = 0x00; // Class
            
            // Source TSAP
            response[11] = 0xC1; // Parameter code
            response[12] = 0x02; // Length
            response[13] = 0x01; // TSAP
            response[14] = 0x00;
            
            // Dest TSAP
            response[15] = 0xC2;
            response[16] = 0x02;
            response[17] = 0x00;
            response[18] = 0x02;

            Console.WriteLine($"[{Name}] COTP Connection Response sent");
            return response;
        }

        private byte[] BuildPduSetupResponse(byte[] request, int s7Offset)
        {
            // 从请求中提取PDU大小
            if (request.Length > s7Offset + 7)
            {
                _pduSize = (request[s7Offset + 6] << 8) | request[s7Offset + 7];
            }

            // TPKT + COTP DT + S7 Response
            byte[] response = new byte[27];
            
            // TPKT
            response[0] = 0x03;
            response[1] = 0x00;
            response[2] = 0x00;
            response[3] = 0x1B; // 27 bytes

            // COTP DT
            response[4] = 0x02;
            response[5] = 0xF0;
            response[6] = 0x80;

            // S7 Header
            response[7] = 0x32; // Protocol ID
            response[8] = 0x03; // Response
            response[9] = 0x00; response[10] = 0x00; // Reserved
            response[11] = 0x00; response[12] = 0x00; // PDU Ref
            response[13] = 0x00; response[14] = 0x08; // Parameter length
            response[15] = 0x00; response[16] = 0x00; // Data length

            // PDU Setup
            response[17] = 0xF0; // Function
            response[18] = 0x00;
            response[19] = 0x00; response[20] = 0x01; // Max AMQ caller
            response[21] = 0x00; response[22] = 0x01; // Max AMQ callee
            response[23] = (byte)((_pduSize >> 8) & 0xFF);
            response[24] = (byte)(_pduSize & 0xFF);
            response[25] = 0x00;
            response[26] = 0x00;

            Console.WriteLine($"[{Name}] PDU Setup Response sent, PDU size={_pduSize}");
            return response;
        }

        private byte[] BuildReadResponse(byte[] request, int s7Offset)
        {
            // 解析读取项
            int paramLen = (request[s7Offset + 9] << 8) | request[s7Offset + 10];
            int itemCount = request[s7Offset + 13];

            var data = new System.Collections.Generic.List<byte>();
            int dataLen = 0;

            int itemOffset = s7Offset + 14;
            for (int i = 0; i < itemCount; i++)
            {
                byte areaType = request[itemOffset];
                byte lengthCode = request[itemOffset + 1];
                int dbNumber = (request[itemOffset + 2] << 8) | request[itemOffset + 3];
                int areaAddr = (request[itemOffset + 4] << 16) | (request[itemOffset + 5] << 8) | request[itemOffset + 6];
                areaAddr >>= 3; // 位地址转字节地址

                int readLen = lengthCode switch
                {
                    0x04 => 1, // Byte
                    0x05 => 2, // Word
                    0x06 => 4, // DWord
                    0x07 => 2, // Real
                    _ => 2
                };

                byte[]? areaData = GetAreaData(areaType, dbNumber);
                if (areaData == null || areaAddr + readLen > areaData.Length)
                {
                    // 错误响应
                    data.Add(0x0A); // Data error
                    data.Add(0x04); // Address not found
                    data.Add(0x00);
                }
                else
                {
                    data.Add(0xFF); // Success
                    for (int j = 0; j < readLen; j++)
                    {
                        data.Add(areaData[areaAddr + j]);
                    }
                }

                dataLen += readLen + 2;
                itemOffset += 12;
            }

            // 构建响应
            int totalLen = 17 + paramLen + 2 + data.Count;
            byte[] response = new byte[4 + 3 + totalLen];

            // TPKT
            response[0] = 0x03;
            response[1] = 0x00;
            response[2] = (byte)((totalLen + 7) >> 8);
            response[3] = (byte)((totalLen + 7) & 0xFF);

            // COTP DT
            response[4] = 0x02;
            response[5] = 0xF0;
            response[6] = 0x80;

            // S7 Header
            int idx = 7;
            response[idx++] = 0x32; // Protocol ID
            response[idx++] = 0x03; // Response
            response[idx++] = 0x00; response[idx++] = 0x00;
            response[idx++] = 0x00; response[idx++] = 0x00; // PDU Ref
            response[idx++] = (byte)((paramLen >> 8) & 0xFF);
            response[idx++] = (byte)(paramLen & 0xFF);
            response[idx++] = (byte)(((data.Count + 2) >> 8) & 0xFF);
            response[idx++] = (byte)((data.Count + 2) & 0xFF);

            // Parameter (copy from request)
            Buffer.BlockCopy(request, s7Offset + 7, response, idx, paramLen);
            idx += paramLen;

            // Data
            response[idx++] = 0x00; // Data header
            response[idx++] = (byte)itemCount;
            foreach (byte b in data)
            {
                response[idx++] = b;
            }

            Console.WriteLine($"[{Name}] Read response: {itemCount} items");
            return response;
        }

        private byte[] BuildWriteResponse(byte[] request, int s7Offset)
        {
            // 简化实现：总是返回成功
            int paramLen = (request[s7Offset + 9] << 8) | request[s7Offset + 10];
            int itemCount = request[s7Offset + 13];

            int totalLen = 17 + paramLen + 2 + itemCount;
            byte[] response = new byte[4 + 3 + totalLen];

            // TPKT
            response[0] = 0x03;
            response[1] = 0x00;
            response[2] = (byte)((totalLen + 7) >> 8);
            response[3] = (byte)((totalLen + 7) & 0xFF);

            // COTP DT
            response[4] = 0x02;
            response[5] = 0xF0;
            response[6] = 0x80;

            // S7 Header
            int idx = 7;
            response[idx++] = 0x32;
            response[idx++] = 0x03;
            response[idx++] = 0x00; response[idx++] = 0x00;
            response[idx++] = 0x00; response[idx++] = 0x00;
            response[idx++] = (byte)((paramLen >> 8) & 0xFF);
            response[idx++] = (byte)(paramLen & 0xFF);
            response[idx++] = 0x00;
            response[idx++] = (byte)(itemCount + 2);

            Buffer.BlockCopy(request, s7Offset + 7, response, idx, paramLen);
            idx += paramLen;

            response[idx++] = 0x00;
            response[idx++] = (byte)itemCount;
            for (int i = 0; i < itemCount; i++)
            {
                response[idx++] = 0xFF; // Success
            }

            Console.WriteLine($"[{Name}] Write response: OK");
            return response;
        }

        private byte[]? GetAreaData(byte areaType, int dbNumber)
        {
            return areaType switch
            {
                0x83 => _dbAreas[dbNumber] ?? _mArea, // DB (如果没有则返回M区)
                0x81 => _iArea,  // I
                0x82 => _qArea,  // Q
                0x1C => _mArea,  // M (常用)
                _ => null
            };
        }
    }

    /// <summary>
    /// Modbus TCP模拟器。
    /// </summary>
    public class ModbusTcpSimulator : PlcSimulatorBase
    {
        private readonly byte[] _coils = new byte[65536 / 8];
        private readonly byte[] _discreteInputs = new byte[65536 / 8];
        private readonly ushort[] _holdingRegisters = new ushort[65536];
        private readonly ushort[] _inputRegisters = new ushort[65536];
        
        // private ushort _transactionId; // 未使用

        public ModbusTcpSimulator(int port = 502) : base("ModbusTCP", port)
        {
            // 初始化测试数据
            _holdingRegisters[0] = 0x1234;
            _holdingRegisters[1] = 0x5678;
            _holdingRegisters[2] = 0xABCD;
        }

        protected override async Task HandleClient(TcpClient client, CancellationToken ct)
        {
            Console.WriteLine($"[{Name}] Client connected: {client.Client.RemoteEndPoint}");
            var stream = client.GetStream();
            var buffer = new byte[4096];

            try
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read == 0) break;

                    var response = ProcessRequest(buffer, read);
                    if (response != null)
                    {
                        await stream.WriteAsync(response, 0, response.Length, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Name}] Client error: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine($"[{Name}] Client disconnected");
            }
        }

        private byte[]? ProcessRequest(byte[] request, int length)
        {
            if (length < 8) return null;

            // MBAP Header
            ushort transId = (ushort)((request[0] << 8) | request[1]);
            ushort protocolId = (ushort)((request[2] << 8) | request[3]);
            ushort msgLen = (ushort)((request[4] << 8) | request[5]);
            byte unitId = request[6];
            byte functionCode = request[7];

            Console.WriteLine($"[{Name}] Request: trans={transId}, func=0x{functionCode:X2}, unit={unitId}");

            switch (functionCode)
            {
                case 0x01: // Read Coils
                case 0x02: // Read Discrete Inputs
                    return BuildBitReadResponse(request, functionCode == 0x01 ? _coils : _discreteInputs);
                    
                case 0x03: // Read Holding Registers
                case 0x04: // Read Input Registers
                    return BuildRegisterReadResponse(request, functionCode == 0x03 ? _holdingRegisters : _inputRegisters);
                    
                case 0x05: // Write Single Coil
                    return BuildSingleWriteResponse(request);
                    
                case 0x06: // Write Single Register
                    return BuildSingleWriteResponse(request);
                    
                case 0x0F: // Write Multiple Coils
                case 0x10: // Write Multiple Registers
                    return BuildMultipleWriteResponse(request);

                default:
                    return BuildErrorResponse(request, 0x01); // Illegal function
            }
        }

        private byte[] BuildBitReadResponse(byte[] request, byte[] data)
        {
            ushort startAddr = (ushort)((request[8] << 8) | request[9]);
            ushort quantity = (ushort)((request[10] << 8) | request[11]);
            byte byteCount = (byte)((quantity + 7) / 8);

            byte[] response = new byte[9 + byteCount];
            
            // MBAP Header
            response[0] = request[0];
            response[1] = request[1];
            response[2] = 0x00;
            response[3] = 0x00;
            response[4] = (byte)((3 + byteCount) >> 8);
            response[5] = (byte)((3 + byteCount) & 0xFF);
            response[6] = request[6];
            response[7] = request[7];
            response[8] = byteCount;

            // Data
            int byteOffset = startAddr / 8;
            for (int i = 0; i < byteCount; i++)
            {
                response[9 + i] = data[byteOffset + i];
            }

            return response;
        }

        private byte[] BuildRegisterReadResponse(byte[] request, ushort[] registers)
        {
            ushort startAddr = (ushort)((request[8] << 8) | request[9]);
            ushort quantity = (ushort)((request[10] << 8) | request[11]);
            byte byteCount = (byte)(quantity * 2);

            byte[] response = new byte[9 + byteCount];
            
            // MBAP Header
            response[0] = request[0];
            response[1] = request[1];
            response[2] = 0x00;
            response[3] = 0x00;
            response[4] = (byte)((3 + byteCount) >> 8);
            response[5] = (byte)((3 + byteCount) & 0xFF);
            response[6] = request[6];
            response[7] = request[7];
            response[8] = byteCount;

            // Data (big-endian)
            for (int i = 0; i < quantity; i++)
            {
                ushort val = registers[startAddr + i];
                response[9 + i * 2] = (byte)((val >> 8) & 0xFF);
                response[9 + i * 2 + 1] = (byte)(val & 0xFF);
            }

            Console.WriteLine($"[{Name}] Read {quantity} registers from {startAddr}");
            return response;
        }

        private byte[] BuildSingleWriteResponse(byte[] request)
        {
            byte[] response = new byte[12];
            Buffer.BlockCopy(request, 0, response, 0, 12);
            Console.WriteLine($"[{Name}] Write single OK");
            return response;
        }

        private byte[] BuildMultipleWriteResponse(byte[] request)
        {
            ushort startAddr = (ushort)((request[8] << 8) | request[9]);
            ushort quantity = (ushort)((request[10] << 8) | request[11]);

            if (request[7] == 0x10) // Write registers
            {
                int dataOffset = 13;
                for (int i = 0; i < quantity; i++)
                {
                    _holdingRegisters[startAddr + i] = (ushort)((request[dataOffset + i * 2] << 8) | request[dataOffset + i * 2 + 1]);
                }
            }

            byte[] response = new byte[12];
            Buffer.BlockCopy(request, 0, response, 0, 12);
            Console.WriteLine($"[{Name}] Write multiple OK");
            return response;
        }

        private byte[] BuildErrorResponse(byte[] request, byte errorCode)
        {
            byte[] response = new byte[9];
            Buffer.BlockCopy(request, 0, response, 0, 7);
            response[7] = (byte)(request[7] | 0x80);
            response[8] = errorCode;
            Console.WriteLine($"[{Name}] Error: 0x{errorCode:X2}");
            return response;
        }
    }
}
