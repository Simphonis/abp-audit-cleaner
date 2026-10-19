namespace Simphonis.Abp.AuditLogs;

public static class SimphonisAuditLogSettings
{
    private const string Prefix = "Simphonis.AuditLogs";

    /// <summary>
    /// Maximum age of the audit log rows, in days.
    /// </summary>
    public const string AuditLogRetentionPeriod = Prefix + ".AuditLogRetentionPeriod";

    /// <summary>
    /// Maximum number of rows that can be deleted on each run of the deletion batch
    /// </summary>
    public const string MaxDeleteCount = Prefix + ".MaxDeleteCount";
}
