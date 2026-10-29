using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Modularity;

namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Minimal ABP application using the real audit log EF Core repository, on top of an in-memory SQLite database
/// </summary>
[DependsOn(
    typeof(SimphonisAuditLogsModule),
    typeof(AbpAuditLoggingEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule),
    typeof(AbpAutofacModule)
    )]
public class AuditLogsEntityFrameworkCoreTestModule : AbpModule
{
    /// <summary>
    /// The in-memory database only lives as long as this connection stays open
    /// </summary>
    private SqliteConnection? _sqliteConnection;

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        _sqliteConnection = CreateDatabaseAndGetConnection();

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(configurationContext =>
            {
                configurationContext.DbContextOptions.UseSqlite(_sqliteConnection);
            });
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _sqliteConnection?.Dispose();
    }

    private static SqliteConnection CreateDatabaseAndGetConnection()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        DbContextOptions<AbpAuditLoggingDbContext> options = new DbContextOptionsBuilder<AbpAuditLoggingDbContext>()
            .UseSqlite(connection)
            .Options;

        using AbpAuditLoggingDbContext context = new(options);
        context.GetService<IRelationalDatabaseCreator>().CreateTables();

        return connection;
    }
}
