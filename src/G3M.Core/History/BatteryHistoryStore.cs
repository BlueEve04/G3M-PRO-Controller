using System.Text.Json;
using System.Text.Json.Serialization;
using G3M.Core.Services;
using G3M.Core.Storage;

namespace G3M.Core.History;

/// <summary>
/// 电量历史的持久化。语义照抄 <c>g3m-battery</c>：状态变化立即记录，
/// 状态不变时每 5 分钟记录一次，只保留最近 8 天。
/// </summary>
/// <remarks>
/// 只有托盘进程会调用 <see cref="Record"/>；设置进程只读取 <see cref="Samples"/> 和
/// <see cref="Incidents"/>。单写者让文件不需要跨进程锁。
/// </remarks>
public sealed class BatteryHistoryStore
{
    /// <summary>只保留最近 8 天。</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(8);

    /// <summary>状态不变时的最小记录间隔。</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);

    /// <summary>两次采样间隔超过该值即视为不连续，续航推算不跨越这个断点。</summary>
    public static readonly TimeSpan MaxSampleGap = SampleInterval * 2;

    /// <summary>续航推算回看的最大时间窗口。</summary>
    public static readonly TimeSpan EstimateWindow = TimeSpan.FromHours(6);

    /// <summary>续航推算要求的最短连续观测时长。</summary>
    public static readonly TimeSpan MinEstimateWindow = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly List<BatterySample> _samples;
    private readonly List<BatteryIncident> _incidents;
    private bool _dirty;

    private BatteryHistoryStore(
        List<BatterySample> samples, List<BatteryIncident> incidents, DateTimeOffset sessionStartedAt)
    {
        _samples = samples;
        _incidents = incidents;
        SessionStartedAt = sessionStartedAt;
    }

    /// <summary>本次观测会话的起始时刻。程序没运行的时段不会被跨接起来。</summary>
    public DateTimeOffset SessionStartedAt { get; }

    /// <summary>采样记录，按时间升序。</summary>
    public IReadOnlyList<BatterySample> Samples => _samples;

    /// <summary>事件记录，按时间升序。</summary>
    public IReadOnlyList<BatteryIncident> Incidents => _incidents;

