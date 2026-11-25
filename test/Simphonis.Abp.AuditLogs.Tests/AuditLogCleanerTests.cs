using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
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
/// Tests of the batching logic and of the per-tenant error handling of <see cref="AuditLogCleaner"/>, against an in-memory list of audit logs.
/// Unless the test passes tenants to <see cref="CreateCleaner"/>, multi-tenancy is disabled: only the host rows are cleaned.
/// </summary>
public sealed class AuditLogCleanerTests : AuditLogsApplicationTestBase
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly DateTime RetentionLimit = Now - MaxAge;
    private static readonly Guid Tenant1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Tenant2 = new("22222222-2222-2222-2222-222222222222");

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

    private readonly ILogger<AuditLogCleaner> _logger = Substitute.For<ILogger<AuditLogCleaner>>();

    /// <summary>
    /// Tenant selected by the cleaner through <see cref="ICurrentTenant.Change"/>, <see langword="null"/> for the host
    /// </summary>
    private Guid? _currentTenantId;

    /// <summary>
    /// Returns the exception the repository must fail with while the given tenant is cleaned, or <see langword="null"/> to succeed
    /// </summary>
    private Func<Guid?, Exception?> _tenantFailure = _ => null;

    /// <param name="deleteBatchSize">Value of <see cref="AuditLogCleanerOptions.DeleteBatchSize"/></param>
    /// <param name="tenantIds">When set, multi-tenancy is enabled and the tenant store lists these tenants</param>
    private AuditLogCleaner CreateCleaner(int deleteBatchSize, IReadOnlyList<Guid>? tenantIds = null)
    {
        IAuditLogRepository repository = Substitute.For<IAuditLogRepository>();
        repository.GetQueryableAsync().Returns(_ =>
        {
            Exception? failure = _tenantFailure(_currentTenantId);
            if (failure != null)
            {
                return Task.FromException<IQueryable<AuditLog>>(failure);
            }

            return Task.FromResult(_rows.AsQueryable());
        });
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

        ICurrentTenant currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Change(Arg.Any<Guid?>(), Arg.Any<string?>())
                     .Returns(call =>
                     {
                         _currentTenantId = call.ArgAt<Guid?>(0);
                         return Substitute.For<IDisposable>();
                     });

        IAbpLazyServiceProvider lazyServiceProvider = Substitute.For<IAbpLazyServiceProvider>();
        if (tenantIds != null)
        {
            IReadOnlyList<TenantConfiguration> tenants = [.. tenantIds.Select(id => new TenantConfiguration(id, id.ToString()))];
            ITenantStore tenantStore = Substitute.For<ITenantStore>();
            tenantStore.GetListAsync(Arg.Any<bool>()).Returns(tenants);
            lazyServiceProvider.LazyGetRequiredService<ITenantStore>().Returns(tenantStore);
        }

        return new AuditLogCleaner(
            repository,
            clock,
            asyncExecuter,
            Substitute.For<IDataFilter>(),
            currentTenant,
            unitOfWorkManager,
            Options.Create(new AbpMultiTenancyOptions { IsEnabled = tenantIds != null }),
            lazyServiceProvider,
            Options.Create(new AuditLogCleanerOptions { DeleteBatchSize = deleteBatchSize }),
            _logger);
    }

    /// <summary>
    /// Returns the exceptions passed to the logger at the <see cref="LogLevel.Error"/> level, in order
    /// </summary>
    private List<Exception?> GetLoggedErrors() => [.. _logger.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log) && c.GetArguments()[0] is LogLevel.Error)
        .Select(c => c.GetArguments()[3] as Exception)];

    private int CountRows(Guid? tenantId) => _rows.Count(r => r.TenantId == tenantId);

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
    /// Adds <paramref name="count"/> outdated rows of a tenant (the host by default), one minute apart, and returns their execution times, oldest first
    /// </summary>
    private List<DateTime> AddOutdatedRows(int count, Guid? tenantId = null)
    {
        List<DateTime> result = [];
        for (int i = 0; i < count; i++)
        {
            DateTime executionTime = Now.AddDays(-40).AddMinutes(i);
            AddRow(executionTime, tenantId);
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
        AddRow(Now.AddDays(-50), Guid.NewGuid());       // Outdated, but belongs to a tenant: kept
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10);

        // Act
        await cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken);

        // Assert
        GetRemainingExecutionTimes().ShouldBe([Now.AddDays(-50), RetentionLimit, Now.AddDays(-10)]);
        // 10 + 10 + 5 rows
        _deleteUnitsOfWork.Count.ShouldBe(3);
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

    [Fact]
    public async Task Should_Clean_Other_Tenants_When_A_Tenant_Fails()
    {
        // Arrange
        AddOutdatedRows(5);
        AddOutdatedRows(5, Tenant1);
        AddOutdatedRows(5, Tenant2);
        InvalidOperationException failure = new("Tenant database unavailable");
        _tenantFailure = tenantId => tenantId == Tenant1 ? failure : null;
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10, [Tenant1, Tenant2]);

        // Act
        AggregateException exception = await Should.ThrowAsync<AggregateException>(() => cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken));

        // Assert
        exception.InnerExceptions.ShouldBe([failure]);
        GetLoggedErrors().ShouldBe([failure]);
        // The host, cleaned before the failing tenant, and the tenant cleaned after it
        CountRows(null).ShouldBe(0);
        CountRows(Tenant2).ShouldBe(0);
        CountRows(Tenant1).ShouldBe(5);
    }

    [Fact]
    public async Task Should_Report_Every_Tenant_Failure()
    {
        // Arrange
        AddOutdatedRows(5);
        AddOutdatedRows(5, Tenant1);
        AddOutdatedRows(5, Tenant2);
        InvalidOperationException hostFailure = new("Host database unavailable");
        InvalidOperationException tenant2Failure = new("Tenant database unavailable");
        _tenantFailure = tenantId => tenantId == null ? hostFailure : tenantId == Tenant2 ? tenant2Failure : null;
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10, [Tenant1, Tenant2]);

        // Act
        AggregateException exception = await Should.ThrowAsync<AggregateException>(() => cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken));

        // Assert
        exception.InnerExceptions.ShouldBe([hostFailure, tenant2Failure]);
        GetLoggedErrors().ShouldBe([hostFailure, tenant2Failure]);
        CountRows(Tenant1).ShouldBe(0);
    }

    [Fact]
    public async Task Should_Stop_When_Cancelled_During_A_Tenant_Cleanup()
    {
        // Arrange
        AddOutdatedRows(5);
        AddOutdatedRows(5, Tenant1);
        AddOutdatedRows(5, Tenant2);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _tenantFailure = tenantId =>
        {
            if (tenantId != Tenant1)
            {
                return null;
            }

            // The caller cancels the cleanup while the tenant is being cleaned
            cancellation.Cancel();
            return new OperationCanceledException(cancellation.Token);
        };
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10, [Tenant1, Tenant2]);

        // Act & Assert
        // The cancellation is propagated as is, not reported as a tenant failure
        await Should.ThrowAsync<OperationCanceledException>(() => cleaner.CleanAsync(MaxAge, 10_000, cancellation.Token));
        GetLoggedErrors().ShouldBeEmpty();
        CountRows(null).ShouldBe(0);
        // The tenants after the cancelled one are not cleaned
        CountRows(Tenant2).ShouldBe(5);
    }

    [Fact]
    public async Task Should_Treat_A_Cancellation_Not_Requested_By_The_Caller_As_A_Tenant_Failure()
    {
        // Arrange
        AddOutdatedRows(5, Tenant1);
        AddOutdatedRows(5, Tenant2);
        // For instance a timeout inside the database provider, while the caller's token is not cancelled
        TaskCanceledException failure = new("Command timeout");
        _tenantFailure = tenantId => tenantId == Tenant1 ? failure : null;
        AuditLogCleaner cleaner = CreateCleaner(deleteBatchSize: 10, [Tenant1, Tenant2]);

        // Act
        AggregateException exception = await Should.ThrowAsync<AggregateException>(() => cleaner.CleanAsync(MaxAge, 10_000, TestContext.Current.CancellationToken));

        // Assert
        exception.InnerExceptions.ShouldBe([failure]);
        GetLoggedErrors().ShouldBe([failure]);
        CountRows(Tenant2).ShouldBe(0);
    }
}
