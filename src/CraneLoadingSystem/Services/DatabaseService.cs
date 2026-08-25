using CraneLoadingSystem.Models;
using Microsoft.Data.Sqlite;
using Serilog;

namespace CraneLoadingSystem.Services;

/// <summary>
/// SQLite 数据库服务 - 持久化报警记录、操作日志、单据历史
/// </summary>
public interface IDatabaseService
{
    void Initialize();
    void InsertAlarm(AlarmRecord record);
    void InsertOperationLog(OperationLog log);
    void UpdateOrderStatus(string orderNo, string status, double actualWeight, DateTime? completeTime);
    void InsertOrderHistory(LoadingOrder order);
    List<AlarmRecord> QueryAlarms(DateTime? from, DateTime? to, string? craneId, AlarmLevel? level);
    List<OperationLog> QueryOperationLogs(DateTime? from, DateTime? to, string? craneId);
    List<LoadingOrder> QueryOrderHistory(string? craneId = null, int limit = 100);
}

/// <summary>
/// SQLite 数据库服务实现
/// </summary>
public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string connectionString = "Data Source=crane_loading.db")
    {
        _connectionString = connectionString;
    }

    public void Initialize()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // 报警记录表
            conn.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS AlarmRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Time TEXT NOT NULL,
                    CraneId TEXT NOT NULL,
                    CraneName TEXT,
                    Level INTEGER NOT NULL,
                    Message TEXT NOT NULL,
                    Detail TEXT,
                    Acknowledged INTEGER DEFAULT 0,
                    AcknowledgedTime TEXT,
                    OperatorName TEXT
                );
            """);

            // 操作日志表
            conn.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS OperationLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Time TEXT NOT NULL,
                    Operator TEXT,
                    Action TEXT,
                    CraneId TEXT,
                    OrderNo TEXT,
                    Detail TEXT,
                    Ip TEXT
                );
            """);

            // 单据历史表
            conn.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS OrderHistory (
                    OrderNo TEXT PRIMARY KEY,
                    Source TEXT,
                    Status TEXT NOT NULL,
                    CustomerName TEXT,
                    VehicleNo TEXT,
                    ProductName TEXT,
                    PlannedWeight REAL,
                    ActualWeight REAL,
                    AssignedCraneId TEXT,
                    CreateTime TEXT,
                    DispatchTime TEXT,
                    CompleteTime TEXT,
                    Operator TEXT
                );
            """);

            // 创建索引
            conn.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS IX_Alarm_Time ON AlarmRecords(Time);");
            conn.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS IX_Alarm_CraneId ON AlarmRecords(CraneId);");
            conn.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS IX_Logs_Time ON OperationLogs(Time);");
            conn.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS IX_Orders_CraneId ON OrderHistory(AssignedCraneId);");

            Log.Information("[Database] SQLite 初始化完成: {Conn}", _connectionString);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Database] 初始化失败");
        }
    }

    public void InsertAlarm(AlarmRecord record)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AlarmRecords (Time, CraneId, CraneName, Level, Message, Detail, Acknowledged, AcknowledgedTime)
                VALUES (@time, @craneId, @craneName, @level, @msg, @detail, @ack, @ackTime);
            """;
            cmd.Parameters.AddWithValue("@time", record.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@craneId", record.CraneId);
            cmd.Parameters.AddWithValue("@craneName", record.CraneName);
            cmd.Parameters.AddWithValue("@level", (int)record.Level);
            cmd.Parameters.AddWithValue("@msg", record.Message);
            cmd.Parameters.AddWithValue("@detail", (object?)record.Detail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ack", record.Acknowledged ? 1 : 0);
            cmd.Parameters.AddWithValue("@ackTime", (object?)record.AcknowledgedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Database] 插入报警记录失败");
        }
    }

    public void InsertOperationLog(OperationLog log)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO OperationLogs (Time, Operator, Action, CraneId, OrderNo, Detail, Ip)
                VALUES (@time, @op, @action, @craneId, @orderNo, @detail, @ip);
            """;
            cmd.Parameters.AddWithValue("@time", log.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@op", log.Operator ?? "System");
            cmd.Parameters.AddWithValue("@action", log.Action ?? "");
            cmd.Parameters.AddWithValue("@craneId", (object?)log.CraneId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@orderNo", (object?)log.OrderNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@detail", (object?)log.Detail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ip", log.Ip ?? "");
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Database] 插入操作日志失败");
        }
    }

    public void UpdateOrderStatus(string orderNo, string status, double actualWeight, DateTime? completeTime)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE OrderHistory SET Status = @status, ActualWeight = @weight, CompleteTime = @time
                WHERE OrderNo = @orderNo;
            """;
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@weight", actualWeight);
            cmd.Parameters.AddWithValue("@time", (object?)completeTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@orderNo", orderNo);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Database] 更新单据状态失败");
        }
    }

    public void InsertOrderHistory(LoadingOrder order)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO OrderHistory
                (OrderNo, Source, Status, CustomerName, VehicleNo, ProductName,
                 PlannedWeight, ActualWeight, AssignedCraneId, CreateTime, DispatchTime, CompleteTime)
                VALUES (@orderNo, @source, @status, @cust, @vehicle, @product,
                        @plan, @actual, @craneId, @create, @dispatch, @complete);
            """;
            cmd.Parameters.AddWithValue("@orderNo", order.OrderNo);
            cmd.Parameters.AddWithValue("@source", order.Source.ToString());
            cmd.Parameters.AddWithValue("@status", order.Status.ToString());
            cmd.Parameters.AddWithValue("@cust", order.CustomerName);
            cmd.Parameters.AddWithValue("@vehicle", order.VehicleNo);
            cmd.Parameters.AddWithValue("@product", order.ProductName);
            cmd.Parameters.AddWithValue("@plan", order.PlannedWeight);
            cmd.Parameters.AddWithValue("@actual", order.ActualWeight);
            cmd.Parameters.AddWithValue("@craneId", (object?)order.AssignedCraneId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@create", order.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@dispatch", (object?)order.DispatchTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@complete", (object?)order.CompleteTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Database] 插入单据历史失败");
        }
    }

    public List<AlarmRecord> QueryAlarms(DateTime? from, DateTime? to, string? craneId, AlarmLevel? level)
    {
        var result = new List<AlarmRecord>();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            var where = new List<string>();
            if (from.HasValue) where.Add("Time >= @from");
            if (to.HasValue) where.Add("Time <= @to");
            if (!string.IsNullOrEmpty(craneId)) where.Add("CraneId = @craneId");
            if (level.HasValue) where.Add("Level = @level");

            cmd.CommandText = "SELECT * FROM AlarmRecords";
            if (where.Count > 0) cmd.CommandText += " WHERE " + string.Join(" AND ", where);
            cmd.CommandText += " ORDER BY Time DESC LIMIT 500";

            if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (!string.IsNullOrEmpty(craneId)) cmd.Parameters.AddWithValue("@craneId", craneId);
            if (level.HasValue) cmd.Parameters.AddWithValue("@level", (int)level.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new AlarmRecord
                {
                    Id = reader.GetInt64(0),
                    Time = DateTime.Parse(reader.GetString(1)),
                    CraneId = reader.GetString(2),
                    CraneName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Level = (AlarmLevel)reader.GetInt32(4),
                    Message = reader.GetString(5),
                    Detail = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Acknowledged = reader.GetInt32(7) == 1,
                    AcknowledgedTime = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8))
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Database] 查询报警记录失败");
        }
        return result;
    }

    public List<OperationLog> QueryOperationLogs(DateTime? from, DateTime? to, string? craneId)
    {
        var result = new List<OperationLog>();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            var where = new List<string>();
            if (from.HasValue) where.Add("Time >= @from");
            if (to.HasValue) where.Add("Time <= @to");
            if (!string.IsNullOrEmpty(craneId)) where.Add("CraneId = @craneId");

            cmd.CommandText = "SELECT * FROM OperationLogs";
            if (where.Count > 0) cmd.CommandText += " WHERE " + string.Join(" AND ", where);
            cmd.CommandText += " ORDER BY Time DESC LIMIT 500";

            if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (!string.IsNullOrEmpty(craneId)) cmd.Parameters.AddWithValue("@craneId", craneId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new OperationLog
                {
                    Id = reader.GetInt64(0),
                    Time = DateTime.Parse(reader.GetString(1)),
                    Operator = reader.IsDBNull(2) ? "System" : reader.GetString(2),
                    Action = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CraneId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    OrderNo = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Detail = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Ip = reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Database] 查询操作日志失败");
        }
        return result;
    }

    public List<LoadingOrder> QueryOrderHistory(string? craneId = null, int limit = 100)
    {
        var result = new List<LoadingOrder>();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            if (!string.IsNullOrEmpty(craneId))
            {
                cmd.CommandText = "SELECT * FROM OrderHistory WHERE AssignedCraneId = @craneId ORDER BY CreateTime DESC LIMIT @limit";
                cmd.Parameters.AddWithValue("@craneId", craneId);
            }
            else
            {
                cmd.CommandText = "SELECT * FROM OrderHistory ORDER BY CreateTime DESC LIMIT @limit";
            }
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new LoadingOrder
                {
                    OrderNo = reader.GetString(0),
                    Source = Enum.Parse<OrderSource>(reader.IsDBNull(1) ? "Manual" : reader.GetString(1)),
                    Status = Enum.Parse<OrderStatus>(reader.GetString(2)),
                    CustomerName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    VehicleNo = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ProductName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    PlannedWeight = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                    ActualWeight = reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                    AssignedCraneId = reader.IsDBNull(8) ? null : reader.GetString(8),
                    CreateTime = DateTime.Parse(reader.GetString(9)),
                    DispatchTime = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)),
                    CompleteTime = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11))
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Database] 查询单据历史失败");
        }
        return result;
    }
}

/// <summary>
/// SQLite 扩展方法
/// </summary>
internal static class SqliteExtensions
{
    public static void ExecuteNonQuery(this SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
