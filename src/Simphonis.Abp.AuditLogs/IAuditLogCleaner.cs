namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Utility service responsible for implementing the audit log cleanup logic.
/// </summary>
public interface IAuditLogCleaner
{
    /// <summary>
    /// Deletes all audit log entries whose age is greater than the specified value.
    /// Note that these deletes are made on their own unit of work.
    /// </summary>
    /// <remarks>
    /// When ABP multi-tenancy is disabled, only the host entries (those without a tenant) are cleaned.
    /// When it is enabled, the host and every tenant listed by <see cref="Volo.Abp.MultiTenancy.ITenantStore"/> are cleaned one after the other.
    /// </remarks>
    /// <param name="maxAge">Maximum age beyond which entries must be deleted</param>
    /// <param name="maxRowCount">
    /// Maximum number of rows to delete during this operation, per tenant.
    /// The provided value cannot exceed the one in <see cref="SimphonisAuditLogConsts.MaximumPossibleRowCount"/>.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="AggregateException">An unexpected error occurred while cleaning one or more of the tenants</exception>
    Task CleanAsync(TimeSpan maxAge, int maxRowCount = 10000, CancellationToken cancellationToken = default);
}
