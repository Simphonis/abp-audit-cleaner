
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
    // Dans tous les tests c'est cette date qui sera utilisée comme "Now" par IClock
    private static readonly DateTime TestNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

    /// <summary>
    /// Données de test manipulées par le repository
    /// </summary>
    private AuditLog[] _database = [];

    private IAuditLogRepository CreateMockRepository()
    {
        var result = Substitute.For<IAuditLogRepository>();

        result.DeleteDirectAsync(default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(callInfo =>
            {
                // Suppression de toutes les entrées dans data qui correspondent à la lambda fournie
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

        DateTime dateRecente = TestNow.AddDays(-25);
        _database = [
            CreateTestEntry(dateRecente), // Entrée assez jeune qui sera conservée
            CreateTestEntry(TestNow.AddDays(-35))           // Entrée plus ancienne qui sera supprimée
        ];

        var cleaner = new AuditLogCleaner(repository, clock, asyncExecuter);

        // Act
        await cleaner.CleanAsync(TimeSpan.FromDays(30), 10000, TestContext.Current.CancellationToken);

        // Assert
        // on ne doit plus avoir que l'entrée plus récente
        _database.ShouldHaveSingleItem().ExecutionTime.ShouldBe(dateRecente);
    }

    [Fact]
    public async Task Applies_Security_Threshold()
    {
        // Arrange
        // On prépare une suppression de 11 lignes, mais avec un nombre max de suppressions de 10 lignes
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
            // 👆 Données trop vieilles qu'on veut supprimer (mais y en a trop donc on gardera la plus récente)
            // 👇 Données à conserver
            CreateTestEntry(TestNow.AddDays(-30)),
        ];

        var cleaner = new AuditLogCleaner(repository, clock, asyncExecuter);

        // Act
        await cleaner.CleanAsync(TimeSpan.FromDays(30), 10, TestContext.Current.CancellationToken);

        // Assert
        // Il doit nous rester deux entrées : celle qui est assez récente, et celle qui date d'il y a 200 jours
        // qui ne pouvait pas être inclue dans les 10 lignes
        var remainingDates = _database.Select(al => al.ExecutionTime).ToArray();
        remainingDates.ShouldBe([TestNow.AddDays(-200), TestNow.AddDays(-30)]);
    }
}
