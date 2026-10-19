
using System.Linq.Expressions;
using NSubstitute;
using Shouldly;
using Volo.Abp.AuditLogging;
using Volo.Abp.Linq;
using Volo.Abp.Timing;
using Xunit;

namespace Simphonis.Abp.AuditLogs;

public class AuditLogCleanerTests : AuditLogsApplicationTestBase
{
    // In all tests, this is the date that will be used as "Now" by IClock
    private static readonly DateTime TestNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

    /// <summary>
    /// Test data manipulated by the repository
    /// </summary>
    private AuditLog[] _database = [];

    private IAuditLogRepository CreateMockRepository()
    {
        var result = Substitute.For<IAuditLogRepository>();

        result.DeleteDirectAsync(default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(callInfo =>
            {
                // Deletion of all entries in data that match the provided lambda
                var lambda = callInfo.ArgAt<Expression<Func<AuditLog, bool>>>(0);
                _database = _database.Except(_database.Where(lambda.Compile())).ToArray();

                return Task.CompletedTask;
            });

        result.GetQueryableAsync().Returns((_) => Task.FromResult(_database.AsQueryable()));

        return result;
    }

    private static IClock CreateMockClock()
    {
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(TestNow);
        return clock;
    }

    private static IAsyncQueryableExecuter CreateMockAsyncExecuter()
    {
        var asyncExecuter = Substitute.For<IAsyncQueryableExecuter>();
        asyncExecuter.FirstOrDefaultAsync<DateTime>(default).ReturnsForAnyArgs(callInfo => callInfo.Arg<IQueryable<DateTime>>().FirstOrDefault());

        return asyncExecuter;
    }

    private static AuditLog CreateTestEntry(DateTime executionTime) => new(
        Guid.NewGuid(),
        "UnitTest",
        null,
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
        null,
        null,
        null,
        null,
        "Test entry");

    [Fact]
    public async Task Should_Clean_AuditLogs()
    {
        // Arrange
        var clock = CreateMockClock();
        var asyncExecuter = CreateMockAsyncExecuter();
        var repository = CreateMockRepository();

        DateTime recentDate = TestNow.AddDays(-25);
        _database = [
            CreateTestEntry(recentDate), // Recent enough entry that will be kept
            CreateTestEntry(TestNow.AddDays(-35))           // Older entry that will be deleted
        ];

        var cleaner = new AuditLogCleaner(repository, clock, asyncExecuter);

        // Act
        await cleaner.CleanAsync(TimeSpan.FromDays(30), 10000, TestContext.Current.CancellationToken);

        // Assert
        // we should only have the more recent entry left
        _database.ShouldHaveSingleItem().ExecutionTime.ShouldBe(recentDate);
    }

    [Fact]
    public async Task Applies_Security_Threshold()
    {
        // Arrange
        // We prepare a deletion of 11 rows, but with a max number of deletions of 10 rows
        var clock = CreateMockClock();
        var asyncExecuter = CreateMockAsyncExecuter();
        var repository = CreateMockRepository();

        _database = [
            CreateTestEntry(TestNow.AddDays(-300)),
            CreateTestEntry(TestNow.AddDays(-290)),
            CreateTestEntry(TestNow.AddDays(-280)),
            CreateTestEntry(TestNow.AddDays(-270)),
            CreateTestEntry(TestNow.AddDays(-260)),
            CreateTestEntry(TestNow.AddDays(-250)),
            CreateTestEntry(TestNow.AddDays(-240)),
            CreateTestEntry(TestNow.AddDays(-230)),
            CreateTestEntry(TestNow.AddDays(-220)),
            CreateTestEntry(TestNow.AddDays(-210)),
            CreateTestEntry(TestNow.AddDays(-200)),
            // 👆 Data too old that we want to delete (but there's too much of it so we'll keep the most recent)
            // 👇 Data to keep
            CreateTestEntry(TestNow.AddDays(-30)),
        ];

        var cleaner = new AuditLogCleaner(repository, clock, asyncExecuter);

        // Act
        await cleaner.CleanAsync(TimeSpan.FromDays(30), 10, TestContext.Current.CancellationToken);

        // Assert
        // We should have two entries left: the one that is recent enough, and the one from 200 days ago
        // which could not be included in the 10 rows
        var remainingDates = _database.Select(al => al.ExecutionTime).ToArray();
        remainingDates.ShouldBe([TestNow.AddDays(-200), TestNow.AddDays(-30)]);
    }
}
