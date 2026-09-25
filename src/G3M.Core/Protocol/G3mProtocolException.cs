namespace G3M.Core.Protocol;

/// <summary>协议层失败的分类，调用方据此决定是提示用户、重试还是回滚。</summary>
public enum G3mErrorKind
{
    /// <summary>没有找到任何 G3M Pro 厂商集合。</summary>
    ReceiverNotFound,

    /// <summary>设备存在但打不开。</summary>
    OpenFailed,

    /// <summary>接收器直接拒绝了命令（响应 <c>[3]==0xFF</c>）。</summary>
    CommandRejected,

    /// <summary>设备返回命令错误（响应 <c>[7]==0xFF</c>）。</summary>
    DeviceCommandError,

    /// <summary>设备返回参数错误（响应 <c>[7]==0xFE</c>）。</summary>
    DeviceParameterError,

    /// <summary>等待响应超时。</summary>
    Timeout,

    /// <summary>接收器已连接但鼠标本体不在线，通常需要动一下鼠标唤醒。</summary>
    MouseOffline,

    /// <summary>底层 HID 读写失败。</summary>
    IoFailed,

    /// <summary>写入后读回内容与写入内容不一致。</summary>
    VerifyFailed,

    /// <summary>调用方传入了非法参数。</summary>
    InvalidArgument,
}

/// <summary>协议交互失败。<see cref="Exception.Message"/> 已是可以直接展示给用户的中文描述。</summary>
public sealed class G3mProtocolException : Exception
{
    public G3mProtocolException(G3mErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>失败分类。</summary>
    public G3mErrorKind Kind { get; }

    /// <summary>
    /// 该失败是否值得原样重试。超时和鼠标离线通常动一下鼠标或重试就能恢复；
    /// 参数错误和校验失败则不会因为重试而变好。
    /// </summary>
    public bool IsTransient => Kind is G3mErrorKind.Timeout or G3mErrorKind.MouseOffline
        or G3mErrorKind.IoFailed;
}