    /// <summary>是否有未落盘的改动。</summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// 载入历史记录。文件不存在、损坏或版本不符都不会抛异常——
    /// 历史数据丢失不应该阻止程序启动，会以空历史继续。
    /// </summary>
    public static BatteryHistoryStore Load()
    {
        DateTimeOffset sessionStart = DateTimeOffset.Now;
        List<BatterySample> samples = [];
        List<BatteryIncident> incidents = [];

        try
        {
            if (File.Exists(AppPaths.HistoryFile))
            {
                string json = ReadWithRetry(AppPaths.HistoryFile);
                BatteryHistoryDocument? document =
                    JsonSerializer.Deserialize<BatteryHistoryDocument>(json, Options);

                if (document is not null && document.Version == BatteryHistoryDocument.CurrentVersion)
                {
                    samples = [.. document.Samples.Select(ToSample).OrderBy(static s => s.At)];
                    incidents = [.. document.Incidents.Select(ToIncident).OrderBy(static i => i.StartedAt)];

                    // 上次退出时若留下未闭合的事件，在载入时闭合，避免续航推算被永久阻断。
                    for (int i = 0; i < incidents.Count; i++)
                    {
                        if (incidents[i].EndedAt is null)
                        {
                            incidents[i] = incidents[i] with { EndedAt = sessionStart };
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or FormatException or OverflowException)
        {
            // 以空历史继续。
            samples = [];
            incidents = [];
        }

        return new BatteryHistoryStore(samples, incidents, sessionStart);
    }

    /// <summary>记录一次电量读取结果。返回记录内容是否发生变化。</summary>
    public void Record(BatteryReadResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);

        bool changed = Trim(now);

        if (!result.Success)
        {
            IncidentKind kind = result.Present ? IncidentKind.Read : IncidentKind.NoDevice;
            changed |= OpenIncident(kind, now, result.Error ?? "读取失败");
        }
        else
        {
            changed |= CloseIncident(now);
            changed |= AppendSample(result.State!, now);
        }

        if (changed)
        {
            _dirty = true;
        }
    }

    /// <summary>把历史写入磁盘。使用临时文件加原子替换，读取方永远看不到半截文件。</summary>
    public bool Save()
    {
        try
        {
            AppPaths.EnsureCreated();

            var document = new BatteryHistoryDocument
            {
                Samples = [.. _samples.Select(ToDocument)],
                Incidents = [.. _incidents.Select(ToDocument)],
            };

            string json = JsonSerializer.Serialize(document, Options);
            string temporary = AppPaths.HistoryFile + ".tmp";
            File.WriteAllText(temporary, json);

            if (File.Exists(AppPaths.HistoryFile))
            {
                File.Replace(temporary, AppPaths.HistoryFile, AppPaths.HistoryFile + ".bak",
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, AppPaths.HistoryFile);
            }

            _dirty = false;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool AppendSample(Model.BatteryState state, DateTimeOffset now)
    {
        if (_samples.Count > 0)
        {
            BatterySample last = _samples[^1];
            bool sameReading = last.Percent == state.Percent
                               && last.Charging == state.Charging
                               && last.Connection == state.Connection;

            if (sameReading && now - last.At < SampleInterval)
            {
                return false;
            }
        }

        _samples.Add(new BatterySample(now, state.Percent, state.Connection, state.Charging));
        return true;
    }

    private bool OpenIncident(IncidentKind kind, DateTimeOffset now, string message)
    {
        if (_incidents.Count > 0)
        {
            BatteryIncident last = _incidents[^1];
            if (last.IsActive && last.Kind == kind)
            {
                return false;
            }

            // 换了一种故障，先把上一段闭合。
            if (last.IsActive)
            {
                _incidents[^1] = last with { EndedAt = now };
            }
        }

        _incidents.Add(new BatteryIncident(kind, now, null, message));
        return true;
    }

    private bool CloseIncident(DateTimeOffset now)
    {
        if (_incidents.Count == 0 || !_incidents[^1].IsActive)
        {
            return false;
        }

        _incidents[^1] = _incidents[^1] with { EndedAt = now };
        return true;
    }

    /// <summary>丢弃超过保留期的记录。返回是否有内容被移除。</summary>
    private bool Trim(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - Retention;
        int removedSamples = _samples.RemoveAll(sample => sample.At < cutoff);
        int removedIncidents = _incidents.RemoveAll(
            incident => (incident.EndedAt ?? now) < cutoff);

        return removedSamples > 0 || removedIncidents > 0;
    }

    private static string ReadWithRetry(string path)
    {
        IOException? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException exception)
            {
                last = exception;
                Thread.Sleep(50);
            }
        }

        // 主文件读不动时退回上一个版本。
        string backup = path + ".bak";
        if (File.Exists(backup))
        {
            using var stream = new FileStream(
                backup, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        throw last ?? new IOException("读取历史记录失败");
    }

    private static BatterySample ToSample(BatterySampleDocument document) => new(
        DateTimeOffset.FromUnixTimeSeconds(document.At),
        document.Percent,
        (Hid.G3mConnectionKind)document.Transport,
        (Model.ChargingState)document.Charge);

    private static BatterySampleDocument ToDocument(BatterySample sample) => new()
    {
        At = sample.At.ToUnixTimeSeconds(),
        Percent = sample.Percent,
        Transport = (int)sample.Connection,
        Charge = (int)sample.Charging,
    };

    private static BatteryIncident ToIncident(BatteryIncidentDocument document) => new(
        Enum.IsDefined((IncidentKind)document.Kind) ? (IncidentKind)document.Kind : IncidentKind.Read,
        DateTimeOffset.FromUnixTimeSeconds(document.StartedAt),
        document.EndedAt is { } ended ? DateTimeOffset.FromUnixTimeSeconds(ended) : null,
        document.Message);

    private static BatteryIncidentDocument ToDocument(BatteryIncident incident) => new()
    {
        Kind = (int)incident.Kind,
        StartedAt = incident.StartedAt.ToUnixTimeSeconds(),
        EndedAt = incident.EndedAt?.ToUnixTimeSeconds(),
        Message = incident.Message,
    };
}
