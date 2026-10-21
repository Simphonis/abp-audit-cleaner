
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
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
    [InlineData("nan", SimphonisAuditLogConsts.DefaultRetentionPeriod)]
    // Test case 4: Setting is set to a negative integer
    [InlineData("-15", SimphonisAuditLogConsts.DefaultRetentionPeriod)]
    // Test case 5 : Setting is zero
    [InlineData("0", 0)]
    public async Task Should_Read_Retention_Period_Setting(string? input, int output)
    {
        // Arrange
        var settingProvider = Substitute.For<ISettingProvider>();
        var logger = Substitute.For<ILogger<AuditLogsCleanerWorker>>();

        settingProvider.GetOrNullAsync(SimphonisAuditLogSettings.AuditLogRetentionPeriod).Returns(input);
        int retentionPeriod = await AuditLogsCleanerWorker.ReadRetentionPeriod(settingProvider, logger);
        retentionPeriod.ShouldBe(output);
    }

    [Theory]
    // Test case 1: Setting is not set
    [InlineData(null, SimphonisAuditLogConsts.DefaultMaximumRowCount)]
    // Test case 2: Setting is set to a valid integer
    [InlineData("15", 15)]
    // Test case 3: Setting is set to an invalid integer
    [InlineData("nan", SimphonisAuditLogConsts.DefaultMaximumRowCount)]
    // Test case 4: Setting is set to a negative integer
    [InlineData("-15", SimphonisAuditLogConsts.DefaultMaximumRowCount)]
    // Test case 5 : Setting is zero
    [InlineData("0", SimphonisAuditLogConsts.DefaultMaximumRowCount)]
    public async Task Should_Read_Max_Delete_Count_Setting(string? input, int output)
    {
        // Arrange
        var settingProvider = Substitute.For<ISettingProvider>();
        var logger = Substitute.For<ILogger<AuditLogsCleanerWorker>>();

        settingProvider.GetOrNullAsync(SimphonisAuditLogSettings.MaxDeleteCount).Returns(input);
        int retentionPeriod = await AuditLogsCleanerWorker.ReadMaxRowCount(settingProvider, logger);
        retentionPeriod.ShouldBe(output);
    }

    [Fact]
    public void Should_Use_Default_Period_When_Not_Configured()
    {
        // Arrange
        var timer = new AbpAsyncTimer();
        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = Options.Create(new AuditLogsCleanerWorkerOptions());

        // Act
        _ = new AuditLogsCleanerWorker(timer, serviceScopeFactory, options);

        // Assert
        timer.Period.ShouldBe(10 * 60 * 1000);
    }

    [Theory]
    // Test case 1: Smallest possible period
    [InlineData(1)]
    // Test case 2: Typical period
    [InlineData(30 * 60 * 1000)]
    // Test case 3: Largest possible period
    [InlineData(int.MaxValue)]
    public void Should_Set_Timer_Period_From_Options(long periodInMilliseconds)
    {
        // Arrange
        var timer = new AbpAsyncTimer();
        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = Options.Create(new AuditLogsCleanerWorkerOptions
        {
            Period = TimeSpan.FromMilliseconds(periodInMilliseconds)
        });

        // Act
        _ = new AuditLogsCleanerWorker(timer, serviceScopeFactory, options);

        // Assert
        timer.Period.ShouldBe((int)periodInMilliseconds);
    }

    [Theory]
    // Test case 1: Period is zero
    [InlineData(0)]
    // Test case 2: Period is negative
    [InlineData(-1)]
    // Test case 3: Period exceeds what the timer supports
    [InlineData((long)int.MaxValue + 1)]
    public void Should_Reject_Invalid_Period(long periodInMilliseconds)
    {
        // Arrange
        var timer = new AbpAsyncTimer();
        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = Options.Create(new AuditLogsCleanerWorkerOptions
        {
            Period = TimeSpan.FromMilliseconds(periodInMilliseconds)
        });

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => new AuditLogsCleanerWorker(timer, serviceScopeFactory, options));
    }

    [Fact]
    public void Should_Reject_Sub_Millisecond_Period()
    {
        // Arrange
        var timer = new AbpAsyncTimer();
        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = Options.Create(new AuditLogsCleanerWorkerOptions
        {
            // Would be truncated to a 0 ms timer period
            Period = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond - 1)
        });

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => new AuditLogsCleanerWorker(timer, serviceScopeFactory, options));
    }
}
