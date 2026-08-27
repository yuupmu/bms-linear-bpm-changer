using System.Collections.ObjectModel;

namespace BmsLinearBpmChanger.Core;

public enum ApproximationGranularity
{
    PerMeasure,
    PerBeat,
}

public enum AverageMethod
{
    TimeEquivalent,
    Arithmetic,
}

public sealed record SegmentInput(int StartMeasure, int EndMeasure, double StartBpm, double EndBpm);

public sealed record ConversionOptions(
    IReadOnlyList<SegmentInput> Segments,
    ApproximationGranularity Granularity = ApproximationGranularity.PerMeasure,
    AverageMethod AverageMethod = AverageMethod.TimeEquivalent,
    int DecimalPlaces = 2);

public readonly record struct PositionFraction
{
    public PositionFraction(int numerator, int denominator)
    {
        if (denominator == 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));

        var sign = denominator < 0 ? -1 : 1;
        var divisor = MathUtil.Gcd(numerator, denominator);
        Numerator = numerator * sign / divisor;
        Denominator = Math.Abs(denominator) / divisor;
    }

    public int Numerator { get; }
    public int Denominator { get; }
    public double Value => (double)Numerator / Denominator;
    public bool IsZero => Numerator == 0;

    public override string ToString() => $"{Numerator}/{Denominator}";
}

public sealed record ByteLine(byte[] Content, byte[] Newline);

public sealed record EncodingInfo(
    string DisplayName,
    int CodePage,
    bool HasUtf8Bom,
    string Confidence);

public sealed record ExistingBpmEvent(
    int Measure,
    PositionFraction Position,
    string Channel,
    string Token,
    double? Bpm,
    int LineNumber);

public sealed class BmsDocument
{
    public required byte[] OriginalBytes { get; init; }
    public required IReadOnlyList<ByteLine> Lines { get; init; }
    public required byte[] NewlineBytes { get; init; }
    public required string NewlineName { get; init; }
    public required EncodingInfo Encoding { get; init; }
    public required string FileName { get; init; }
    public required IReadOnlyDictionary<int, double> MeasureRatios { get; init; }
    public required IReadOnlyDictionary<string, double> BpmDefinitions { get; init; }
    public required IReadOnlyList<ExistingBpmEvent> ExistingBpmEvents { get; init; }
    public required IReadOnlyList<string> ParseWarnings { get; init; }
    public double? BaseBpm { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public int FirstMainLineIndex { get; init; } = -1;
    public int MaxMeasure { get; init; }

    public double MeasureRatio(int measure) => MeasureRatios.TryGetValue(measure, out var ratio) ? ratio : 1d;
}

public sealed record PreviewRow(
    int SegmentIndex,
    int Measure,
    int Slot,
    PositionFraction Position,
    double BpmStart,
    double BpmEnd,
    double UnroundedBpm,
    double OutputBpm,
    double ExactSeconds,
    double ApproximateSeconds,
    double IntervalErrorMilliseconds,
    double SegmentCumulativeMilliseconds,
    double GlobalCumulativeMilliseconds);

public sealed class GeneratedBpmEvent
{
    public required int Measure { get; init; }
    public required PositionFraction Position { get; init; }
    public required double Bpm { get; init; }
    public string Id { get; set; } = string.Empty;
    public string BpmText { get; set; } = string.Empty;
}

public sealed record GeneratedBpmDefinition(string Id, double Bpm, string Text);

public sealed record ConversionConflict(string Kind, string Message);

public sealed class PreparedConversion
{
    public required ConversionOptions Options { get; init; }
    public required IReadOnlyList<PreviewRow> Rows { get; init; }
    public required IReadOnlyList<GeneratedBpmEvent> Events { get; init; }
    public required IReadOnlyList<GeneratedBpmDefinition> Definitions { get; init; }
    public required IReadOnlyList<string> ChannelLines { get; init; }
    public required IReadOnlyList<ConversionConflict> Conflicts { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public double TotalExactSeconds { get; init; }
    public double TotalApproximateSeconds { get; init; }
    public double TotalErrorMilliseconds => (TotalApproximateSeconds - TotalExactSeconds) * 1000d;
    public bool CanConvert => Errors.Count == 0 && Conflicts.Count == 0 && Events.Count > 0;
}

internal static class MathUtil
{
    public static int Gcd(int a, int b)
    {
        var x = Math.Abs(a);
        var y = Math.Abs(b);
        while (y != 0)
            (x, y) = (y, x % y);
        return x == 0 ? 1 : x;
    }

    public static int Lcm(int a, int b) => Math.Abs(a / Gcd(a, b) * b);
}
