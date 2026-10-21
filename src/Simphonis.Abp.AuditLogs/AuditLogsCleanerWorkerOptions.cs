namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Options of the <see cref="AuditLogsCleanerWorker"/>, to be configured in a module's <c>ConfigureServices</c> method
/// </summary>
public class AuditLogsCleanerWorkerOptions
{
    /// <summary>
    /// Interval between two runs of the cleanup worker. Must be strictly positive, and lower than <see cref="int.MaxValue"/> milliseconds (about 24 days).
    /// Defaults to <see cref="SimphonisAuditLogConsts.DefaultWorkerPeriodInMinutes"/> minutes.
    /// </summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromMinutes(SimphonisAuditLogConsts.DefaultWorkerPeriodInMinutes);
}
