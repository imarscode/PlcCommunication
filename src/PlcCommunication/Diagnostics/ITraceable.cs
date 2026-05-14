using System;

namespace PlcCommunication.Diagnostics
{
    /// <summary>跟踪消息的严重级别。</summary>
    public enum TraceLevel
    {
        Verbose = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Fatal = 4
    }

    /// <summary>跟踪消息的事件参数。</summary>
    public class TraceEventArgs : EventArgs
    {
        public TraceLevel Level { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }
        public long ElapsedMilliseconds { get; }

        public TraceEventArgs(TraceLevel level, string message, long elapsedMs = 0)
        {
            Level = level;
            Message = message;
            Timestamp = DateTime.Now;
            ElapsedMilliseconds = elapsedMs;
        }
    }

    /// <summary>
    /// 在通信设备上启用诊断跟踪。
    /// 启用后，设备会发出发送/接收字节的十六进制转储、
    /// 连接状态变化和计时信息。
    /// </summary>
    public interface ITraceable
    {
        /// <summary>启用或禁用诊断跟踪。</summary>
        bool EnableTrace { get; set; }

        /// <summary>当有跟踪消息时触发。</summary>
        event EventHandler<TraceEventArgs>? TraceMessage;

        /// <summary>如果跟踪已启用，则发出跟踪消息。</summary>
        void Trace(TraceLevel level, string message, long elapsedMs = 0);
    }
}
