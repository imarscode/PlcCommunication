using System;
using System.Collections.Generic;

namespace PlcCommunication.Diagnostics
{
    /// <summary>
    /// 用于全局跟踪路由的静态日志管理器。订阅
    /// <see cref="ITraceable.TraceMessage"/> 事件并转发到注册的接收器。
    /// </summary>
    public static class LogManager
    {
        private static readonly List<Action<TraceEventArgs>> _sinks = new List<Action<TraceEventArgs>>();
        private static TraceLevel _minimumLevel = TraceLevel.Verbose;
        private static readonly object _lock = new object();

        /// <summary>获取或设置转发到接收器的最小跟踪级别。</summary>
        public static TraceLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = value;
        }

        /// <summary>注册一个接收所有跟踪事件的接收器。</summary>
        public static void AddSink(Action<TraceEventArgs> sink)
        {
            lock (_lock)
            {
                _sinks.Add(sink);
            }
        }

        /// <summary>移除之前注册的接收器。</summary>
        public static void RemoveSink(Action<TraceEventArgs> sink)
        {
            lock (_lock)
            {
                _sinks.Remove(sink);
            }
        }

        /// <summary>清除所有已注册的接收器。</summary>
        public static void ClearSinks()
        {
            lock (_lock)
            {
                _sinks.Clear();
            }
        }

        /// <summary>
        /// 订阅设备的跟踪事件并转发到所有已注册的接收器。
        /// </summary>
        public static void Subscribe(ITraceable device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            device.TraceMessage += OnTraceMessage;
        }

        /// <summary>取消订阅设备的跟踪事件。</summary>
        public static void Unsubscribe(ITraceable device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            device.TraceMessage -= OnTraceMessage;
        }

        private static void OnTraceMessage(object? sender, TraceEventArgs e)
        {
            if (e.Level < _minimumLevel)
                return;

            Action<TraceEventArgs>[] sinks;
            lock (_lock)
            {
                sinks = _sinks.ToArray();
            }

            foreach (var sink in sinks)
            {
                try
                {
                    sink(e);
                }
                catch
                {
                    // 接收器异常被忽略，以避免中断通信
                }
            }
        }
    }
}
