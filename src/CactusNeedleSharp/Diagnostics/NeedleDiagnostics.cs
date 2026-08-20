using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CactusNeedleSharp;

internal static class NeedleDiagnostics
{
    internal const string Name = "CactusNeedleSharp";
    internal static readonly ActivitySource Activities = new(Name);
    internal static readonly Meter Meter = new(Name);
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("needle.inference.duration", "ms");
    internal static readonly Histogram<double> Confidence = Meter.CreateHistogram<double>("needle.inference.confidence");
    internal static readonly Histogram<double> Prefill = Meter.CreateHistogram<double>("needle.inference.prefill_tps");
    internal static readonly Histogram<double> Decode = Meter.CreateHistogram<double>("needle.inference.decode_tps");
    internal static readonly Histogram<int> Calls = Meter.CreateHistogram<int>("needle.tool_calls.count");
}
