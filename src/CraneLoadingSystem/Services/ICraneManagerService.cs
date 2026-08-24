using System.Collections.ObjectModel;
using CraneLoadingSystem.Models;

namespace CraneLoadingSystem.Services;

/// <summary>
/// 鹤位管理器服务 - 管理所有鹤位的生命周期、状态轮询、下发操作
/// </summary>
public interface ICraneManagerService
{
    /// <summary>所有鹤位列表</summary>
    ObservableCollection<CranePosition> Cranes { get; }

    /// <summary>初始化所有鹤位</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>按ID获取鹤位</summary>
    CranePosition? GetCrane(string craneId);

    /// <summary>远程启动鹤位装料（当前分配的单据）</summary>
    Task<bool> RemoteStartAsync(string craneId);

    /// <summary>停止鹤位装料</summary>
    Task<bool> RemoteStopAsync(string craneId);

    /// <summary>暂停/恢复</summary>
    Task<bool> RemotePauseAsync(string craneId);
    Task<bool> RemoteResumeAsync(string craneId);

    /// <summary>紧急停止/复位</summary>
    Task<bool> EmergencyStopAsync(string craneId);
    Task<bool> EmergencyResetAsync(string craneId);

    /// <summary>查询可用鹤位（根据产品匹配）</summary>
    IEnumerable<CranePosition> GetAvailableCranesForProduct(string productCode);
}
