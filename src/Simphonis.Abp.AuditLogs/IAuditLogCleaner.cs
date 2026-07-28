namespace Simphonis.Abp.AuditLogs
{
    /// <summary>
    /// Service utilitaire chargé d'implémenter la logique de nettoyage des logs d'audit.
    /// </summary>
    public interface IAuditLogCleaner
    {
        /// <summary>
        /// Supprime toutes les entrées du log d'audit dont l'âge est supérieur à la valeur spécifiée.
        /// </summary>
        /// <param name="maxAge">Age maximal au-delà duquel les entrées doivent être supprimées</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task CleanAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
    }
}