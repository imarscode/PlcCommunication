using System;

namespace PlcCommunication.Core
{
    /// <summary>
    /// 仅在编程/配置错误时抛出的异常
    /// （无效的地址格式、已释放的对象使用等）。
    /// 通信失败始终返回 <see cref="OperateResult"/>。
    /// </summary>
    public class PlcCommunicationException : Exception
    {
        /// <summary>可选的框架相关错误码。</summary>
        public int ErrorCode { get; }

        public PlcCommunicationException(string message)
            : base(message)
        {
        }

        public PlcCommunicationException(string message, int errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public PlcCommunicationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
