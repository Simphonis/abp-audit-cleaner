using System.Globalization;
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
                SimphonisAuditLogConsts.DefaultRetentionPeriod.ToString(NumberFormatInfo.InvariantInfo),
                new LocalizableString(typeof(SimphonisAuditLogsResource), "Setting__AuditLogRetentionPeriod__DisplayName"),
                new LocalizableString(typeof(SimphonisAuditLogsResource), "Setting__AuditLogRetentionPeriod__Description")
            )
        );

        context.Add(new SettingDefinition(
                SimphonisAuditLogSettings.MaxDeleteCount,
                SimphonisAuditLogConsts.DefaultMaximumRowCount.ToString(NumberFormatInfo.InvariantInfo),
                new LocalizableString(typeof(SimphonisAuditLogsResource), "Setting__DefaultMaximumRowCount__DisplayName"),
                new LocalizableString(typeof(SimphonisAuditLogsResource), "Setting__DefaultMaximumRowCount__Description")
            )
        );
    }
}
