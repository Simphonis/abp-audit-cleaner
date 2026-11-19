using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Same application as <see cref="AuditLogsEntityFrameworkCoreTestModule"/>, with the ABP tenant registry
/// (tenant management EF Core repository and tables) in the same in-memory SQLite database.
/// </summary>
[DependsOn(
    typeof(AuditLogsEntityFrameworkCoreTestModule),
    typeof(AbpTenantManagementEntityFrameworkCoreModule)
    )]
public class AuditLogsTenantManagementTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        SqliteConnection connection = context.Services.GetSingletonInstance<SqliteConnection>();

        using TenantManagementDbContext dbContext = new(new DbContextOptionsBuilder<TenantManagementDbContext>().UseSqlite(connection).Options);
        dbContext.GetService<IRelationalDatabaseCreator>().CreateTables();
    }
}
