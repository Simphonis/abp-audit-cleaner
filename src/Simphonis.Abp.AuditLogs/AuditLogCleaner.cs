using Microsoft.Extensions.Options;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Simphonis.Abp.AuditLogs;
/// <summary>
/// Default implementation of <see cref="IAuditLogCleaner"/>, which uses <see cref="IAuditLogRepository"/> to do its cleanup
/// </summary>
public class AuditLogCleaner : ITransientDependency, IAuditLogCleaner
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IClock _clock;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IOptions<AbpMultiTenancyOptions> _multiTenancyOptions;
    private readonly IAbpLazyServiceProvider _lazyServiceProvider;

    public AuditLogCleaner(
        IAuditLogRepository auditLogRepository,
        IClock clock,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<AbpMultiTenancyOptions> multiTenancyOptions,
        IAbpLazyServiceProvider lazyServiceProvider)
    {
        _auditLogRepository = auditLogRepository;
        _clock = clock;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _multiTenancyOptions = multiTenancyOptions;
        _lazyServiceProvider = lazyServiceProvider;
    }

    /// <inheritdoc />
    public virtual async Task CleanAsync(TimeSpan maxAge, int maxRowCount = 10000, CancellationToken cancellationToken = default)
    {
        if (maxAge.TotalMicroseconds <= 0)
        {
            throw new ArgumentException("The audit log max age must be a positive amount of time", nameof(maxAge));
        }

        if (maxRowCount <= 0)
        {
            throw new ArgumentException("The maximum row count must be a positive number.", nameof(maxRowCount));
        }

        if (maxRowCount > SimphonisAuditLogConsts.MaximumPossibleRowCount)
        {
            throw new ArgumentException($"The maximum row count cannot exceed {SimphonisAuditLogConsts.MaximumPossibleRowCount} rows", nameof(maxRowCount));
        }

        // Computed once, so that every tenant is cleaned up to the same point in time
        DateTime minimumCreationTime = _clock.Now - maxAge;

        IReadOnlyList<Guid?> tenantIds = await GetTenantIdsAsync(cancellationToken);
        foreach (Guid? tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CleanTenantAsync(tenantId, minimumCreationTime, maxRowCount, cancellationToken);
        }
    }

    /// <summary>
    /// Returns the identifiers of the tenants whose audit logs must be cleaned, <see langword="null"/> standing for the host.
    /// When multi-tenancy is disabled, only the host is returned. Otherwise, the host is returned along with all the tenants
    /// of the application's <see cref="ITenantStore"/> (ABP Tenant Management, SaaS module, or tenants defined in the configuration).
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual async Task<IReadOnlyList<Guid?>> GetTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        if (!_multiTenancyOptions.Value.IsEnabled)
        {
            return [null];
        }

        // Resolved lazily: applications that do not use multi-tenancy never need the tenant store
        ITenantStore tenantStore = _lazyServiceProvider.LazyGetRequiredService<ITenantStore>();
        IReadOnlyList<TenantConfiguration> tenants = await tenantStore.GetListAsync();

        List<Guid?> result = [null];
        result.AddRange(tenants.Select(t => (Guid?)t.Id));
        return result;
    }

    /// <summary>
    /// Deletes the audit log entries of a single tenant that were created before <paramref name="minimumCreationTime"/>,
    /// within the limit of <paramref name="maxRowCount"/> rows, in a dedicated unit of work.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant to clean, or <see langword="null"/> for the host</param>
    /// <param name="minimumCreationTime">Entries executed before this time are deleted</param>
    /// <param name="maxRowCount">Maximum number of rows to delete for this tenant</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual async Task CleanTenantAsync(Guid? tenantId, DateTime minimumCreationTime, int maxRowCount, CancellationToken cancellationToken = default)
    {
        // Changing the current tenant selects the tenant's database, for tenants that have their own connection string.
        // The tenant filter is then applied explicitly, so the result does not depend on how the repository applies data filters.
        using IDisposable tenantScope = _currentTenant.Change(tenantId);
        using IDisposable filterScope = _dataFilter.Disable<IMultiTenant>();
        using IUnitOfWork uow = _unitOfWorkManager.Begin(requiresNew: true);

        DateTime? securityCreationTime = await GetSecurityThreshold(tenantId, maxRowCount, cancellationToken);

        // If the security date is less restrictive than the configured date, we will use the security one
        // This avoids deleting millions of rows, which risks locking the DB for too long.
        if (securityCreationTime.HasValue && minimumCreationTime > securityCreationTime)
        {
            minimumCreationTime = securityCreationTime.Value;
        }

        await _auditLogRepository.DeleteDirectAsync(a => a.TenantId == tenantId && a.ExecutionTime < minimumCreationTime, cancellationToken);

        await uow.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the creation date of the Nth element in the audit logs of a tenant
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant, or <see langword="null"/> for the host</param>
    /// <param name="rowCount">Position for which we want to retrieve the creation date (after sorting by creation date)</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The creation date found, or <see langword="null"/> if the tenant does not have enough rows.</returns>
    protected virtual async Task<DateTime?> GetSecurityThreshold(Guid? tenantId, int rowCount, CancellationToken cancellationToken = default)
    {
        IQueryable<AuditLog> query = await _auditLogRepository.GetQueryableAsync();

        IQueryable<DateTime> timeList = query.Where(l => l.TenantId == tenantId)
                                             .OrderBy(l => l.ExecutionTime)
                                             .Select(l => l.ExecutionTime)
                                             .Skip(rowCount)
                                             .Take(1);

        DateTime result = await _asyncExecuter.FirstOrDefaultAsync(timeList, cancellationToken);

        if (result == default)
        {
            // If we get the default value, it means we did not find a row
            return null;
        }
        return result;
    }
}
