namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Options of the <see cref="AuditLogCleaner"/>, to be configured in a module's <c>ConfigureServices</c> method
/// </summary>
public class AuditLogCleanerOptions
{
    /// <summary>
    /// Maximum number of audit logs deleted by a single statement. The rows of a tenant are deleted in batches of this size, oldest first,
    /// each batch in its own unit of work, so that the database locks are released between two batches and concurrent audit log writes wait at most one batch.
    /// Must be strictly positive, and cannot exceed <see cref="SimphonisAuditLogConsts.MaximumPossibleRowCount"/>.
    /// Defaults to <see cref="SimphonisAuditLogConsts.DefaultDeleteBatchSize"/>.
    /// </summary>
    public int DeleteBatchSize { get; set; } = SimphonisAuditLogConsts.DefaultDeleteBatchSize;
}
