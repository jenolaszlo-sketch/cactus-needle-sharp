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

    [Fact]
    public async Task ReturnedWorkerWakesQueuedSession()
    {
        await using var pool = CreatePool(new() { MaximumWorkers = 2 });
        await using var first = await pool.CreateSessionAsync([Tool]);
        await using var second = await pool.CreateSessionAsync([Tool]);
        var pending = pool.CreateSessionAsync([Tool]).AsTask();
        await Task.Delay(200);
        Assert.False(pending.IsCompleted, "A third conversation must wait while both workers are leased.");
        await first.DisposeAsync();
        var completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(pending, completed);
        await using var third = await pending;
        Assert.Equal(2, pool.WorkerCount);
    }

    [Fact]
    public async Task WarmAndCreateNeverExceedMaximumWorkers()
    {
        await using var pool = CreatePool(new() { MaximumWorkers = 2 });
        var warm = pool.WarmAsync(2).AsTask();
        var first = await pool.CreateSessionAsync([Tool]);
        var second = await pool.CreateSessionAsync([Tool]);
        Assert.True(pool.WorkerCount <= 2, $"Worker count {pool.WorkerCount} exceeded the maximum.");
        await first.DisposeAsync();
        var completed = await Task.WhenAny(warm, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(warm, completed);
        await warm;
        Assert.True(pool.WorkerCount <= 2, $"Worker count {pool.WorkerCount} exceeded the maximum after warming.");
        await second.DisposeAsync();
    }

    [Fact]
    public async Task SessionFactoryOneShotCompilationWorksWithPool()
    {
        await using var pool = CreatePool();
        IToolCallCompiler compiler = pool;

        var result = await compiler.CompileAsync("ok", [Tool]);

        Assert.True(result.Success);
        Assert.Equal("test", Assert.Single(result.Calls).Name);
        Assert.Equal(1, pool.IdleWorkerCount);
    }

    [Fact]
    public async Task InvalidSessionTextIsRejectedBeforeStartingWorker()
    {
        await using var pool = CreatePool();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            pool.CreateSessionAsync([Tool], new() { SystemFacts = "bad\0facts" }).AsTask());

        Assert.Equal(0, pool.WorkerCount);
    }

    [Fact]
    public async Task InitializationFailureReleasesExactlyOneLease()
    {
        await using var pool = CreatePool(new() { MaximumWorkers = 1, MaximumProtocolMessageLength = 1024 });
        var facts = new NeedleSessionOptions { SystemFacts = new string('x', 4_000) };
        await Assert.ThrowsAsync<NeedleWorkerException>(() => pool.CreateSessionAsync([Tool], facts).AsTask());

        await using var replacement = await pool.CreateSessionAsync([Tool]);
        Assert.True((await replacement.CompleteAsync("ok")).Success);
    }

    [Fact]
    public async Task DisposalClosesAdmissionCreationRace()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pool = CreatePool(new()
        {
            AdmissionCheck = async (_, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
        });
        var creating = pool.CreateSessionAsync([Tool]).AsTask();
        await entered.Task;
        var disposing = pool.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => creating);
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, pool.WorkerCount);
    }

    [Fact]
    public async Task DisposalCancelsBlockedWarmup()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pool = CreatePool(new()
        {
            AdmissionCheck = async (_, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
        });
        var warming = pool.WarmAsync(1).AsTask();
        await entered.Task;

        var disposing = pool.DisposeAsync().AsTask();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => warming);
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, pool.WorkerCount);
    }

    [Fact]
    public async Task CancelledAdmissionReturnsPoolCapacity()
    {
        var attempt = 0;
        await using var pool = CreatePool(new()
        {
            MaximumWorkers = 1,
            AdmissionCheck = async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref attempt) == 1)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pool.CreateSessionAsync([Tool], cancellationToken: cancellation.Token).AsTask());

        await using var replacement = await pool.CreateSessionAsync([Tool]);
        Assert.True((await replacement.CompleteAsync("ok")).Success);
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
