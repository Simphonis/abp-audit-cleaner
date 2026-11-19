using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Modularity;

namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Minimal ABP application using the real audit log EF Core repository, on top of an in-memory SQLite database.
/// It has no tenant management tables nor repository, like an application that does not use the ABP tenant registry.
/// Multi-tenancy is left to its ABP default (disabled): tests enable it when they create the application.
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

        // Exposed so that dependent modules can create their own tables in the same database
        context.Services.AddSingleton(_sqliteConnection);

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

        using AbpAuditLoggingDbContext context = new(new DbContextOptionsBuilder<AbpAuditLoggingDbContext>().UseSqlite(connection).Options);
        context.GetService<IRelationalDatabaseCreator>().CreateTables();

        return connection;
    }
}
