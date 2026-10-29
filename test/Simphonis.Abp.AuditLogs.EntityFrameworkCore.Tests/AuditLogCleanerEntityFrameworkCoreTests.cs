using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Runs <see cref="AuditLogCleaner"/> against the real EF Core repository, so that ABP's data filters
/// (in particular the multi-tenant one) apply exactly as they do in an application
/// </summary>
public sealed class AuditLogCleanerEntityFrameworkCoreTests : IAsyncLifetime
{
    private IAbpApplicationWithInternalServiceProvider _application = null!;

    private IServiceProvider ServiceProvider => _application.ServiceProvider;

    private DateTime Now => ServiceProvider.GetRequiredService<IClock>().Now;

    public async ValueTask InitializeAsync()
    {
        // Autofac is required for the property injection used by AbpDbContext, as in an ABP application
        _application = await AbpApplicationFactory.CreateAsync<AuditLogsEntityFrameworkCoreTestModule>(options => options.UseAutofac());
        await _application.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _application.ShutdownAsync();
        _application.Dispose();
    }

    private static AuditLog CreateTestEntry(DateTime executionTime, Guid? tenantId) => new(
        Guid.NewGuid(),
        "UnitTest",
        tenantId,
        null,
        null,
        null,
        executionTime,
        23,
        "1.2.3.4",
        "TestClient",
        "TestClientId",
        "asl;kfjasdl;kfjasdkl;fj",
        "UnitTests",
        "GET",
        "https://www.example.org/unit-tests",
        200,
        null,
        null,
        null,
        null,
        new ExtraPropertyDictionary(),
        null,
        null,
        null,
        "Test entry");

    /// <summary>
    /// Inserts an audit log entry as the given tenant would, in its own unit of work
    /// </summary>
    private async Task InsertAsync(DateTime executionTime, Guid? tenantId)
    {
        using (ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId))
        {
            using IUnitOfWork uow = ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            await ServiceProvider.GetRequiredService<IAuditLogRepository>().InsertAsync(CreateTestEntry(executionTime, tenantId), cancellationToken: TestContext.Current.CancellationToken);
            await uow.CompleteAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Runs the cleaner from the host, in its own unit of work, like the worker does
    /// </summary>
    private async Task CleanAsync(TimeSpan maxAge, int maxRowCount)
    {
        using IUnitOfWork uow = ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await ServiceProvider.GetRequiredService<IAuditLogCleaner>().CleanAsync(maxAge, maxRowCount, TestContext.Current.CancellationToken);
        await uow.CompleteAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Returns the execution times of all the entries left in the database, regardless of their tenant
    /// </summary>
    private async Task<List<DateTime>> GetRemainingExecutionTimesAsync()
    {
        using IDisposable _ = ServiceProvider.GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
        using IUnitOfWork uow = ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);

        IQueryable<AuditLog> query = await ServiceProvider.GetRequiredService<IAuditLogRepository>().GetQueryableAsync();
        List<DateTime> result = await ServiceProvider.GetRequiredService<IAsyncQueryableExecuter>()
            .ToListAsync(query.OrderBy(a => a.ExecutionTime).Select(a => a.ExecutionTime), TestContext.Current.CancellationToken);

        await uow.CompleteAsync(TestContext.Current.CancellationToken);
        return result;
    }

    [Fact]
    public async Task Should_Clean_AuditLogs_Of_All_Tenants()
    {
        // Arrange
        DateTime recentDate = Now.AddDays(-25);
        await InsertAsync(recentDate, null);                        // Recent enough entry that will be kept
        await InsertAsync(Now.AddDays(-35), null);                  // Older entry that will be deleted - host (no tenant)
        await InsertAsync(Now.AddDays(-36), Guid.NewGuid());        // Older entry that will be deleted - with tenant

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10000);

        // Assert
        // we should only have the more recent entry left
        (await GetRemainingExecutionTimesAsync()).ShouldBe([recentDate]);
    }

    [Fact]
    public async Task Applies_Security_Threshold_Across_Tenants()
    {
        // Arrange
        // 11 old rows spread across the host and two tenants, with a max number of deletions of 10 rows:
        // the threshold must count the rows of all tenants, not only the host ones
        DateTime now = Now;
        Guid tenant1 = Guid.NewGuid();
        Guid tenant2 = Guid.NewGuid();

        await InsertAsync(now.AddDays(-300), null);
        await InsertAsync(now.AddDays(-290), tenant1);
        await InsertAsync(now.AddDays(-280), tenant2);
        await InsertAsync(now.AddDays(-270), null);
        await InsertAsync(now.AddDays(-260), tenant1);
        await InsertAsync(now.AddDays(-250), tenant2);
        await InsertAsync(now.AddDays(-240), null);
        await InsertAsync(now.AddDays(-230), tenant1);
        await InsertAsync(now.AddDays(-220), tenant2);
        await InsertAsync(now.AddDays(-210), null);
        await InsertAsync(now.AddDays(-200), tenant1);
        // 👆 Data too old that we want to delete (but there's too much of it so we'll keep the most recent)
        // 👇 Data to keep
        await InsertAsync(now.AddDays(-30), tenant2);

        // Act
        await CleanAsync(TimeSpan.FromDays(30), 10);

        // Assert
        // We should have two entries left: the one that is recent enough, and the one from 200 days ago
        // which could not be included in the 10 rows
        (await GetRemainingExecutionTimesAsync()).ShouldBe([now.AddDays(-200), now.AddDays(-30)]);
    }
}
