
using NSubstitute;
using Shouldly;
using System.Linq.Expressions;
using Volo.Abp.AuditLogging;
using Volo.Abp.Timing;
using Xunit;

namespace Simphonis.Abp.AuditLogs;
public class AuditLogCleanerTests : AuditLogsApplicationTestBase
{
    // Dans tous les tests c'est cette date qui sera utilisée comme "Now" par IClock
    private static readonly DateTime TestNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

    private static IAuditLogRepository CreateMockRepository() => Substitute.For<IAuditLogRepository>();

    private static IClock CreateMockClock()
    {
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(TestNow);
        return clock;
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
        var repository = CreateMockRepository();
        var clock = CreateMockClock();
        var cleaner = new AuditLogCleaner(repository, clock);

        DateTime dateVieille = TestNow.AddDays(-35);
        AuditLog[] data = [
            CreateTestEntry(TestNow.AddDays(-25)), // Entrée assez jeune qui sera conservée
            CreateTestEntry(dateVieille)           // Entrée plus ancienne qui sera supprimée
            ];
        repository.DeleteDirectAsync(default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(callInfo =>
            {
                // Suppression de toutes les entrées dans data qui ne correspondent pas à la lambda fournie
                var lambda = callInfo.ArgAt<Expression<Func<AuditLog, bool>>>(0);
                data = [.. data.Where(lambda.Compile())];

                return Task.CompletedTask;
            });

        // Act
        await cleaner.CleanAsync(TimeSpan.FromDays(30), TestContext.Current.CancellationToken);

        // Assert
        // on ne doit plus avoir que l'entrée vieille dans data
        // (c'est à dire celle qui est sélectionnées pour suppression)
        data.ShouldHaveSingleItem().ExecutionTime.ShouldBe(dateVieille);
    }
}
