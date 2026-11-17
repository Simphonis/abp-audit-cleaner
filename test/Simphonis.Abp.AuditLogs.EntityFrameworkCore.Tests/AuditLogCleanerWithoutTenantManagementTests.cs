using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.MultiTenancy.ConfigurationStore;
using Xunit;

namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Cleanup tests in an application that does not use the ABP tenant registry: no tenant management tables nor repository
/// </summary>
public sealed class AuditLogCleanerWithoutTenantManagementTests : AuditLogCleanerEntityFrameworkCoreTestBase<AuditLogsEntityFrameworkCoreTestModule>
{
    [Fact]
    public async Task Should_Clean_Host_AuditLogs_When_MultiTenancy_Is_Disabled()
    {
        // Arrange
        await StartApplicationAsync(isMultiTenancyEnabled: false);

        DateTime now = Now;
        await InsertAsync(now.AddDays(-25), null);      // Recent enough entry that will be kept
        await InsertAsync(now.AddDays(-35), null);      // Older entry that will be deleted
        await InsertAsync(now.AddDays(-36), null);      // Older entry that will be deleted

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10000);

        // Assert
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-25)]);
    }

    [Fact]
    public async Task Should_Not_Clean_Tenant_AuditLogs_When_MultiTenancy_Is_Disabled()
    {
        // Arrange
        await StartApplicationAsync(isMultiTenancyEnabled: false);

        DateTime now = Now;
        await InsertAsync(now.AddDays(-35), null);              // Older entry that will be deleted
        await InsertAsync(now.AddDays(-36), Guid.NewGuid());    // Older entry with a tenant, that will be kept

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10000);

        // Assert
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-36)]);
    }

    [Fact]
    public async Task Applies_Security_Threshold_When_MultiTenancy_Is_Disabled()
    {
        // Arrange
        await StartApplicationAsync(isMultiTenancyEnabled: false);

        DateTime now = Now;
        await InsertAsync(now.AddDays(-300), null);
        await InsertAsync(now.AddDays(-290), null);
        await InsertAsync(now.AddDays(-280), null);     // Old, but beyond the 2 rows that can be deleted: kept

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 2);

        // Assert
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-280)]);
    }

    [Fact]
    public async Task Should_Clean_Configured_Tenants_When_MultiTenancy_Is_Enabled()
    {
        // Arrange
        // Without the tenant management module, the tenants come from the configuration (ABP's default tenant store)
        Guid configuredTenant = Guid.NewGuid();
        await StartApplicationAsync(isMultiTenancyEnabled: true, services =>
            services.Configure<AbpDefaultTenantStoreOptions>(o => o.Tenants = [new TenantConfiguration(configuredTenant, "Tenant1")]));

        DateTime now = Now;
        await InsertAsync(now.AddDays(-25), null);              // Recent enough entry that will be kept - host
        await InsertAsync(now.AddDays(-35), null);              // Older entry that will be deleted - host
        await InsertAsync(now.AddDays(-36), configuredTenant);  // Older entry that will be deleted - configured tenant
        await InsertAsync(now.AddDays(-37), Guid.NewGuid());    // Older entry of an unknown tenant, that will be kept

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10000);

        // Assert
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-37), now.AddDays(-25)]);
    }
}
