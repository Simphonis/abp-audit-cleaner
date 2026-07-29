namespace Simphonis.Abp.AuditLogs;

public static class SimphonisAuditLogSettings
{
    private const string Prefix = "Simphonis.AuditLogs";

    /// <summary>
    /// Age maximal des lignes du log d'audit, en jours.
    /// </summary>
    public const string AuditLogRetentionPeriod = Prefix + ".AuditLogRetentionPeriod";

    /// <summary>
    /// Nombre maximal de lignes supprimables à chaque passage du batch de suppression
    /// </summary>
    public const string MaxDeleteCount = Prefix + ".MaxDeleteCount";
}
