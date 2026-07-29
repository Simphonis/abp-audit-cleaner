using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Simphonis.Abp.AuditLogs;
/// <summary>
/// Worker qui exécute un nettoyage du log d'audit toutes les 10 minutes, en conservant la durée de rétention définie dans le setting <see cref="SimphonisAuditLogSettings.AuditLogRetentionPeriod"/>
/// </summary>
public class AuditLogsCleanerWorker : AsyncPeriodicBackgroundWorkerBase
{
    public AuditLogsCleanerWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory) : base(timer, serviceScopeFactory)
    {
        int periodInMinutes = 10;
        Timer.Period = periodInMinutes * 60 * 1000;
    }

    [UnitOfWork]
    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        IAuditLogCleaner auditLogRepository = workerContext.ServiceProvider.GetRequiredService<IAuditLogCleaner>();
        ISettingProvider settingValueProvider = workerContext.ServiceProvider.GetRequiredService<ISettingProvider>();
        ILogger logger = workerContext.ServiceProvider.GetRequiredService<ILogger<AuditLogsCleanerWorker>>();

        int retentionDays = await ReadRetentionPeriod(settingValueProvider, logger);
        TimeSpan maxAge = TimeSpan.FromDays(retentionDays);

        int maxRowCount = await ReadMaxRowCount(settingValueProvider, logger);

        await auditLogRepository.CleanAsync(maxAge, maxRowCount, workerContext.CancellationToken);
    }

    /// <summary>
    /// Lecture sécurisée du setting contenant la durée de rétention à appliquer
    /// </summary>
    /// <param name="settingValueProvider"></param>
    /// <param name="logger"></param>
    /// <returns></returns>
    internal static async Task<int> ReadRetentionPeriod(ISettingProvider settingValueProvider, ILogger logger)
    {
        string? strRetentionDays = await settingValueProvider.GetOrNullAsync(SimphonisAuditLogSettings.AuditLogRetentionPeriod);

        int result;
        if (strRetentionDays == null)
        {
            logger.LogWarning("Retention period setting is not set. Using default value: {DefaultRetentionPeriod} days.", SimphonisAuditLogConsts.DefaultRetentionPeriod);
            return SimphonisAuditLogConsts.DefaultRetentionPeriod;
        }
        else if (!int.TryParse(strRetentionDays, NumberFormatInfo.InvariantInfo, out result))
        {
            logger.LogWarning("Retention period setting is not a valid integer. Using default value: {DefaultRetentionPeriod} days.", SimphonisAuditLogConsts.DefaultRetentionPeriod);
            return SimphonisAuditLogConsts.DefaultRetentionPeriod;
        }

        if (result < 0)
        {
            logger.LogWarning("The retention period cannot be negative (currently {RetentionPeriod})", result);
            return SimphonisAuditLogConsts.DefaultRetentionPeriod;
        }

        return result;
    }

    internal static async Task<int> ReadMaxRowCount(ISettingProvider settingValueProvider, ILogger logger)
    {
        string? strMaxRowCount = await settingValueProvider.GetOrNullAsync(SimphonisAuditLogSettings.MaxDeleteCount);
        int result;
        if (strMaxRowCount == null)
        {
            logger.LogWarning("Maximum delete count is not set. Using default value: {DefaultMaximumRowCount} days.", SimphonisAuditLogConsts.DefaultMaximumRowCount);
            return SimphonisAuditLogConsts.DefaultMaximumRowCount;
        }
        else if (!int.TryParse(strMaxRowCount, NumberFormatInfo.InvariantInfo, out result))
        {
            logger.LogWarning("Maximum delete count setting is not a valid integer. Using default value: {DefaultMaximumRowCount} days.", SimphonisAuditLogConsts.DefaultRetentionPeriod);
            return SimphonisAuditLogConsts.DefaultMaximumRowCount;
        }

        if (result <= 0)
        {
            logger.LogWarning("The maximum delete count cannot be negative (currently {MaximumDeleteCount})", result);
            return SimphonisAuditLogConsts.DefaultMaximumRowCount;
        }

        return result;
    }
}
