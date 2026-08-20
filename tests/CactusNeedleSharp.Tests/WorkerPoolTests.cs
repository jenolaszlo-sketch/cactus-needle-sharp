using CactusNeedleSharp.TestWorker;

namespace CactusNeedleSharp.Tests;

public sealed class WorkerPoolTests
{
    private static readonly NeedleTool Tool = NeedleTool.FromJson("""{"name":"test","parameters":{"type":"object","properties":{}}}""");

    [Fact]
    public async Task ReusesHealthyWorkerAndExposesCounts()
    {
        await using var pool = CreatePool();
        await using (var first = await pool.CreateSessionAsync([Tool]))
            Assert.True((await first.CompleteAsync("ok")).Success);

        Assert.Equal(1, pool.WorkerCount);
        Assert.Equal(1, pool.IdleWorkerCount);
        await using var second = await pool.CreateSessionAsync([Tool]);
        Assert.Equal(1, pool.WorkerCount);
        Assert.Equal(0, pool.IdleWorkerCount);
    }

    [Fact]
    public async Task CanPrewarmBoundedWorkers()
    {
        await using var pool = CreatePool(new() { MaximumWorkers = 2 });
        await pool.WarmAsync(2);
        Assert.Equal(2, pool.WorkerCount);
        Assert.Equal(2, pool.IdleWorkerCount);
    }

    [Fact]
    public async Task QueueTimeoutAppliesBackpressure()
    {
        await using var pool = CreatePool(new() { MaximumWorkers = 1, QueueTimeout = TimeSpan.FromMilliseconds(100) });
        await using var leased = await pool.CreateSessionAsync([Tool]);
        await Assert.ThrowsAsync<TimeoutException>(() => pool.CreateSessionAsync([Tool]).AsTask());
        Assert.Equal(0, pool.WaitingSessionCount);
    }

    [Fact]
    public async Task ZeroQueueStillAllowsImmediateLease()
    {
        await using var pool = CreatePool(new() { MaximumWorkers = 1, MaximumQueueLength = 0 });
        await using var leased = await pool.CreateSessionAsync([Tool]);
        await Assert.ThrowsAsync<NeedleWorkerException>(() => pool.CreateSessionAsync([Tool]).AsTask());
    }

    [Theory]
    [InlineData("crash")]
    [InlineData("wrong-correlation")]
    [InlineData("oversize")]
    public async Task ProtocolFailureDiscardsWorker(string input)
    {
        await using var pool = CreatePool(new() { MaximumProtocolMessageLength = 1024 });
        await using (var broken = await pool.CreateSessionAsync([Tool]))
            await Assert.ThrowsAsync<NeedleWorkerException>(() => broken.CompleteAsync(input).AsTask());

        await using var replacement = await pool.CreateSessionAsync([Tool]);
        Assert.True((await replacement.CompleteAsync("ok")).Success);
    }

    [Fact]
    public async Task DisposeWaitsForInFlightOperationAndIsIdempotent()
    {
        await using var pool = CreatePool(new() { RequestTimeout = TimeSpan.FromMilliseconds(200) });
        var session = await pool.CreateSessionAsync([Tool]);
        var completion = session.CompleteAsync("delay").AsTask();
        await Task.Delay(50);
        var firstDispose = session.DisposeAsync().AsTask();
        var secondDispose = session.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        await Task.WhenAll(firstDispose, secondDispose);
    }

    [Fact]
    public async Task PoolDisposalTerminatesInFlightWorkerWithoutRacingProtocol()
    {
        var pool = CreatePool(new() { ShutdownTimeout = TimeSpan.FromSeconds(1) });
        var session = await pool.CreateSessionAsync([Tool]);
        var completion = session.CompleteAsync("delay").AsTask();
        await Task.Delay(50);

        await pool.DisposeAsync();

        await Assert.ThrowsAsync<NeedleWorkerException>(() => completion);
        await session.DisposeAsync();
        await pool.DisposeAsync();
    }

    private static NeedleWorkerPool CreatePool(NeedleWorkerPoolOptions? overrides = null)
    {
        var defaults = overrides ?? new();
        return new(defaults with
        {
            WorkerPath = typeof(Marker).Assembly.Location,
            MaximumWorkers = defaults.MaximumWorkers,
            StartupTimeout = defaults.StartupTimeout,
            RequestTimeout = defaults.RequestTimeout,
            ShutdownTimeout = defaults.ShutdownTimeout
        });
    }
}
