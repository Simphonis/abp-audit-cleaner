
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Volo.Abp.Settings;
using Xunit;

namespace Simphonis.Abp.AuditLogs;
public class AuditLogsCleanerWorkerTests : AuditLogsApplicationTestBase
{
    [Theory]
    // Test case 1: Setting is not set
    [InlineData(null, SimphonisAuditLogConsts.DefaultRetentionPeriod)]
    // Test case 2: Setting is set to a valid integer
    [InlineData("15", 15)]
    // Test case 3: Setting is set to an invalid integer
    [InlineData("pouet", SimphonisAuditLogConsts.DefaultRetentionPeriod)]
    // Test case 4: Setting is set to a negative integer
    [InlineData("-15", SimphonisAuditLogConsts.DefaultRetentionPeriod)]
    public async Task Should_Read_Retention_Period_Setting(string? input, int output)
    {
        // Arrange
        var settingProvider = Substitute.For<ISettingProvider>();
        var logger = Substitute.For<ILogger<AuditLogsCleanerWorker>>();

        settingProvider.GetOrNullAsync(SimphonisAuditLogSettings.AuditLogRetentionPeriod).Returns(input);
        int retentionPeriod = await AuditLogsCleanerWorker.ReadRetentionPeriod(settingProvider, logger);
        retentionPeriod.ShouldBe(output);
    }
}
