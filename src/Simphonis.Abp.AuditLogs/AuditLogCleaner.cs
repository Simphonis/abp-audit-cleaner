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
    private readonly int _deleteBatchSize;

    public AuditLogCleaner(
        IAuditLogRepository auditLogRepository,
        IClock clock,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<AbpMultiTenancyOptions> multiTenancyOptions,
        IAbpLazyServiceProvider lazyServiceProvider,
        IOptions<AuditLogCleanerOptions> options)
    {
        int deleteBatchSize = options.Value.DeleteBatchSize;
        if (deleteBatchSize <= 0 || deleteBatchSize > SimphonisAuditLogConsts.MaximumPossibleRowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(options), deleteBatchSize, $"{nameof(AuditLogCleanerOptions)}.{nameof(AuditLogCleanerOptions.DeleteBatchSize)} must be strictly positive and cannot exceed {SimphonisAuditLogConsts.MaximumPossibleRowCount}.");
        }

        _deleteBatchSize = deleteBatchSize;
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
    /// within the limit of <paramref name="maxRowCount"/> rows, oldest first, in batches of <see cref="AuditLogCleanerOptions.DeleteBatchSize"/> rows.
    /// Each batch is deleted by its own statement, in its own unit of work, so that the database locks are released between two batches.
    /// </summary>
    /// <remarks>
    /// When more rows than a batch share the same execution time, they are deleted together, even if they exceed the batch size or <paramref name="maxRowCount"/>:
    /// the cleanup could not go past them otherwise.
    /// </remarks>
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

        DateTime? oldestExecutionTime;
        using (IUnitOfWork uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            oldestExecutionTime = await GetSecurityThreshold(tenantId, null, 0, cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }

        if (!oldestExecutionTime.HasValue || oldestExecutionTime.Value >= minimumCreationTime)
        {
            // Nothing to delete: the delete statement, which joins all the cascaded tables, is not run at all
            return;
        }

        // Each batch starts where the previous one ended, so that it only reads its own rows, instead of going through the rows
        // deleted by the previous batches again (which the database engine may not have physically removed yet).
        // Rows written late with an older execution time are left for the next run, which starts from the oldest row again.
        DateTime batchStart = oldestExecutionTime.Value;
        int remainingRows = maxRowCount;

        while (remainingRows > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int batchSize = Math.Min(_deleteBatchSize, remainingRows);

            // One unit of work per batch: its locks are released before the next batch, so concurrent audit log writes wait at most one batch
            using IUnitOfWork uow = _unitOfWorkManager.Begin(requiresNew: true);

            // The batch ends at the first row after the batch size, or at the retention limit if it comes first
            DateTime? batchThreshold = await GetSecurityThreshold(tenantId, batchStart, batchSize, cancellationToken);
            DateTime batchEnd = batchThreshold.HasValue && batchThreshold.Value < minimumCreationTime ? batchThreshold.Value : minimumCreationTime;

            if (batchEnd == batchStart)
            {
                // More than a batch of rows share the batch's start time: the strict comparison would delete none of them
                await _auditLogRepository.DeleteDirectAsync(a => a.TenantId == tenantId && a.ExecutionTime == batchStart, cancellationToken);
            }
            else
            {
                await _auditLogRepository.DeleteDirectAsync(a => a.TenantId == tenantId && a.ExecutionTime >= batchStart && a.ExecutionTime < batchEnd, cancellationToken);
            }

            await uow.CompleteAsync(cancellationToken);

            if (batchEnd >= minimumCreationTime)
            {
                break;
            }

            remainingRows -= batchSize;
            batchStart = batchEnd;
        }
    }

    /// <summary>
    /// Returns the execution time of the Nth audit log of a tenant, sorted by execution time
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant, or <see langword="null"/> for the host</param>
    /// <param name="minimumExecutionTime">When set, only the rows executed at or after this time are counted</param>
    /// <param name="rowCount">Number of rows to skip: 0 returns the oldest execution time</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The execution time found, or <see langword="null"/> if the tenant does not have enough rows.</returns>
    protected virtual async Task<DateTime?> GetSecurityThreshold(Guid? tenantId, DateTime? minimumExecutionTime, int rowCount, CancellationToken cancellationToken = default)
    {
        IQueryable<AuditLog> query = await _auditLogRepository.GetQueryableAsync();
        query = query.Where(l => l.TenantId == tenantId);

        if (minimumExecutionTime.HasValue)
        {
            DateTime minimum = minimumExecutionTime.Value;
            query = query.Where(l => l.ExecutionTime >= minimum);
        }

        IQueryable<DateTime> timeList = query.OrderBy(l => l.ExecutionTime)
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
