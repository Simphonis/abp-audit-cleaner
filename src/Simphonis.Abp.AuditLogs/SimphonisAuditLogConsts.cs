namespace Simphonis.Abp.AuditLogs;

public static class SimphonisAuditLogConsts
{
    /// <summary>
    /// Valeur par défaut pour la période de rétention maximale
    /// </summary>
    public const int DefaultRetentionPeriod = 365;

    /// <summary>
    /// Représente la valeur la plus élevée qui peut être utilisée pour désigner le nombre de lignes maximal
    /// supprimables en un bloc.
    /// </summary>
    public const int MaximumPossibleRowCount = 1_000_000;

    /// <summary>
    /// Valeur par défaut pour le nombre maximum de lignes supprimées à chaque passage du batch
    /// </summary>
    public const int DefaultMaximumRowCount = 10_000;
}
