using Simphonis.Abp.AuditLogs.Localization;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace Simphonis.Abp.AuditLogs;

public class SimphonisAuditLogSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinition(
                SimphonisAuditLogSettings.AuditLogRetentionPeriod,
                "365",
                new LocalizableString(typeof(SimphonisAuditLogsResource), "Setting:AuditLogRetentionPeriod:DisplayName"),
                new LocalizableString(typeof(SimphonisAuditLogsResource), "Setting:AuditLogRetentionPeriod:Description"),
                isVisibleToClients: true
            )
        );
    }
}
