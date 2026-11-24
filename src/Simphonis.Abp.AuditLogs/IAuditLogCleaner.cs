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
    /// When ABP multi-tenancy is enabled, the host and every tenant listed by <see cref="Volo.Abp.MultiTenancy.ITenantStore"/> are cleaned one after the other,
    /// each in its own database. Then, every other tenant that has outdated entries in the host database (a tenant that was deleted, or never registered)
    /// is cleaned in the host database. When multi-tenancy is disabled, the host and all the tenants found in its database are cleaned.
    /// The entries of an unlisted tenant that are stored in a database of its own cannot be reached, and are left untouched.
    /// </remarks>
    /// <param name="maxAge">Maximum age beyond which entries must be deleted</param>
    /// <param name="maxRowCount">
    /// Maximum number of rows to delete during this operation, per tenant.
    /// The provided value cannot exceed the one in <see cref="SimphonisAuditLogConsts.MaximumPossibleRowCount"/>.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task CleanAsync(TimeSpan maxAge, int maxRowCount = 10000, CancellationToken cancellationToken = default);
}
