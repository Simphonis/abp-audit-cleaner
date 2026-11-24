using Simphonis.Abp.AuditLogs.Localization;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Simphonis.Abp.AuditLogs;

[DependsOn(typeof(AbpBackgroundWorkersModule),
           typeof(AbpAuditLoggingDomainModule))]
public class SimphonisAuditLogsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<SimphonisAuditLogsModule>();
        });

        Configure<AbpLocalizationOptions>(opts =>
        {
            opts.Resources.Add<SimphonisAuditLogsResource>("en")
                          .AddVirtualJson("/Localization");
        });
    }
}
