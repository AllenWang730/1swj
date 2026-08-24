# CraneLoadingSystem · 流体装卸鹤位上位机监控系统

基于 **.NET 8 WPF** 的工业流体装卸鹤位上位机系统。支持**多鹤位同屏显示**（每鹤位独立卡片）、**下位机(PLC)远程启动/停止/急停**、对接 **SAP / ERP** 获取单据并**下发工作量**。

---

## ✨ 功能特性

| 模块 | 功能 |
|------|------|
| 🖥️ **多鹤位同屏** | 每鹤位一张独立卡片，动态生成、独立操作；支持流式/平铺/紧凑多布局；顶部Tab快速跳转 |
| 📊 **状态监控** | 瞬时流量、累计流量、压力、温度、密度、已装/计划量、进度条、已用/剩余时间 |
| 🎛️ **鹤位控制** | 启动 / 暂停恢复 / 停止 / 复位 / **紧急停止** / 全局急停，状态机严格控制按钮可用性 |
| 📦 **单据管理** | 待下发 / 进行中 / 已完成 三Tab视图；双击或右键下发；支持手工补录单据 |
| 🔌 **SAP对接** | OData + Basic认证：取单、回传下发状态、回传完成、回传异常 |
| 🔌 **ERP对接** | 通用REST API（Key鉴权）：取单、客户/产品主数据、完成回传 |
| 🤖 **仿真模式** | `appsettings.json` 开启 `EnableSimulation=true`：无PLC环境下全流程演示 |
| 📝 **日志** | Serilog控制台+文件双写，30天滚动保留；全局异常处理+单实例互斥 |
| 🧩 **架构** | DI + 接口解耦 + ObservableProperty，方便替换真实PLC/OPC UA/Modbus驱动 |

---

## 📁 目录结构

```
CraneLoadingSystem.sln
└── src/CraneLoadingSystem/
    ├── CraneLoadingSystem.csproj    # net8.0-windows, WPF
    ├── appsettings.json             # 系统/PLC/SAP/ERP/鹤位配置
    ├── App.xaml / App.xaml.cs       # 启动入口 + DI + 日志 + 单实例 + 全局异常
    ├── AssemblyInfo.cs
    │
    ├── Models/                      # 数据模型
    │   ├── AppConfig.cs             # 配置类（appsettings映射）
    │   ├── CranePosition.cs         # 鹤位+状态+实时数据
    │   ├── LoadingOrder.cs          # 装料单据
    │   └── AlarmRecord.cs           # 报警/操作日志
    │
    ├── Services/                    # 业务服务（接口+实现）
    │   ├── IPlcControlService.cs    # 下位机通讯接口
    │   ├── PlcControlService.cs     # 实现(含仿真定时器)
    │   ├── ICraneManagerService.cs  # 鹤位管理器接口
    │   ├── CraneManagerService.cs   # 鹤位初始化/轮询/事件
    │   ├── IOrderManagementService.cs
    │   ├── OrderManagementService.cs# 单据获取-分配-下发-完成
    │   ├── IErpSapServices.cs       # SAP/ERP接口
    │   ├── SapService.cs            # SAP OData 实现（含mock）
    │   └── ErpService.cs            # ERP REST 实现（含mock）
    │
    └── Views/                       # WPF界面
        ├── CranePositionCard.xaml   # ★ 单鹤位内部卡片（独立窗口）
        ├── CranePositionCard.xaml.cs
        ├── DispatchOrderDialog.xaml # 单据下发→鹤位对话框
        ├── DispatchOrderDialog.xaml.cs
        ├── ManualOrderDialog.xaml   # 手工创建单据
        ├── ManualOrderDialog.xaml.cs
        ├── MainWindow.xaml          # ★ 主窗口(MDI容器+单据面板)
        └── MainWindow.xaml.cs
```

---

## 🚀 本地构建运行

> 环境要求：Windows 10/11，[.NET 8 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0) 或 Visual Studio 2022 17.8+

### 方式一：命令行

```powershell
# 还原 + 构建
dotnet restore CraneLoadingSystem.sln
dotnet build CraneLoadingSystem.sln -c Release

# 运行（进入项目目录）
cd src/CraneLoadingSystem
dotnet run -c Release
```

### 方式二：Visual Studio

1. 双击打开 `CraneLoadingSystem.sln`
2. 按 `F5` 启动（默认仿真模式直接可用，不需PLC/SAP）

---

## ⚙️ 关键配置 (appsettings.json)

