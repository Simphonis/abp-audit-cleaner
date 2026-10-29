using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

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

    public AuditLogCleaner(IAuditLogRepository auditLogRepository, IClock clock, IAsyncQueryableExecuter asyncExecuter, IDataFilter dataFilter)
    {
        _auditLogRepository = auditLogRepository;
        _clock = clock;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
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

        // Clean the audit logs for all tenants (in the current database)
        using IDisposable _ = _dataFilter.Disable<IMultiTenant>();

        DateTime minimumCreationTime = _clock.Now - maxAge;
        DateTime? securityCreationTime = await GetSecurityThreshold(maxRowCount);

        // If the security date is less restrictive than the configured date, we will use the security one
        // This avoids deleting millions of rows, which risks locking the DB for too long.
        if (securityCreationTime.HasValue && minimumCreationTime > securityCreationTime)
        {
            minimumCreationTime = securityCreationTime.Value;
        }

        await _auditLogRepository.DeleteDirectAsync(a => a.ExecutionTime < minimumCreationTime, cancellationToken);
    }

    /// <summary>
    /// Returns the creation date of the Nth element in the audit logs table
    /// </summary>
    /// <param name="rowCount">Position for which we want to retrieve the creation date (after sorting by creation date)</param>
    /// <returns>The creation date found, or <see langword="null"/> if the table does not contain enough rows.</returns>
    protected virtual async Task<DateTime?> GetSecurityThreshold(int rowCount)
    {
        IQueryable<AuditLog> query = await _auditLogRepository.GetQueryableAsync();

        IQueryable<DateTime> timeList = query.OrderBy(l => l.ExecutionTime)
                                             .Select(l => l.ExecutionTime)
                                             .Skip(rowCount)
                                             .Take(1);

        DateTime result = await _asyncExecuter.FirstOrDefaultAsync(timeList);

        if (result == default)
        {
            // If we get the default value, it means we did not find a row
            return null;
        }
        return result;
    }
}
