using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace Simphonis.Abp.AuditLogs;
/// <summary>
/// Implémentation par défaut de <see cref="IAuditLogCleaner"/>, qui utilise <see cref="IAuditLogRepository"/> pour faire son nettoyage
/// </summary>
public class AuditLogCleaner : ITransientDependency, IAuditLogCleaner
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IClock _clock;

    public AuditLogCleaner(IAuditLogRepository auditLogRepository, IClock clock)
    {
        _auditLogRepository = auditLogRepository;
        _clock = clock;
    }

    /// <inheritdoc />
    public virtual async Task CleanAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        DateTime minimumCreationTime = _clock.Now - maxAge;

        await _auditLogRepository.DeleteDirectAsync(a => a.ExecutionTime < minimumCreationTime, cancellationToken);
    }
}
