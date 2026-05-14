using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PlcCommunication.DataConvert;
using PlcCommunication.Diagnostics;
using PlcCommunication.Utilities;

namespace PlcCommunication.Core
{
    /// <summary>
    /// 所有 PLC 协议客户端的抽象基类。处理连接生命周期、
    /// 线程安全（通过 SemaphoreSlim）、可配置超时、自动重连、
    /// 指数退避重试和诊断跟踪。
    /// </summary>
    public abstract class NetworkDeviceBase : IReadWriteNet, ITraceable, IDisposable
    {
        // ---- 连接配置 ----

        /// <summary>PLC IP 地址或主机名。</summary>
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>PLC TCP 端口。</summary>
        public int Port { get; set; } = 102;

        /// <summary>连接超时时间（毫秒）。默认 5000。</summary>
        public int ConnectTimeout { get; set; } = 5000;

        /// <summary>发送超时时间（毫秒）。默认 3000。</summary>
        public int SendTimeout { get; set; } = 3000;

        /// <summary>接收超时时间（毫秒）。默认 5000。</summary>
        public int ReceiveTimeout { get; set; } = 5000;

        /// <summary>失败时的重试次数。默认 3。</summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>重试之间的基础间隔（毫秒）。默认 100。</summary>
        /// <remarks>实际延迟 = RetryIntervalMs * 2^attempt（指数退避）。</remarks>
        public int RetryIntervalMs { get; set; } = 100;

        /// <summary>用于帧定界的响应头前缀长度。默认 4 字节。</summary>
        /// <remarks>如果协议对长度前缀帧定界使用不同的头大小，请重写此属性。</remarks>
        protected int ResponseHeaderLength { get; set; } = 4;

        // ---- 连接状态 ----

        /// <inheritdoc/>
        public bool IsConnected { get; protected set; }

        // ---- 数据转换 ----

        /// <summary>此协议的字节序转换器。</summary>
        public IByteTransform ByteTransform { get; set; } = new RegularBytesTransform();

        // ---- 同步 ----

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        /// <summary>用于连接的 TCP 客户端。派生类可访问。</summary>
        protected TcpClient? _tcpClient;
        /// <summary>用于数据 I/O 的网络流。派生类可访问。</summary>
        protected NetworkStream? _stream;

        // ---- IDisposable ----

