using CraneLoadingSystem.Models;

namespace CraneLoadingSystem.Services;

/// <summary>
/// 下位机（PLC/鹤位控制器）通讯服务接口
/// 负责远程启动/停止、数据读写等控制指令
/// </summary>
public interface IPlcControlService
{
    /// <summary>PLC连接状态</summary>
    bool IsConnected { get; }

    /// <summary>连接PLC</summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开PLC</summary>
    Task DisconnectAsync();

    /// <summary>远程启动指定鹤位装料</summary>
    Task<bool> RemoteStartAsync(string craneId, LoadingOrder order, CancellationToken cancellationToken = default);

    /// <summary>远程停止指定鹤位装料（正常结束）</summary>
    Task<bool> RemoteStopAsync(string craneId, CancellationToken cancellationToken = default);

    /// <summary>远程暂停装料</summary>
    Task<bool> RemotePauseAsync(string craneId, CancellationToken cancellationToken = default);

    /// <summary>远程恢复装料</summary>
    Task<bool> RemoteResumeAsync(string craneId, CancellationToken cancellationToken = default);

    /// <summary>紧急停止（触发安全回路）</summary>
    Task<bool> EmergencyStopAsync(string craneId, CancellationToken cancellationToken = default);

    /// <summary>紧急停止复位</summary>
    Task<bool> EmergencyResetAsync(string craneId, CancellationToken cancellationToken = default);

    /// <summary>读取鹤位实时数据</summary>
    Task<CraneRealtimeData?> ReadRealtimeDataAsync(string craneId, CancellationToken cancellationToken = default);

    /// <summary>下发鹤位参数（定量值、误差阈值等）</summary>
    Task<bool> WriteCraneParamsAsync(string craneId, double targetWeight, double tolerance, CancellationToken cancellationToken = default);

    /// <summary>读取鹤位I/O状态（到位信号、静电夹、阀门状态等）</summary>
    Task<CraneIoStatus?> ReadIoStatusAsync(string craneId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 鹤位I/O信号状态
/// </summary>
public class CraneIoStatus
{
    public bool IsCranePositioned { get; set; } // 鹤臂到位
    public bool IsClampConnected { get; set; }   // 静电夹已连接
    public bool IsTankCoverOpen { get; set; }   // 罐口盖已开
    public bool IsInletValveOpen { get; set; }  // 入口阀开
    public bool IsOutletValveOpen { get; set; } // 出口阀开
    public bool IsPumpRunning { get; set; }      // 泵运行
    public bool IsOverflowAlarm { get; set; }   // 溢出报警
    public bool IsStaticAlarm { get; set; }     // 静电报警
    public bool IsPressureAlarm { get; set; }   // 压力报警

    // === 8项安全联锁 DI 信号 (按 PRD Modbus 映射) ===
    /// <summary>DI00001 人体静电释放完成</summary>
    public bool DiHumanStatic { get; set; }
    /// <summary>DI00002 静电夹已连接</summary>
    public bool DiStaticClamp { get; set; }
    /// <summary>DI00003 鹤管连接到位</summary>
    public bool DiArmConnected { get; set; }
    /// <summary>DI00004 鹤管已归位（启动前需true，运行中变true=异常）</summary>
    public bool DiArmHomed { get; set; }
    /// <summary>DI00005 阻车器已升起</summary>
    public bool DiVehicleBlock { get; set; }
    /// <summary>DI00006 钥匙联锁已到位</summary>
    public bool DiKeyInterlock { get; set; }
    /// <summary>DI00007 溢油报警触发（true=报警，需急停）</summary>
    public bool DiOverflowAlarm { get; set; }
    /// <summary>DI00008 急停按钮触发（true=急停，最高优先级）</summary>
    public bool DiEmergencyStop { get; set; }
}
