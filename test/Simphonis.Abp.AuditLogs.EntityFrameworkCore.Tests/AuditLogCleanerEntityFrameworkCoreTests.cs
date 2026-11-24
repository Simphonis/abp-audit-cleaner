using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Xunit;

namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Cleanup tests in an application that uses the ABP tenant registry (tenant management tables and repository)
/// </summary>
public sealed class AuditLogCleanerEntityFrameworkCoreTests : AuditLogCleanerEntityFrameworkCoreTestBase<AuditLogsTenantManagementTestModule>
{
    /// <summary>
    /// Registers a new tenant in the ABP tenant registry, and returns its identifier
    /// </summary>
    private async Task<Guid> CreateTenantAsync(string name)
    {
        using IUnitOfWork uow = ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        Tenant tenant = await ServiceProvider.GetRequiredService<ITenantManager>().CreateAsync(name);
        await ServiceProvider.GetRequiredService<ITenantRepository>().InsertAsync(tenant, cancellationToken: TestContext.Current.CancellationToken);
        await uow.CompleteAsync(TestContext.Current.CancellationToken);
        return tenant.Id;
    }

    /// <summary>
    /// Deletes a tenant from the ABP tenant registry
    /// </summary>
    private async Task DeleteTenantAsync(Guid tenantId)
    {
        using IUnitOfWork uow = ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await ServiceProvider.GetRequiredService<ITenantRepository>().DeleteAsync(tenantId, cancellationToken: TestContext.Current.CancellationToken);
        await uow.CompleteAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Clean_AuditLogs_Of_All_Tenants()
    {
        // Arrange
        await StartApplicationAsync(isMultiTenancyEnabled: true);
        Guid tenant1 = await CreateTenantAsync("Tenant1");
        Guid tenant2 = await CreateTenantAsync("Tenant2");

        DateTime now = Now;
        await InsertAsync(now.AddDays(-25), null);          // Recent enough entry that will be kept - host
        await InsertAsync(now.AddDays(-35), null);          // Older entry that will be deleted - host
        await InsertAsync(now.AddDays(-36), tenant1);       // Older entry that will be deleted - tenant 1
        await InsertAsync(now.AddDays(-37), tenant2);       // Older entry that will be deleted - tenant 2
        await InsertAsync(now.AddDays(-20), tenant2);       // Recent enough entry that will be kept - tenant 2

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10000);

        // Assert
        // we should only have the more recent entries left
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-25), now.AddDays(-20)]);
    }

    [Fact]
    public async Task Applies_Security_Threshold_Per_Tenant()
    {
        // Arrange
        // With a max number of deletions of 2 rows, each tenant (and the host) can lose up to 2 rows, regardless of the others
        await StartApplicationAsync(isMultiTenancyEnabled: true);
        Guid tenant1 = await CreateTenantAsync("Tenant1");
        Guid tenant2 = await CreateTenantAsync("Tenant2");

        DateTime now = Now;
        // Host: 3 old rows, the most recent one is kept
        await InsertAsync(now.AddDays(-300), null);
        await InsertAsync(now.AddDays(-290), null);
        await InsertAsync(now.AddDays(-280), null);
        // Tenant 1: 2 old rows, all deleted
        await InsertAsync(now.AddDays(-270), tenant1);
        await InsertAsync(now.AddDays(-260), tenant1);
        // Tenant 2: 4 old rows, the 2 most recent ones are kept, and a recent row that is kept
        await InsertAsync(now.AddDays(-250), tenant2);
        await InsertAsync(now.AddDays(-240), tenant2);
        await InsertAsync(now.AddDays(-230), tenant2);
        await InsertAsync(now.AddDays(-220), tenant2);
        await InsertAsync(now.AddDays(-10), tenant2);

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 2);

        // Assert
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-280), now.AddDays(-230), now.AddDays(-220), now.AddDays(-10)]);
    }

    [Fact]
    public async Task Should_Clean_AuditLogs_Of_Unregistered_Tenants()
    {
        // Arrange
        await StartApplicationAsync(isMultiTenancyEnabled: true);

        DateTime now = Now;
        await InsertAsync(now.AddDays(-35), null);              // Older entry that will be deleted - host
        await InsertAsync(now.AddDays(-36), Guid.NewGuid());    // Older entry of an unknown tenant, that will be deleted
        await InsertAsync(now.AddDays(-20), Guid.NewGuid());    // Recent enough entry of an unknown tenant, that will be kept

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10000);

        // Assert
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-20)]);
    }

    [Fact]
    public async Task Should_Clean_AuditLogs_Of_Deleted_Tenants()
    {
        // Arrange
        // A deleted tenant is no longer listed by the tenant store (tenant management soft-deletes it)
        await StartApplicationAsync(isMultiTenancyEnabled: true);
        Guid deletedTenant = await CreateTenantAsync("DeletedTenant");

        DateTime now = Now;
        await InsertAsync(now.AddDays(-25), deletedTenant);     // Recent enough entry that will be kept
        await InsertAsync(now.AddDays(-35), deletedTenant);     // Older entry that will be deleted
        await DeleteTenantAsync(deletedTenant);

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10000);

        // Assert
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-25)]);
    }

    [Fact]
    public async Task Should_Clean_Tenant_AuditLogs_When_MultiTenancy_Is_Disabled()
    {
        // Arrange
        await StartApplicationAsync(isMultiTenancyEnabled: false);
        Guid tenant = await CreateTenantAsync("Tenant1");

        DateTime now = Now;
        await InsertAsync(now.AddDays(-25), null);      // Recent enough entry that will be kept
        await InsertAsync(now.AddDays(-35), null);      // Older entry that will be deleted
        await InsertAsync(now.AddDays(-36), tenant);    // Older entry of a registered tenant, that will be deleted

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10000);

        // Assert
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-25)]);
    }
}
