namespace G3M.Tray;

/// <summary>
/// 具名单实例锁。防止用户重复启动托盘程序而出现两个图标。
/// 名称带 <c>Local\</c> 前缀，因此只作用于当前登录会话。
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex? _mutex;

    private SingleInstance(Mutex? mutex, bool isOwner)
    {
        _mutex = mutex;
        IsOwner = isOwner;
    }

    /// <summary>当前进程是否持有该实例锁。</summary>
    public bool IsOwner { get; }

    public static SingleInstance Acquire(string name)
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out bool createdNew);
            return new SingleInstance(mutex, createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // 名称已存在但当前进程没有权限打开——说明另一个实例在跑。
            return new SingleInstance(null, isOwner: false);
        }
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        try
        {
            if (IsOwner)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
            // 未持有该锁时释放会抛这个异常，忽略即可。
        }

        _mutex.Dispose();
    }
}
