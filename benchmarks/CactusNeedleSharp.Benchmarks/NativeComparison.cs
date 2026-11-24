using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CactusNeedleSharp;

internal static class NativeComparison
{
    private const int Repetitions = 5;

    private static readonly NeedleTool[] Tools =
    [
        NeedleTool.FromJson("""
            {"name":"get_weather","description":"Get current weather for a city","parameters":{"type":"object",
             "properties":{"city":{"type":"string"}},"required":["city"],"additionalProperties":false}}
            """),
        NeedleTool.FromJson("""
            {"name":"set_thermostat","description":"Set target temperature for a room in Celsius","parameters":{"type":"object",
             "properties":{"room":{"type":"string"},"temperature":{"type":"integer","minimum":10,"maximum":35}},
             "required":["room","temperature"],"additionalProperties":false}}
            """)
    ];

    private static readonly Case[] Cases =
    [
        new("What is the weather in Paris?", "get_weather", "city", "Paris"),
        new("How is the weather in São Paulo?", "get_weather", "city", "São Paulo"),
        new("Set the living room thermostat to 21 degrees Celsius.", "set_thermostat", "room", "living room", 21),
        new("Set the kitchen thermostat to 24 degrees Celsius.", "set_thermostat", "room", "kitchen", 24),
        new("Tell me a joke."),
        new("Do not look up the weather in Paris."),
        new("Get the weather.")
    ];

    internal static async Task RunAsync(string runtimePath, string workerPath, string? modelPath)
    {
        var options = new NeedleOptions { NativeLibraryPath = runtimePath, ModelPath = modelPath, CacheDirectory = Environment.GetEnvironmentVariable("NEEDLE_TEST_CACHE_DIRECTORY"), Offline = true };
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var initialMemory = process.WorkingSet64;
        var clock = Stopwatch.StartNew();
        await using var client = await NeedleClient.CreateAsync(options);
        await using var session = await client.CreateSessionAsync(Tools);
        var cold = clock.Elapsed.TotalMilliseconds;
        await session.CompleteAsync(Cases[0].Prompt);

        var direct = await SampleAsync(session);
        await session.DisposeAsync();
        process.Refresh();
        var initializedMemory = process.WorkingSet64;
        var peakMemory = process.PeakWorkingSet64;

        await using var pool = new NeedleWorkerPool(new() { MaximumWorkers = 1, WorkerPath = workerPath, Runtime = options });
        clock.Restart();
        await pool.WarmAsync(1);
        var workerHandshake = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        await using var worker = await pool.CreateSessionAsync(Tools);
        var workerFirstSession = clock.Elapsed.TotalMilliseconds;
        await worker.CompleteAsync(Cases[0].Prompt);
        var isolated = await SampleAsync(worker);
        await worker.DisposeAsync();
        clock.Restart();
        await using var reused = await pool.CreateSessionAsync(Tools);
        var workerReuse = clock.Elapsed.TotalMilliseconds;

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Machine = new { RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture, CpuCount = Environment.ProcessorCount },
            Wrapper = client.RuntimeInfo.WrapperVersion,
            ColdMs = cold,
            WorkingSetMiB = initializedMemory / 1048576d,
            WorkingSetIncreaseMiB = (initializedMemory - initialMemory) / 1048576d,
            PeakWorkingSetMiB = peakMemory / 1048576d,
            WorkerHandshakeMs = workerHandshake,
            WorkerFirstSessionMs = workerFirstSession,
            WorkerReusedSessionMs = workerReuse,
            Direct = Summarize(direct),
            Isolated = Summarize(isolated),
            Cases = direct.GroupBy(item => item.Case.Prompt).Select(group => new
            {
                Prompt = group.Key,
                Results = group.Select(item => new { item.Names, item.Arguments, item.Outcome, item.Confidence })
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<List<Measurement>> SampleAsync(INeedleSession session)
    {
        var measurements = new List<Measurement>();
        for (var repeat = 0; repeat < Repetitions; repeat++)
        {
            foreach (var test in Cases)
            {
                await session.ResetAsync();
                var started = Stopwatch.GetTimestamp();
                var result = await session.CompleteAsync(test.Prompt);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var call = result.Calls.Count == 1 ? result.Calls[0] : null;
                var selection = call is not null && call.Name == test.Tool;
                var arguments = selection && call!.Arguments.TryGetProperty(test.Key!, out var value) &&
                    value.ValueKind == JsonValueKind.String && value.GetString() == test.Value &&
                    (test.Temperature is null || call.Arguments.TryGetProperty("temperature", out var temperature) &&
                     temperature.ValueKind == JsonValueKind.Number && temperature.TryGetInt32(out var number) && number == test.Temperature);
                measurements.Add(new(test, elapsed, result.PrefillTokensPerSecond, result.DecodeTokensPerSecond,
                    result.Confidence, selection, arguments, result.Calls.Count > 0,
                    result.GetOutcome().ToString(), result.Calls.Select(item => item.Name).ToArray(), call?.Arguments));
            }
        }
        return measurements;
    }

    private static object Summarize(List<Measurement> samples)
    {
        var positive = samples.Where(item => item.Case.Tool is not null).ToArray();
        var negative = samples.Where(item => item.Case.Tool is null).ToArray();
        var confidence = samples.Where(item => item.Confidence.HasValue).ToArray();
        var incorrectProposals = confidence.Where(item => item.Proposed && !item.CorrectArguments).ToArray();
        return new
        {
            Count = samples.Count,
            WarmMedianMs = Percentile(samples.Select(item => item.Milliseconds), .5),
            WarmP95Ms = Percentile(samples.Select(item => item.Milliseconds), .95),
            PrefillMedianTps = MedianOrNull(samples.Select(item => item.Prefill)),
            DecodeMedianTps = MedianOrNull(samples.Select(item => item.Decode)),
            ExpectedCalls = positive.Length,
            CorrectTool = positive.Count(item => item.CorrectTool),
            CorrectArguments = positive.Count(item => item.CorrectArguments),
            FalseRefusals = positive.Count(item => !item.Proposed),
            ExpectedNoCall = negative.Length,
            NoCallOrWithheld = negative.Count(item => !item.Proposed),
            FalsePositives = negative.Count(item => item.Proposed),
            AcceptedFalsePositives = negative.Count(item => item.Outcome == "Success"),
            ConfidenceAvailable = confidence.Length,
            MedianCorrectConfidence = MedianOrNull(confidence.Where(item => item.CorrectArguments).Select(item => item.Confidence)),
            MedianIncorrectProposalConfidence = MedianOrNull(incorrectProposals.Select(item => item.Confidence))
        };
    }

    private static double? MedianOrNull(IEnumerable<double?> values)
    {
        var available = values.OfType<double>().ToArray();
        return available.Length == 0 ? null : Percentile(available, .5);
    }

    private static double Percentile(IEnumerable<double> values, double fraction)
    {
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Ceiling(sorted.Length * fraction) - 1];
    }

    private sealed record Case(string Prompt, string? Tool = null, string? Key = null, string? Value = null, int? Temperature = null);
    private sealed record Measurement(Case Case, double Milliseconds, double? Prefill, double? Decode,
        double? Confidence, bool CorrectTool, bool CorrectArguments, bool Proposed, string Outcome, string[] Names, JsonElement? Arguments);
}
