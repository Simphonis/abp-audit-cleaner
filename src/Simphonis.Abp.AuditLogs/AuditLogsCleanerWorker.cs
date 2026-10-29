using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Simphonis.Abp.AuditLogs;
/// <summary>
/// Worker that runs a cleanup of the audit log at the interval defined in <see cref="AuditLogsCleanerWorkerOptions.Period"/>, keeping the retention period defined in the setting <see cref="SimphonisAuditLogSettings.AuditLogRetentionPeriod"/>
/// </summary>
public class AuditLogsCleanerWorker : AsyncPeriodicBackgroundWorkerBase
{
    public AuditLogsCleanerWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory, IOptions<AuditLogsCleanerWorkerOptions> options) : base(timer, serviceScopeFactory)
    {
        TimeSpan period = options.Value.Period;
        if (period.TotalMilliseconds < 1 || period.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(options), period, $"{nameof(AuditLogsCleanerWorkerOptions)}.{nameof(AuditLogsCleanerWorkerOptions.Period)} must be strictly positive and lower than {int.MaxValue} milliseconds.");
        }

        Timer.Period = (int)period.TotalMilliseconds;
    }

    [UnitOfWork]
    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        IAuditLogCleaner auditLogRepository = workerContext.ServiceProvider.GetRequiredService<IAuditLogCleaner>();
        ISettingProvider settingValueProvider = workerContext.ServiceProvider.GetRequiredService<ISettingProvider>();
        ILogger logger = workerContext.ServiceProvider.GetRequiredService<ILogger<AuditLogsCleanerWorker>>();

        using IDisposable? _ = logger.BeginScope(nameof(AuditLogsCleanerWorker));
        Stopwatch watch = Stopwatch.StartNew();

        int retentionDays = await ReadRetentionPeriod(settingValueProvider, logger);
        TimeSpan maxAge = TimeSpan.FromDays(retentionDays);

        int maxRowCount = await ReadMaxRowCount(settingValueProvider, logger);

        await auditLogRepository.CleanAsync(maxAge, maxRowCount, workerContext.CancellationToken);

        watch.Stop();
        logger.LogInformation("Audit log cleaner ran in {Elapsed}ms", watch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Safely reads and returns the setting containing the retention period to apply
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
