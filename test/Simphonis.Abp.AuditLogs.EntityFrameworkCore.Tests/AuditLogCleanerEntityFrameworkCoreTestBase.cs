using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.Linq;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Runs <see cref="AuditLogCleaner"/> against the real EF Core repositories of the <typeparamref name="TStartupModule"/> application,
/// so that ABP's data filters (in particular the multi-tenant one) behave exactly as they do in an application
/// </summary>
/// <typeparam name="TStartupModule">Module of the test application to start</typeparam>
public abstract class AuditLogCleanerEntityFrameworkCoreTestBase<TStartupModule> : IAsyncDisposable
    where TStartupModule : IAbpModule
{
    private IAbpApplicationWithInternalServiceProvider? _application;

    protected IServiceProvider ServiceProvider => _application?.ServiceProvider ?? throw new InvalidOperationException("The application has not been started");

    protected DateTime Now => ServiceProvider.GetRequiredService<IClock>().Now;

    /// <summary>
    /// Creates and starts the test application. Each test starts its own, to choose whether multi-tenancy is enabled.
    /// </summary>
    /// <param name="isMultiTenancyEnabled">Value of <see cref="AbpMultiTenancyOptions.IsEnabled"/></param>
    /// <param name="configureServices">Additional service configuration for the test</param>
    protected async Task StartApplicationAsync(bool isMultiTenancyEnabled, Action<IServiceCollection>? configureServices = null)
    {
        _application = await AbpApplicationFactory.CreateAsync<TStartupModule>(options =>
        {
            // Autofac is required for the property injection used by AbpDbContext, as in an ABP application
            options.UseAutofac();
            options.Services.Configure<AbpMultiTenancyOptions>(o => o.IsEnabled = isMultiTenancyEnabled);
            configureServices?.Invoke(options.Services);
        });
        await _application.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_application != null)
        {
            await _application.ShutdownAsync();
            _application.Dispose();
        }
        GC.SuppressFinalize(this);
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
    protected async Task InsertAsync(DateTime executionTime, Guid? tenantId)
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
    protected async Task CleanAsync(TimeSpan maxAge, int maxRowCount)
    {
        using IUnitOfWork uow = ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await ServiceProvider.GetRequiredService<IAuditLogCleaner>().CleanAsync(maxAge, maxRowCount, TestContext.Current.CancellationToken);
        await uow.CompleteAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Returns the execution times of all the entries left in the database, regardless of their tenant
    /// </summary>
    protected async Task<List<DateTime>> GetRemainingExecutionTimesAsync()
    {
        using IDisposable _ = ServiceProvider.GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
        using IUnitOfWork uow = ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);

        IQueryable<AuditLog> query = await ServiceProvider.GetRequiredService<IAuditLogRepository>().GetQueryableAsync();
        List<DateTime> result = await ServiceProvider.GetRequiredService<IAsyncQueryableExecuter>()
            .ToListAsync(query.OrderBy(a => a.ExecutionTime).Select(a => a.ExecutionTime), TestContext.Current.CancellationToken);

        await uow.CompleteAsync(TestContext.Current.CancellationToken);
        return result;
    }
}