```jsonc
{
  "AppSettings": {
    "EnableSimulation": true,        // ← 生产环境改为false，连接真实PLC
    "DataRefreshIntervalMs": 1000    // 数据刷新频率
  },
  "SapSettings": {
    "BaseUrl": "https://sap.example.com:44300",
    "UserName": "sapuser",
    "Password": "sappassword",
    "ODataServicePath": "/sap/opu/odata/sap/Z_CARGO_LOADING_SRV/"
  },
  "ErpSettings": {
    "BaseUrl": "http://erp.example.com/api",
    "ApiKey": "erp-api-key-here"
  },
  "PlcSettings": {
    "IpAddress": "192.168.1.100",
    "Port": 502,
    "TimeoutMs": 3000
  },
  "CranePositions": [
    { "Id": "CP001", "Name": "1#鹤位", "ProductName": "汽油", "MaxFlowRate": 300, "PlcAddress": 1 }
    // ... 可自由扩展多个鹤位
  ]
}
```

### 替换为真实PLC

1. 安装驱动包，例如 Modbus / S7.Net / OPC UA：
   ```
   dotnet add package S7.Net
   dotnet add package OPCFoundation.NetStandard.Opc.Ua
   ```
2. 打开 `Services/PlcControlService.cs`，在非仿真分支实现 `ConnectAsync` / `ReadRealtimeDataAsync` / `RemoteStartAsync` 等接口即可，上层业务无需改动。

---

## 🗺️ 业务流程

```
 ┌──────────────┐   pull    ┌───────────────────┐   dispatch   ┌───────────┐  RemoteStart  ┌──────────────┐
 │  SAP / ERP   │ ───────►  │ OrderManagerService│ ───────────► │ Crane Mgr │ ────────────► │ PLC/下位机   │
 └──────────────┘           └───────────────────┘              └─────┬─────┘               └──────┬───────┘
        ▲                                                             │                            │
        │ 回传完成/异常                                                │  读取实时数据                 │ 装料
        └─────────────────────────────────────────────────────────────┘◄───────────────────────────┘
                                                                      │
                                                              通知完成 → OrderMgr回传
```

**典型操作步骤：**

1. 打开系统 → 系统自动初始化鹤位并从SAP/ERP拉取单据
2. 左侧"待下发"Tab **双击** 或 右键"下发到鹤位" → 选择目标鹤位
3. 系统自动：回传SAP下发状态 → 分配鹤位 → 发送**远程启动**指令
4. 鹤位卡片实时刷新流量/进度；达到定量自动停止并回传完成
5. 也可在鹤位卡片手动：暂停 / 恢复 / 停止 / ⚠急停

---

## 🔗 GitHub 托管说明

本项目已按开源仓库结构组织，直接推送到GitHub即可：

```bash
# 1. 初始化仓库（项目根目录）
cd /path/to/CraneLoadingSystem
git init
git checkout -b main

# 2. 添加所有源码（.gitignore 已自动排除 bin/obj/日志）
git add -A
git commit -m "feat: 流体装卸鹤位上位机系统 v1.0 初始化"

# 3. 推送到 GitHub
git remote add origin https://github.com/<你的用户名>/<仓库名>.git
git push -u origin main
```

**推荐仓库设置：**
- License：私有仓库可使用 MIT / Apache-2.0（工业代码建议私有）
- `Secrets → Actions`：把 SAP 密码、PLC IP 等敏感配置放入 GitHub Secrets，生产环境部署时注入
- 添加 GitHub Actions 工作流（`.github/workflows/build.yml`）实现自动编译，模板示例如下：

```yaml
name: Build
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore CraneLoadingSystem.sln
      - run: dotnet build CraneLoadingSystem.sln -c Release --no-restore
```

---

## 🛠️ 扩展路线图

| 优先级 | 能力 | 建议 |
|--------|------|------|
| P0 | 真实PLC驱动 | 替换 `PlcControlService` 仿真逻辑为 S7.Net/OPC UA/Modbus TCP |
| P1 | 过磅系统对接 | 过磅毛重/皮重读取→自动校验实际装载量 |
| P1 | IC卡/RFID司机身份验证 | 装车前刷卡→车牌/司机匹配校验 |
| P2 | 视频监控嵌入 | 每鹤位卡片叠加VLC或海康SDK实时画面 |
| P2 | 报表打印 | 装车单/日累计/月汇总 RDLC |
| P2 | 用户权限 | 操作员/班长/管理员三级+操作审计 |
| P3 | WinCC/Historian 趋势归档 | 流量、压力历史曲线 |

---

## 📄 License

Industrial Automation · 内部项目
