using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilder.Hub;
using QueryBuilder.Tests.Hub;
using Xunit;
using HubHostApp = global::QueryBuilder.Hub.HubHost;

namespace QueryBuilder.Tests.HubHostSuite;

public class MetadataDatabaseWaitTests
{
    private sealed class Paused : Exception;

    private sealed class WrongPassword : Exception;

    private sealed class FakeTime
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Waits { get; } = [];

        public Task Delay(TimeSpan span, CancellationToken _)
        {
            Waits.Add(span);
            Now += span;
            return Task.CompletedTask;
        }
    }

    private static Task<bool> Wait(Func<CancellationToken, Task> probe, FakeTime time, int seconds = 90) =>
        MetadataDatabaseWait.WaitAsync(probe, ex => ex is Paused, TimeSpan.FromSeconds(seconds), time.Delay,
            NullLogger.Instance, CancellationToken.None, () => time.Now);

    [Fact]
    public async Task A_resuming_database_is_retried_with_growing_pauses_until_it_answers()
    {
        var time = new FakeTime();
        var calls = 0;

        var ok = await Wait(_ => ++calls < 4 ? throw new Paused() : Task.CompletedTask, time);

        Assert.True(ok);
        Assert.Equal(4, calls);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)], time.Waits);
    }

    [Fact]
    public async Task Pauses_stop_growing_at_ten_seconds()
    {
        var time = new FakeTime();
        var calls = 0;

        await Wait(_ => ++calls < 7 ? throw new Paused() : Task.CompletedTask, time);

        Assert.Equal(TimeSpan.FromSeconds(10), time.Waits.Max());
        Assert.Equal(TimeSpan.FromSeconds(10), time.Waits[^1]);
    }

    [Fact]
    public async Task A_non_transient_error_is_not_waited_on()
    {
        var time = new FakeTime();
        var calls = 0;

        var ok = await Wait(_ => { calls++; throw new WrongPassword(); }, time);

        Assert.False(ok);
        Assert.Equal(1, calls);
        Assert.Empty(time.Waits);
    }

    [Fact]
    public async Task The_wait_gives_up_at_its_budget_and_never_sleeps_past_it()
    {
        var time = new FakeTime();
        var start = time.Now;

        var ok = await Wait(_ => throw new Paused(), time, seconds: 30);

        Assert.False(ok);
        Assert.Equal(TimeSpan.FromSeconds(30), time.Now - start);
    }

    [Fact]
    public async Task A_zero_budget_never_probes()
    {
        var calls = 0;

        var ok = await Wait(_ => { calls++; return Task.CompletedTask; }, new FakeTime(), seconds: 0);

        Assert.False(ok);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Ordinary_exceptions_are_not_treated_as_a_resuming_database() =>
        Assert.False(MetadataDatabaseWait.IsTransient(new InvalidOperationException("Login failed")));

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Hub:ConnectionString"] = "Server=unused",
        ["Hub:Audience"] = "https://hub.test",
        ["Hub:SignIn:Codes:Enabled"] = "true",
        ["Hub:SuperAdmins:CodeNames:0"] = "ops",
        ["Hub:DataSourceKey"] = HubApiHost.Key,
    };

    private static WebApplication Build(Dictionary<string, string?> settings) =>
        HubHostApp.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings);
        });

    [Fact]
    public void The_wait_is_the_first_hosted_service_so_it_runs_before_migrations()
    {
        using var app = Build(Settings());

        Assert.IsType<MetadataDatabaseWait>(app.Services.GetServices<IHostedService>().First());
    }

    [Fact]
    public void Zero_turns_the_wait_off()
    {
        var settings = Settings();
        settings["Hub:StartupDatabaseWaitSeconds"] = "0";
        using var app = Build(settings);

        Assert.DoesNotContain(app.Services.GetServices<IHostedService>(), s => s is MetadataDatabaseWait);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("111")]
    public void A_wait_outside_0_to_110_seconds_is_refused(string seconds)
    {
        var settings = Settings();
        settings["Hub:StartupDatabaseWaitSeconds"] = seconds;

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Hub:StartupDatabaseWaitSeconds", ex.Message);
    }
}
