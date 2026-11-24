using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace Simphonis.Abp.AuditLogs;

/// <summary>
/// Tests of the batching logic of <see cref="AuditLogCleaner"/>, against an in-memory list of audit logs.
/// Multi-tenancy is disabled: the host is the only listed tenant, and the rows of the other tenants are cleaned as unlisted tenants.
/// </summary>
public sealed class AuditLogCleanerTests : AuditLogsApplicationTestBase
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly DateTime RetentionLimit = Now - MaxAge;

    /// <summary>
    /// Content of the fake audit log table
    /// </summary>
    private readonly List<AuditLog> _rows = [];

    /// <summary>
    /// Units of work started by the cleaner, in order
    /// </summary>
    private readonly List<IUnitOfWork> _unitsOfWork = [];

    /// <summary>
    /// For each delete statement, the unit of work that was started last when it ran
    /// </summary>
    private readonly List<IUnitOfWork> _deleteUnitsOfWork = [];

    private AuditLogCleaner CreateCleaner(int deleteBatchSize)
    {
        IAuditLogRepository repository = Substitute.For<IAuditLogRepository>();
        repository.GetQueryableAsync().Returns(_ => Task.FromResult(_rows.AsQueryable()));
        repository.When(r => r.DeleteDirectAsync(Arg.Any<Expression<Func<AuditLog, bool>>>(), Arg.Any<CancellationToken>()))
                  .Do(call =>
                  {
                      Func<AuditLog, bool> predicate = call.Arg<Expression<Func<AuditLog, bool>>>().Compile();
                      _deleteUnitsOfWork.Add(_unitsOfWork[^1]);
                      _rows.RemoveAll(r => predicate(r));
                  });

        IAsyncQueryableExecuter asyncExecuter = Substitute.For<IAsyncQueryableExecuter>();
        asyncExecuter.FirstOrDefaultAsync(Arg.Any<IQueryable<DateTime>>(), Arg.Any<CancellationToken>())
                     .Returns(call => Task.FromResult(call.Arg<IQueryable<DateTime>>().FirstOrDefault()));
        asyncExecuter.FirstOrDefaultAsync(Arg.Any<IQueryable<Guid?>>(), Arg.Any<CancellationToken>())
                     .Returns(call => Task.FromResult(call.Arg<IQueryable<Guid?>>().FirstOrDefault()));

        IUnitOfWorkManager unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        unitOfWorkManager.Begin(Arg.Any<AbpUnitOfWorkOptions>(), Arg.Any<bool>())
                         .Returns(_ =>
                         {
                             IUnitOfWork uow = Substitute.For<IUnitOfWork>();
                             _unitsOfWork.Add(uow);
                             return uow;
                         });

        IClock clock = Substitute.For<IClock>();
        clock.Now.Returns(Now);

        return new AuditLogCleaner(
            repository,
            clock,
            asyncExecuter,
            Substitute.For<IDataFilter>(),
            Substitute.For<ICurrentTenant>(),
            unitOfWorkManager,
            Options.Create(new AbpMultiTenancyOptions { IsEnabled = false }),
            Substitute.For<IAbpLazyServiceProvider>(),
            Options.Create(new AuditLogCleanerOptions { DeleteBatchSize = deleteBatchSize }));
    }

    private void AddRow(DateTime executionTime, Guid? tenantId = null) => _rows.Add(new AuditLog(
        Guid.NewGuid(),
        "UnitTest",
        tenantId,
        null,
        null,
        null,
        executionTime,
        23,
        "1.2.3.4",
        "TestClient",
        "TestClientId",
        "asl;kfjasdl;kfjasdkl;fj",
        "UnitTests",
        "GET",
        "https://www.example.org/unit-tests",
        200,
        null,
        null,
        null,
        null,
        new ExtraPropertyDictionary(),
        null,
        null,
        null,
        "Test entry"));

    /// <summary>
    /// Adds <paramref name="count"/> outdated host rows, one minute apart, and returns their execution times, oldest first
    /// </summary>
    private List<DateTime> AddOutdatedRows(int count)
    {
        List<DateTime> result = [];
        for (int i = 0; i < count; i++)
        {
            DateTime executionTime = Now.AddDays(-40).AddMinutes(i);
            AddRow(executionTime);
            result.Add(executionTime);
        }
        return result;
    }

    private List<DateTime> GetRemainingExecutionTimes() => [.. _rows.Select(r => r.ExecutionTime).Order()];

    [Fact]
    public async Task Should_Delete_All_Outdated_Rows_In_Batches()
    {
        // Arrange
        AddOutdatedRows(25);
        AddRow(Now.AddDays(-10));                       // Recent: kept
        AddRow(RetentionLimit);                         // Exactly at the retention limit: kept
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken);

        // Assert
        GetRemainingExecutionTimes().ShouldBe([RetentionLimit, Now.AddDays(-10)]);
        // 10 + 10 + 5 rows
        _deleteUnitsOfWork.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Should_Delete_Outdated_Rows_Of_Unlisted_Tenants()
    {
        // Arrange
        Guid tenant1 = Guid.NewGuid();
        Guid tenant2 = Guid.NewGuid();
        AddRow(Now.AddDays(-50), tenant1);              // Outdated: deleted
        AddRow(Now.AddDays(-45), tenant1);              // Outdated: deleted
        AddRow(Now.AddDays(-10), tenant1);              // Recent: kept
        AddRow(Now.AddDays(-5), tenant2);               // Recent, and the tenant's only row: kept
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken);

        // Assert
        GetRemainingExecutionTimes().ShouldBe([Now.AddDays(-10), Now.AddDays(-5)]);
        // Only tenant 1 has outdated rows
        _deleteUnitsOfWork.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Delete_No_More_Than_Max_Row_Count_Per_Unlisted_Tenant()
    {
        // Arrange
        Guid tenant1 = Guid.NewGuid();
        Guid tenant2 = Guid.NewGuid();
        for (int i = 0; i < 3; i++)
        {
            AddRow(Now.AddDays(-50).AddMinutes(i), tenant1);
            AddRow(Now.AddDays(-40).AddMinutes(i), tenant2);
        }
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 2, TestContext.Current.CancellationToken);

        // Assert
        // The most recent outdated row of each tenant is kept
        GetRemainingExecutionTimes().ShouldBe([Now.AddDays(-50).AddMinutes(2), Now.AddDays(-40).AddMinutes(2)]);
    }

    [Fact]
    public async Task Should_Run_Each_Batch_In_Its_Own_Unit_Of_Work()
    {
        // Arrange
        AddOutdatedRows(25);
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken);

        // Assert
        _deleteUnitsOfWork.Count.ShouldBe(3);
        _deleteUnitsOfWork.Distinct().Count().ShouldBe(3);
        foreach (IUnitOfWork uow in _deleteUnitsOfWork)
        {
            await uow.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
            uow.Received(1).Dispose();
        }
    }

    [Theory]
    // Test case 1: The limit is a multiple of the batch size
    [InlineData(20, 2)]
    // Test case 2: The last batch is smaller than the batch size
    [InlineData(15, 2)]
    // Test case 3: The limit is smaller than the batch size
    [InlineData(7, 1)]
    public async Task Should_Delete_No_More_Than_Max_Row_Count(int maxRowCount, int expectedDeleteCount)
    {
        // Arrange
        List<DateTime> outdatedRows = AddOutdatedRows(25);
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, maxRowCount, TestContext.Current.CancellationToken);

        // Assert
        // The oldest rows are deleted first
        GetRemainingExecutionTimes().ShouldBe(outdatedRows.Skip(maxRowCount));
        _deleteUnitsOfWork.Count.ShouldBe(expectedDeleteCount);
    }

    [Fact]
    public async Task Should_Not_Delete_When_No_Row_Is_Outdated()
    {
        // Arrange
        AddRow(RetentionLimit);
        AddRow(Now.AddDays(-10));
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken);

        // Assert
        _rows.Count.ShouldBe(2);
        _deleteUnitsOfWork.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Not_Delete_When_Table_Is_Empty()
    {
        // Arrange
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken);

        // Assert
        _deleteUnitsOfWork.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Delete_Rows_Sharing_An_Execution_Time_Beyond_The_Batch_Size()
    {
        // Arrange
        // 15 rows share the same execution time, between two groups of 5 rows
        DateTime sharedTime = Now.AddDays(-40);
        for (int i = 0; i < 5; i++)
        {
            AddRow(sharedTime.AddMinutes(-10 - i));
        }
        for (int i = 0; i < 15; i++)
        {
            AddRow(sharedTime);
        }
        for (int i = 0; i < 5; i++)
        {
            AddRow(sharedTime.AddMinutes(10 + i));
        }
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken);

        // Assert
        _rows.ShouldBeEmpty();
        // The 5 oldest rows, then the 15 rows sharing their execution time, then the 5 newest rows
        _deleteUnitsOfWork.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Should_Not_Get_Stuck_On_Rows_Sharing_The_Oldest_Execution_Time()
    {
        // Arrange
        // More rows than the limit share the oldest execution time: they can only be deleted together
        for (int i = 0; i < 15; i++)
        {
            AddRow(Now.AddDays(-40));
        }
        AddRow(Now.AddDays(-39));
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 10, TestContext.Current.CancellationToken);

        // Assert
        GetRemainingExecutionTimes().ShouldBe([Now.AddDays(-39)]);
    }

    [Theory]
    // Test case 1: Batch size is zero
    [InlineData(0)]
    // Test case 2: Batch size is negative
    [InlineData(-1)]
    // Test case 3: Batch size exceeds the maximum row count
    [InlineData(SimphonisAuditLogConsts.MaximumPossibleRowCount + 1)]
    public void Should_Reject_Invalid_Delete_Batch_Size(int deleteBatchSize)
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => CreateCleaner(deleteBatchSize));
    }

    [Fact]
    public void Should_Use_Default_Delete_Batch_Size_When_Not_Configured()
    {
        new AuditLogCleanerOptions().DeleteBatchSize.ShouldBe(SimphonisAuditLogConsts.DefaultDeleteBatchSize);
    }
}
