using System;

namespace PlcCommunication.Core
{
    /// <summary>
    /// 不返回数据的操作结果对象。每个公共API方法返回此对象或 <see cref="OperateResult{T}"/>，
    /// 通信失败时不会抛出异常。
    /// </summary>
    public class OperateResult
    {
        /// <summary>操作成功时为 true。</summary>
        public bool IsSuccess { get; protected set; }

        /// <summary>结果的人类可读描述。</summary>
        public string Message { get; protected set; } = string.Empty;

        /// <summary>协议相关或框架相关的错误码。零通常表示成功。</summary>
        public int ErrorCode { get; protected set; }

        /// <summary>导致失败的异常（如果有）。</summary>
        public Exception? Exception { get; protected set; }

        protected OperateResult() { }

        public static OperateResult Success()
        {
            return new OperateResult { IsSuccess = true };
        }

        public static OperateResult<T> Success<T>(T content)
        {
            return new OperateResult<T> { IsSuccess = true, Content = content };
        }

        public static OperateResult Fail(string message, int errorCode = -1)
        {
            return new OperateResult { Message = message, ErrorCode = errorCode };
        }

        public static OperateResult Fail(string message, Exception ex, int errorCode = -1)
        {
            return new OperateResult { Message = message, ErrorCode = errorCode, Exception = ex };
        }

        public static OperateResult<T> Fail<T>(string message, int errorCode = -1)
        {
            return new OperateResult<T> { Message = message, ErrorCode = errorCode };
        }

        public static OperateResult<T> Fail<T>(string message, Exception ex, int errorCode = -1)
        {
            return new OperateResult<T> { Message = message, ErrorCode = errorCode, Exception = ex };
        }

        /// <summary>从另一个失败结果复制错误信息。</summary>
        public void CopyErrorFrom(OperateResult other)
        {
            if (other != null && !other.IsSuccess)
            {
                IsSuccess = false;
                Message = other.Message;
                ErrorCode = other.ErrorCode;
                Exception = other.Exception;
            }
        }

        public static implicit operator bool(OperateResult result)
        {
            return result?.IsSuccess ?? false;
        }
    }

    /// <summary>
    /// 返回类型为 <typeparamref name="T"/> 的数据的操作结果对象。
    /// </summary>
    public class OperateResult<T> : OperateResult
    {
        /// <summary>操作返回的数据，仅在 <see cref="OperateResult.IsSuccess"/> 为 true 时有效。</summary>
        public T Content { get; set; } = default!;

        public new static OperateResult<T> Fail(string message, int errorCode = -1)
        {
            return OperateResult.Fail<T>(message, errorCode);
        }
    }
}
