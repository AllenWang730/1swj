using System.Collections.ObjectModel;
using System.Windows;
using CraneLoadingSystem.Models;
using Serilog;

namespace CraneLoadingSystem.Services;

/// <summary>
/// 报警管理服务（FR-ALARM）
/// </summary>
public interface IAlarmManagerService
{
    /// <summary>所有报警记录（绑定UI列表）</summary>
    ObservableCollection<AlarmRecord> Alarms { get; }

    /// <summary>新增报警</summary>
    Task<AlarmRecord> RaiseAsync(string craneId, string craneName, AlarmLevel level, string message, string? detail = null);

    /// <summary>确认单条报警</summary>
    Task AcknowledgeAsync(long alarmId, string operatorName);

    /// <summary>批量确认指定鹤位的所有未确认报警</summary>
    Task AcknowledgeAllByCraneAsync(string craneId, string operatorName);

    /// <summary>查询历史报警（按时间/鹤位/级别筛选）</summary>
    IEnumerable<AlarmRecord> Query(DateTime? from, DateTime? to, string? craneId, AlarmLevel? level);

    /// <summary>当前是否有未确认的 Critical 报警</summary>
    bool HasUnacknowledgedCritical { get; }
}

/// <summary>
/// 报警管理服务实现
/// </summary>
public class AlarmManagerService : IAlarmManagerService
{
    private readonly object _lock = new();
    private long _nextId = 1;
    private readonly IDatabaseService? _db;

    public AlarmManagerService(IDatabaseService? db = null)
    {
        _db = db;
    }

    public ObservableCollection<AlarmRecord> Alarms { get; } = new();

    public bool HasUnacknowledgedCritical
    {
        get
        {
            lock (_lock)
                return Alarms.Any(a => a.Level == AlarmLevel.Critical && !a.Acknowledged);
        }
    }

    public Task<AlarmRecord> RaiseAsync(string craneId, string craneName, AlarmLevel level, string message, string? detail = null)
    {
        AlarmRecord rec;
        lock (_lock)
        {
            rec = new AlarmRecord
            {
                Id = _nextId++,
                Time = DateTime.Now,
                CraneId = craneId,
                CraneName = craneName,
                Level = level,
                Message = message,
                Detail = detail,
                Acknowledged = false
            };
        }

        // P0 fix: Dispatcher.Invoke 必须在 lock 外执行。
        // 原代码在 lock(_lock) 内调 Dispatcher.Invoke，后台线程持锁等 UI 线程，
        // UI 线程若入锁（如 TrimAlarms/AcknowledgeAsync）则死锁。
        Application.Current?.Dispatcher.Invoke(() => Alarms.Insert(0, rec));

        // 持久化到 SQLite 数据库
        _db?.InsertAlarm(rec);

        Log.Write(level switch
        {
            AlarmLevel.Critical => Serilog.Events.LogEventLevel.Fatal,
            AlarmLevel.Error => Serilog.Events.LogEventLevel.Error,
            AlarmLevel.Warning => Serilog.Events.LogEventLevel.Warning,
            _ => Serilog.Events.LogEventLevel.Information
        }, "[Alarm] [{Level}] 鹤位 {Name}({Id}): {Msg} | {Detail}",
            level, craneName, craneId, message, detail ?? "");

        // 保留最近 500 条，避免内存无限增长
        TrimAlarms();

        return Task.FromResult(rec);
    }

    public Task AcknowledgeAsync(long alarmId, string operatorName)
    {
        lock (_lock)
        {
            var rec = Alarms.FirstOrDefault(a => a.Id == alarmId);
            if (rec != null && !rec.Acknowledged)
            {
                rec.Acknowledged = true;
                rec.AcknowledgedTime = DateTime.Now;
                Log.Information("[Alarm] 报警 #{Id} 已由 {Op} 确认", alarmId, operatorName);
            }
        }
        return Task.CompletedTask;
    }

    public Task AcknowledgeAllByCraneAsync(string craneId, string operatorName)
    {
        lock (_lock)
        {
            foreach (var rec in Alarms.Where(a => a.CraneId == craneId && !a.Acknowledged))
            {
                rec.Acknowledged = true;
                rec.AcknowledgedTime = DateTime.Now;
            }
        }
        Log.Information("[Alarm] 鹤位 {Id} 所有报警已批量确认 (操作员: {Op})", craneId, operatorName);
        return Task.CompletedTask;
    }

    public IEnumerable<AlarmRecord> Query(DateTime? from, DateTime? to, string? craneId, AlarmLevel? level)
    {
        lock (_lock)
        {
            return Alarms.Where(a =>
                (from == null || a.Time >= from) &&
                (to == null || a.Time <= to) &&
                (string.IsNullOrEmpty(craneId) || a.CraneId == craneId) &&
                (level == null || a.Level == level)).ToList();
        }
    }

    private void TrimAlarms()
    {
        // P1 fix: 与 Alarms.Insert 同步，跨线程 RemoveAt 也会抛异常
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                while (Alarms.Count > 500)
                    Alarms.RemoveAt(Alarms.Count - 1);
            }
        });
    }
}
