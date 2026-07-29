using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;
using Volo.Abp.Timing;

namespace Simphonis.Abp.AuditLogs;
/// <summary>
/// Implémentation par défaut de <see cref="IAuditLogCleaner"/>, qui utilise <see cref="IAuditLogRepository"/> pour faire son nettoyage
/// </summary>
public class AuditLogCleaner : ITransientDependency, IAuditLogCleaner
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IClock _clock;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public AuditLogCleaner(IAuditLogRepository auditLogRepository, IClock clock, IAsyncQueryableExecuter asyncExecuter)
    {
        _auditLogRepository = auditLogRepository;
        _clock = clock;
        _asyncExecuter = asyncExecuter;
    }

    /// <inheritdoc />
    public virtual async Task CleanAsync(TimeSpan maxAge, int maxRowCount = 10000, CancellationToken cancellationToken = default)
    {
        if (maxAge.TotalMicroseconds <= 0)
        {
            throw new ArgumentException("The audit log max age must be a positive amount of time", nameof(maxAge));
        }

        if (maxRowCount <= 0)
        {
            throw new ArgumentException("The maximum row count must be a positive number.", nameof(maxRowCount));
        }

        if (maxRowCount > SimphonisAuditLogConsts.MaximumPossibleRowCount)
        {
            throw new ArgumentException($"The maximum row count cannot exceed {SimphonisAuditLogConsts.MaximumPossibleRowCount} rows", nameof(maxRowCount));
        }

        DateTime minimumCreationTime = _clock.Now - maxAge;
        DateTime? securityCreationTime = await GetSecurityThreshold(maxRowCount);

        // Si la date de sécurité est moins restrictive que la date configurée, on va utiliser celle de sécurité
        // Ca permet d'éviter de supprimer des millions de lignes, ce qui risque de verrouiller la BDD pendant trop longtemps.
        if (securityCreationTime.HasValue && minimumCreationTime > securityCreationTime)
        {
            minimumCreationTime = securityCreationTime.Value;
        }

        await _auditLogRepository.DeleteDirectAsync(a => a.ExecutionTime < minimumCreationTime, cancellationToken);
    }

    /// <summary>
    /// Renvoie la date de création du Nième élément dans la table des logs d'audit
    /// </summary>
    /// <param name="rowCount">Position dont on veut récupérer la date de création (après un tri par date de création)</param>
    /// <returns>La date de création trouvée, ou <see langword="null"/> si la table ne contient pas assez de lignes.</returns>
    protected virtual async Task<DateTime?> GetSecurityThreshold(int rowCount)
    {
        IQueryable<AuditLog> query = await _auditLogRepository.GetQueryableAsync();

        IQueryable<DateTime> timeList = query.OrderBy(l => l.ExecutionTime)
                                             .Select(l => l.ExecutionTime)
                                             .Skip(rowCount)
                                             .Take(1);

        DateTime result = await _asyncExecuter.FirstOrDefaultAsync(timeList);

        if (result == default)
        {
            // Si on obtient la valeur par défaut, c'est qu'on n'a pas trouvé de ligne
            return null;
        }
        return result;
    }
}
