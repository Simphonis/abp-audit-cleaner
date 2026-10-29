using Volo.Abp.Modularity;

namespace Simphonis.Abp.AuditLogs;

[DependsOn(
    typeof(SimphonisAuditLogsModule)
    )]
public class AuditLogsApplicationTestModule : AbpModule
{

}