        /// <summary>对象是否已释放。</summary>
        protected bool _disposed;

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>释放托管/非托管资源。</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                DisconnectInternal();
                _lock.Dispose();
            }
            _disposed = true;
        }

        // =====================================================================
        // 抽象方法（协议相关）
        // =====================================================================

        /// <summary>构建用于读取操作的原始协议请求字节。</summary>
        protected abstract byte[] BuildReadCommand(string address, ushort length);

        /// <summary>构建用于写入操作的原始协议请求字节。</summary>
        protected abstract byte[] BuildWriteCommand(string address, byte[] data);

        /// <summary>验证并从响应字节中提取数据。</summary>
        protected abstract OperateResult<byte[]> CheckResponse(byte[] command, byte[] response);

        /// <summary>从响应头中提取预期的总响应长度。</summary>
        protected abstract int GetResponseLength(byte[] header);

        // =====================================================================
        // 虚方法（可重写以自定义行为）
        // =====================================================================

        /// <summary>
        /// 从网络流中接收响应字节。
        /// 默认使用长度前缀帧定界：先读取 <see cref="ResponseHeaderLength"/>
        /// 字节，调用 <see cref="GetResponseLength"/>，然后读取剩余数据。
        /// </summary>
        protected virtual async Task<OperateResult<byte[]>> ReceiveAsync(CancellationToken ct)
        {
            if (_stream == null)
                return OperateResult.Fail<byte[]>("Not connected");

            try
            {
                // 读取响应头
                byte[] header = new byte[ResponseHeaderLength];
                int headerRead = await ReadStreamAsync(_stream, header, 0, ResponseHeaderLength, ct);
                if (headerRead != ResponseHeaderLength)
                    return OperateResult.Fail<byte[]>($"Expected {ResponseHeaderLength} header bytes, got {headerRead}");

                // 确定预期的响应长度
                int totalLength = GetResponseLength(header);
                if (totalLength <= 0)
                    return OperateResult.Fail<byte[]>("Invalid response length from protocol");

                // 如果 totalLength 小于等于头长度，则已读取全部数据
                if (totalLength <= ResponseHeaderLength)
                {
                    byte[] result = new byte[totalLength];
                    Buffer.BlockCopy(header, 0, result, 0, totalLength);
                    return OperateResult.Success(result);
                }

                // 读取剩余数据
                int remaining = totalLength - ResponseHeaderLength;
                byte[] body = new byte[remaining];
                int bodyRead = await ReadStreamAsync(_stream, body, 0, remaining, ct);
                if (bodyRead != remaining)
                    return OperateResult.Fail<byte[]>($"Expected {remaining} body bytes, got {bodyRead}");

                // 合并头 + 主体
                byte[] response = new byte[totalLength];
                Buffer.BlockCopy(header, 0, response, 0, ResponseHeaderLength);
                Buffer.BlockCopy(body, 0, response, ResponseHeaderLength, remaining);
                return OperateResult.Success(response);
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

        // =====================================================================
        // 核心通信
        // =====================================================================

        /// <summary>
        /// 发送命令并接收响应的核心方法。
        /// 管理锁定、连接、发送、接收和响应检查。
        /// </summary>
        protected virtual async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(byte[] command)
        {
            await _lock.WaitAsync();
            try
            {
                var timer = new SoftTimer();
                timer.Start();

                // 通过重试尝试操作
                OperateResult<byte[]>? result = null;
                for (int attempt = 0; attempt <= RetryCount; attempt++)
                {
                    // 必要时连接
                    if (!IsConnected)
                    {
                        var connectResult = await ConnectAsync();
                        if (!connectResult.IsSuccess)
                        {
                            if (attempt < RetryCount)
                            {
                                int delay = RetryIntervalMs * (1 << attempt);
                                await Task.Delay(delay);
                                continue;
                            }
                            return OperateResult.Fail<byte[]>(connectResult.Message, connectResult.ErrorCode);
                        }
                    }

                    // 发送命令
                    var sendResult = await SendAsync(command);
                    if (!sendResult.IsSuccess)
                    {
                        IsConnected = false;
                        if (attempt < RetryCount)
                        {
                            int delay = RetryIntervalMs * (1 << attempt);
                            await Task.Delay(delay);
                            CleanupConnection();
                            continue;
                        }
                        return OperateResult.Fail<byte[]>(sendResult.Message, sendResult.ErrorCode);
                    }

                    // 接收响应
                    using var cts = new CancellationTokenSource(ReceiveTimeout);
                    try
                    {
                        var receiveResult = await ReceiveAsync(cts.Token);
                        if (!receiveResult.IsSuccess)
                        {
                            IsConnected = false;
                            if (attempt < RetryCount)
                            {
                                int delay = RetryIntervalMs * (1 << attempt);
                                await Task.Delay(delay);
                                CleanupConnection();
                                continue;
                            }
                            return receiveResult;
                        }

                        // 检查响应
                        var checkResult = CheckResponse(command, receiveResult.Content);
                        if (!checkResult.IsSuccess)
                        {
                            // 协议错误 — 不重试
                            timer.Stop();
                            Trace(TraceLevel.Warning,
                                $"Protocol error: {checkResult.Message} [{SoftBasic.BytesToHexString(receiveResult.Content)}]",
                                timer.ElapsedMs);
                            return checkResult;
                        }

                        timer.Stop();
                        Trace(TraceLevel.Info,
                            $"Read {checkResult.Content.Length} bytes from {SoftBasic.BytesToHexString(command)}",
                            timer.ElapsedMs);
                        return checkResult;
                    }
                    catch (OperationCanceledException)
                    {
                        IsConnected = false;
                        if (attempt < RetryCount)
                        {
                            int delay = RetryIntervalMs * (1 << attempt);
                            await Task.Delay(delay);
                            CleanupConnection();
                            continue;
                        }
                        return OperateResult.Fail<byte[]>("Receive timeout after retries", -1001);
                    }
                }

                return result ?? OperateResult.Fail<byte[]>("Operation failed after retries");
            }
            finally
            {
                _lock.Release();
            }
        }

        // ---- 发送 ----

        private async Task<OperateResult> SendAsync(byte[] command)
        {
            if (_stream == null)
                return OperateResult.Fail("Not connected");

            try
            {
                Trace(TraceLevel.Verbose,
                    $"[Send] {command.Length} bytes: {SoftBasic.ByteToHexStringDump(command)}");

                using var cts = new CancellationTokenSource(SendTimeout);
                await _stream.WriteAsync(command, 0, command.Length, cts.Token);
                await _stream.FlushAsync(cts.Token);
                return OperateResult.Success();
            }
            catch (OperationCanceledException)
            {
                return OperateResult.Fail("Send timeout", -1001);
            }
            catch (Exception ex)
            {
                return OperateResult.Fail("Send error", ex, -1000);
            }
        }

        // ---- 流读取辅助方法 ----

        /// <summary>从流中读取指定数量的字节，处理部分读取情况。</summary>
        protected static async Task<int> ReadStreamAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct);
                if (bytesRead == 0)
                    break; // 连接已关闭
                totalRead += bytesRead;
            }
            return totalRead;
        }

        // =====================================================================
        // 连接管理
        // =====================================================================

        /// <inheritdoc/>
        public virtual async Task<OperateResult> ConnectAsync()
        {
            if (IsConnected)
                return OperateResult.Success();

            try
            {
                Trace(TraceLevel.Info, $"Connecting to {IpAddress}:{Port}...");

                _tcpClient?.Close();
                _tcpClient = new TcpClient();

                // 带超时的连接
                var connectTask = _tcpClient.ConnectAsync(IpAddress, Port);
                var timeoutTask = Task.Delay(ConnectTimeout);
                var completed = await Task.WhenAny(connectTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    _tcpClient.Close();
                    _tcpClient = null;
                    Trace(TraceLevel.Warning, $"Connection to {IpAddress}:{Port} timed out");
                    return OperateResult.Fail("Connection timeout", -1001);
                }

                // 传播连接任务中的任何异常
                await connectTask;

                _stream = _tcpClient.GetStream();
                IsConnected = true;
                Trace(TraceLevel.Info, $"Connected to {IpAddress}:{Port}");
                return OperateResult.Success();
            }
            catch (Exception ex)
            {
                CleanupConnection();
                Trace(TraceLevel.Error, $"Connection failed: {ex.Message}");
                return OperateResult.Fail($"Connection failed: {ex.Message}", ex, -1000);
            }
        }

        /// <inheritdoc/>
        public virtual Task<OperateResult> DisconnectAsync()
        {
            DisconnectInternal();
            return Task.FromResult(OperateResult.Success());
        }

        private void DisconnectInternal()
        {
            if (!IsConnected && _tcpClient == null)
                return;

            Trace(TraceLevel.Info, "Disconnecting...");
            CleanupConnection();
            IsConnected = false;
        }

        /// <summary>清理 TCP 客户端和流资源。</summary>
        protected void CleanupConnection()
        {
            _stream?.Dispose();
            _stream = null;
            _tcpClient?.Close();
            _tcpClient = null;
            IsConnected = false;
        }

        // =====================================================================
        // IReadWriteNet 实现
        // =====================================================================

        /// <inheritdoc/>
        public async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
        {
            if (_disposed)
                return OperateResult.Fail<byte[]>("Device is disposed");

            try
            {
                Trace(TraceLevel.Verbose, $"[BuildReadCommand] address={address}, length={length}");
                byte[] command = BuildReadCommand(address, length);
                if (command == null || command.Length == 0)
                    return OperateResult.Fail<byte[]>("Failed to build read command");

                Trace(TraceLevel.Verbose, $"[BuildReadCommand] result={command.Length} bytes: {SoftBasic.BytesToHexString(command)}");
                return await ReadFromCoreServerAsync(command);
            }
            catch (PlcCommunicationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace(TraceLevel.Error, $"[ReadAsync] Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return OperateResult.Fail<byte[]>($"ReadAsync error: {ex.Message}", ex, -1000);
            }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> WriteAsync(string address, byte[] data)
        {
            if (_disposed)
                return OperateResult.Fail("Device is disposed");
            if (data == null)
                return OperateResult.Fail("Data is null");

            try
            {
                byte[] command = BuildWriteCommand(address, data);
                if (command == null || command.Length == 0)
                    return OperateResult.Fail("Failed to build write command");

                var result = await ReadFromCoreServerAsync(command);
                return result.IsSuccess
                    ? OperateResult.Success()
                    : OperateResult.Fail(result.Message, result.ErrorCode);
            }
            catch (PlcCommunicationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return OperateResult.Fail($"WriteAsync error: {ex.Message}", ex, -1000);
            }
        }

        /// <inheritdoc/>
        public virtual async Task<OperateResult<bool>> ReadBoolAsync(string address)
        {
            var read = await ReadAsync(address, 1);
            if (!read.IsSuccess)
                return OperateResult.Fail<bool>(read.Message, read.ErrorCode);

            return read.Content.Length > 0
                ? OperateResult.Success(read.Content[0] != 0)
                : OperateResult.Fail<bool>("No data returned");
        }

        /// <inheritdoc/>
        public virtual async Task<OperateResult> WriteAsync(string address, bool value)
        {
            byte[] data = value ? new byte[] { 0x01 } : new byte[] { 0x00 };
            return await WriteAsync(address, data);
        }

        // =====================================================================
        // ITraceable 实现
        // =====================================================================

        /// <inheritdoc/>
        public bool EnableTrace { get; set; }

        /// <inheritdoc/>
        public event EventHandler<TraceEventArgs>? TraceMessage;

        /// <inheritdoc/>
        public void Trace(TraceLevel level, string message, long elapsedMs = 0)
        {
            if (!EnableTrace) return;
            var args = new TraceEventArgs(level, message, elapsedMs);
            TraceMessage?.Invoke(this, args);
        }
    }
}
