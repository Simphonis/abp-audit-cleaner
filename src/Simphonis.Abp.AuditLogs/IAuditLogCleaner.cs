namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Utility service responsible for implementing the audit log cleanup logic.
/// </summary>
public interface IAuditLogCleaner
{
    /// <summary>
    /// Deletes all audit log entries whose age is greater than the specified value.
    /// </summary>
    /// <param name="maxAge">Maximum age beyond which entries must be deleted</param>
    /// <param name="maxRowCount">
    /// Maximum number of rows to delete during this operation. The provided value cannot exceed the one in <see cref="SimphonisAuditLogConsts.MaximumPossibleRowCount"/>.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task CleanAsync(TimeSpan maxAge, int maxRowCount = 10000, CancellationToken cancellationToken = default);
}
