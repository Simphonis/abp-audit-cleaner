namespace Simphonis.Abp.AuditLogs;

public static class SimphonisAuditLogConsts
{
    /// <summary>
    /// Default value for the maximum retention period
    /// </summary>
    public const int DefaultRetentionPeriod = 365;

    /// <summary>
    /// Represents the highest value that can be used to specify the maximum number of rows
    /// that can be deleted in a single cleanup operation.
    /// </summary>
    public const int MaximumPossibleRowCount = 1_000_000;

    /// <summary>
    /// Default value for the maximum number of rows deleted on each run of the batch
    /// </summary>
    public const int DefaultMaximumRowCount = 10_000;

    /// <summary>
    /// Default interval, in minutes, between two runs of the cleanup worker
    /// </summary>
    public const int DefaultWorkerPeriodInMinutes = 10;
}
