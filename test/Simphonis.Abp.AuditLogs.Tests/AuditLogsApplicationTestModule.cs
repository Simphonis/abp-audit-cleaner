using Volo.Abp.Modularity;

namespace Simphonis.Abp.AuditLogs;

[DependsOn(
    typeof(SimphoAuditLogsModule)
    )]
public class AuditLogsApplicationTestModule : AbpModule
{

}
