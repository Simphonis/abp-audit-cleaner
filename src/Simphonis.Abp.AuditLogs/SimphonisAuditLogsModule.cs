using Simphonis.Abp.AuditLogs.Localization;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Simphonis.Abp.AuditLogs;

[DependsOn(typeof(AbpBackgroundWorkersModule),
           typeof(AbpAuditLoggingDomainModule))]
public class SimphonisAuditLogsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpLocalizationOptions>(opts =>
        {
            opts.Resources.Add<SimphonisAuditLogsResource>("fr-FR")
                          .AddVirtualJson("/Localization");

            opts.DefaultResourceType = typeof(SimphonisAuditLogsResource);
        });
    }
}
