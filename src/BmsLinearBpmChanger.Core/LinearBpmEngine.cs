using System.Globalization;

namespace BmsLinearBpmChanger.Core;

public static class LinearBpmEngine
{
    public const int MaximumExtendedBpmIds = 36 * 36 - 1;

    private const int MaximumChannelResolution = 15_360;
    private const int MaximumPositionDenominator = 7_680;

    public static PreparedConversion Prepare(BmsDocument document, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var errors = Validate(options);
        var conflicts = new List<ConversionConflict>();
        var warnings = new List<string>(document.ParseWarnings);
        var rows = new List<PreviewRow>();
        var generatedByPosition = new Dictionary<string, GeneratedBpmEvent>(StringComparer.Ordinal);
        var totalExactSeconds = 0d;
        var totalApproximateSeconds = 0d;
        var globalCumulativeMilliseconds = 0d;

        if (errors.Count == 0)
        {
            for (var segmentIndex = 0; segmentIndex < options.Segments.Count; segmentIndex++)
            {
                var segment = options.Segments[segmentIndex];
                foreach (var existing in document.ExistingBpmEvents.Where(existing => IsInside(existing, segment)))
                {
                    var position = existing.Position.IsZero
                        ? existing.Measure.ToString("000", CultureInfo.InvariantCulture)
                        : $"{existing.Measure:000} + {existing.Position}";
                    conflicts.Add(new ConversionConflict(
                        "existing",
                        $"{segmentIndex + 1}번 구간의 {position} 위치에 기존 BPM 이벤트(#{existing.Channel})가 있습니다. ({existing.LineNumber}행)"));
                }

                if (segment.StartMeasure == 0 && document.BaseBpm is double baseBpm && Math.Abs(baseBpm - segment.StartBpm) > 1e-9)
                    warnings.Add($"{segmentIndex + 1}번 구간은 000마디에서 시작하지만 헤더 BPM은 {baseBpm.ToString(CultureInfo.InvariantCulture)}입니다. 생성 이벤트가 000마디에서 이를 덮어씁니다.");

                try
                {
                    var intervalRows = BuildIntervals(document, segment, segmentIndex, options);
                    foreach (var interval in intervalRows)
                    {
                        totalExactSeconds += interval.ExactSeconds;
                        totalApproximateSeconds += interval.ApproximateSeconds;
                        globalCumulativeMilliseconds += interval.IntervalErrorMilliseconds;
                        var row = interval with { GlobalCumulativeMilliseconds = globalCumulativeMilliseconds };
                        rows.Add(row);

                        var key = PositionKey(row.Measure, row.Position);
                        var generated = new GeneratedBpmEvent { Measure = row.Measure, Position = row.Position, Bpm = row.OutputBpm };
                        AddGeneratedEvent(generatedByPosition, generated, key, conflicts);
                    }
                }
                catch (InvalidOperationException error)
                {
                    errors.Add(error.Message);
                }

                var nextSegment = options.Segments
                    .Select((candidate, candidateIndex) => (candidate, candidateIndex))
                    .FirstOrDefault(item => item.candidateIndex != segmentIndex && item.candidate.StartMeasure == segment.EndMeasure);
                if (nextSegment.candidate is null)
                {
                    var final = new GeneratedBpmEvent
                    {
                        Measure = segment.EndMeasure,
                        Position = new PositionFraction(0, 1),
                        Bpm = RoundBpm(segment.EndBpm, options.DecimalPlaces),
                    };
                    AddGeneratedEvent(generatedByPosition, final, PositionKey(final.Measure, final.Position), conflicts);
                }
                else if (Math.Abs(segment.EndBpm - nextSegment.candidate.StartBpm) > 1e-9)
                {
                    warnings.Add($"{segmentIndex + 1}번 구간 끝 BPM({segment.EndBpm.ToString(CultureInfo.InvariantCulture)})과 {nextSegment.candidateIndex + 1}번 구간 시작 BPM({nextSegment.candidate.StartBpm.ToString(CultureInfo.InvariantCulture)})이 달라 {segment.EndMeasure:000}마디에서 즉시 전환됩니다.");
                }
            }
        }

        var events = generatedByPosition.Values
            .OrderBy(item => item.Measure)
            .ThenBy(item => item.Position.Value)
            .ToList();

        var existingBpmIdCount = CollectUsedIds(document).Count(id => id != "00");
        var requiredNewBpmIdCount = CountRequiredNewDefinitions(document, events, options.DecimalPlaces);
        var definitions = AllocateDefinitions(document, events, options.DecimalPlaces, errors);
        var channelLines = BuildChannelLines(events, errors, conflicts);

        return new PreparedConversion
        {
            Options = options,
            Rows = rows.AsReadOnly(),
            Events = events.AsReadOnly(),
            Definitions = definitions.AsReadOnly(),
            ChannelLines = channelLines.AsReadOnly(),
            Conflicts = conflicts.DistinctBy(conflict => conflict.Message).ToList().AsReadOnly(),
            Warnings = warnings.Distinct().ToList().AsReadOnly(),
            Errors = errors.Distinct().ToList().AsReadOnly(),
            TotalExactSeconds = totalExactSeconds,
            TotalApproximateSeconds = totalApproximateSeconds,
            BpmIdCapacity = MaximumExtendedBpmIds,
            ExistingBpmIdCount = existingBpmIdCount,
            RequiredNewBpmIdCount = requiredNewBpmIdCount,
        };
    }

    public static double LogarithmicMean(double first, double second)
    {
        if (first <= 0 || second <= 0)
            throw new ArgumentOutOfRangeException(nameof(first), "BPM must be positive.");
        if (Math.Abs(first - second) < 1e-12)
            return first;
        return (second - first) / Math.Log(second / first);
    }

    public static double ExactIntervalSeconds(double quarterBeats, double bpmStart, double bpmEnd)
    {
        if (quarterBeats <= 0 || bpmStart <= 0 || bpmEnd <= 0)
            throw new ArgumentOutOfRangeException(nameof(quarterBeats));
        if (Math.Abs(bpmStart - bpmEnd) < 1e-12)
            return 60d * quarterBeats / bpmStart;
        return 60d * quarterBeats * Math.Log(bpmEnd / bpmStart) / (bpmEnd - bpmStart);
    }

    public static double RoundBpm(double bpm, int decimals) =>
        Math.Round(bpm, decimals, MidpointRounding.AwayFromZero);

    public static string FormatBpm(double bpm, int decimals) =>
        RoundBpm(bpm, decimals).ToString($"F{decimals}", CultureInfo.InvariantCulture);

    private static List<string> Validate(ConversionOptions options)
    {
        var errors = new List<string>();
        if (options.DecimalPlaces is < 0 or > 6)
            errors.Add("반올림 자리는 0~6 사이의 정수여야 합니다.");
        if (options.Segments.Count == 0)
            errors.Add("변속 구간을 하나 이상 입력하세요.");

        for (var index = 0; index < options.Segments.Count; index++)
        {
            var segment = options.Segments[index];
            var label = $"{index + 1}번 구간";
            if (segment.StartMeasure is < 0 or > 999 || segment.EndMeasure is < 0 or > 999)
                errors.Add($"{label}: 마디 번호는 000~999 범위여야 합니다.");
            if (segment.StartMeasure >= segment.EndMeasure)
                errors.Add($"{label}: 끝 마디는 시작 마디보다 커야 합니다.");
            if (!double.IsFinite(segment.StartBpm) || !double.IsFinite(segment.EndBpm) || segment.StartBpm <= 0 || segment.EndBpm <= 0)
                errors.Add($"{label}: BPM은 0보다 큰 숫자여야 합니다.");
            if (!Enum.IsDefined(segment.Subdivision))
                errors.Add($"{label}: 지원하지 않는 근사 단위입니다.");
        }

        var ordered = options.Segments.Select((segment, index) => (segment, index))
            .OrderBy(item => item.segment.StartMeasure)
            .ThenBy(item => item.segment.EndMeasure)
            .ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].segment.StartMeasure < ordered[index - 1].segment.EndMeasure)
                errors.Add($"{ordered[index - 1].index + 1}번과 {ordered[index].index + 1}번 구간이 겹칩니다.");
        }
        return errors;
    }

    private static List<PreviewRow> BuildIntervals(BmsDocument document, SegmentInput segment, int segmentIndex, ConversionOptions options)
    {
        var rows = new List<PreviewRow>();
        var segmentCumulativeMilliseconds = 0d;
        var measures = Enumerable.Range(segment.StartMeasure, segment.EndMeasure - segment.StartMeasure)
            .Select(measure => (Measure: measure, QuarterBeats: document.MeasureRatio(measure) * 4d))
            .ToList();

        foreach (var item in measures)
        {
            if (!double.IsFinite(item.QuarterBeats) || item.QuarterBeats <= 0)
                throw new InvalidOperationException($"{item.Measure:000}마디의 길이를 해석할 수 없습니다.");
        }

        var totalQuarterBeats = measures.Sum(item => item.QuarterBeats);
        if (!double.IsFinite(totalQuarterBeats) || totalQuarterBeats <= 0)
            throw new InvalidOperationException("변속 구간의 전체 길이를 계산할 수 없습니다.");

        var elapsedQuarterBeats = 0d;
        var subdivisionLength = segment.Subdivision switch
        {
            SubdivisionUnit.QuarterNote => 1d,
            SubdivisionUnit.SixteenthNote => 0.25d,
            _ => throw new InvalidOperationException("지원하지 않는 근사 단위입니다."),
        };

        foreach (var item in measures)
        {
            var measure = item.Measure;
            var quarterBeats = item.QuarterBeats;
            var boundaries = SubdivisionBoundaries(quarterBeats, subdivisionLength);

            for (var slot = 0; slot < boundaries.Count - 1; slot++)
            {
                var beatStart = boundaries[slot];
                var beatEnd = boundaries[slot + 1];
                var localStart = beatStart / quarterBeats;
                var progressStart = (elapsedQuarterBeats + beatStart) / totalQuarterBeats;
                var progressEnd = (elapsedQuarterBeats + beatEnd) / totalQuarterBeats;
                var bpmStart = segment.StartBpm + (segment.EndBpm - segment.StartBpm) * progressStart;
                var bpmEnd = segment.StartBpm + (segment.EndBpm - segment.StartBpm) * progressEnd;
                var unroundedBpm = options.AverageMethod == AverageMethod.Arithmetic
                    ? (bpmStart + bpmEnd) / 2d
                    : LogarithmicMean(bpmStart, bpmEnd);
                var outputBpm = RoundBpm(unroundedBpm, options.DecimalPlaces);
                var intervalQuarterBeats = beatEnd - beatStart;
                var exactSeconds = ExactIntervalSeconds(intervalQuarterBeats, bpmStart, bpmEnd);
                var approximateSeconds = 60d * intervalQuarterBeats / outputBpm;
                var errorMilliseconds = (approximateSeconds - exactSeconds) * 1000d;
                segmentCumulativeMilliseconds += errorMilliseconds;

                rows.Add(new PreviewRow(
                    segmentIndex,
                    measure,
                    slot,
                    FractionFromDouble(localStart, MaximumPositionDenominator),
                    bpmStart,
                    bpmEnd,
                    unroundedBpm,
                    outputBpm,
                    exactSeconds,
                    approximateSeconds,
                    errorMilliseconds,
                    segmentCumulativeMilliseconds,
                    0d));
            }

            elapsedQuarterBeats += quarterBeats;
        }
        return rows;
    }

    private static List<double> SubdivisionBoundaries(double quarterBeats, double subdivisionLength)
    {
        var intervalCount = Math.Ceiling(quarterBeats / subdivisionLength);
        if (intervalCount > MaximumChannelResolution)
            throw new InvalidOperationException($"한 마디의 근사 구간이 너무 많습니다({intervalCount.ToString(CultureInfo.InvariantCulture)}개).");

        var boundaries = new List<double> { 0d };
        for (var index = 1; index < intervalCount; index++)
        {
            var boundary = index * subdivisionLength;
            if (boundary < quarterBeats - 1e-10)
                boundaries.Add(boundary);
        }
        boundaries.Add(quarterBeats);
        return boundaries;
    }

    private static bool IsInside(ExistingBpmEvent existing, SegmentInput segment)
    {
        if (existing.Measure < segment.StartMeasure || existing.Measure > segment.EndMeasure)
            return false;
        return existing.Measure != segment.EndMeasure || existing.Position.IsZero;
    }

    private static PositionFraction FractionFromDouble(double value, int maximumDenominator)
    {
        if (Math.Abs(value) < 1e-12)
            return new PositionFraction(0, 1);
        if (Math.Abs(value - 1d) < 1e-12)
            return new PositionFraction(1, 1);

        var bestNumerator = (int)Math.Round(value);
        var bestDenominator = 1;
        var bestError = Math.Abs(value - bestNumerator);
        for (var denominator = 1; denominator <= maximumDenominator; denominator++)
        {
            var numerator = (int)Math.Round(value * denominator);
            var error = Math.Abs(value - (double)numerator / denominator);
            if (error >= bestError)
                continue;
            bestNumerator = numerator;
            bestDenominator = denominator;
            bestError = error;
            if (error < 1e-12)
                break;
        }
        return new PositionFraction(bestNumerator, bestDenominator);
    }

    private static string PositionKey(int measure, PositionFraction position) =>
        $"{measure}:{position.Numerator}/{position.Denominator}";

    private static void AddGeneratedEvent(
        IDictionary<string, GeneratedBpmEvent> target,
        GeneratedBpmEvent generated,
        string key,
        ICollection<ConversionConflict> conflicts)
    {
        if (target.TryGetValue(key, out var prior) && Math.Abs(prior.Bpm - generated.Bpm) > 1e-9)
        {
            conflicts.Add(new ConversionConflict("generated", $"{generated.Measure:000}마디 {generated.Position} 위치의 생성 BPM 값이 서로 충돌합니다."));
            return;
        }
        target[key] = generated;
    }

    private static List<GeneratedBpmDefinition> AllocateDefinitions(
        BmsDocument document,
        IEnumerable<GeneratedBpmEvent> events,
        int decimals,
        ICollection<string> errors)
    {
        var usedIds = CollectUsedIds(document);

        var bpmToId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, bpm) in document.BpmDefinitions.Where(item => item.Key != "00"))
            bpmToId[CanonicalBpm(bpm)] = id.ToUpperInvariant();

        var definitions = new List<GeneratedBpmDefinition>();
        foreach (var generated in events)
        {
            var text = FormatBpm(generated.Bpm, decimals);
            var canonical = CanonicalBpm(double.Parse(text, CultureInfo.InvariantCulture));
            if (!bpmToId.TryGetValue(canonical, out var id))
            {
                id = Enumerable.Range(1, MaximumExtendedBpmIds)
                    .Select(ToBase36Id)
                    .FirstOrDefault(candidate => !usedIds.Contains(candidate)) ?? string.Empty;
                if (id.Length == 0)
                {
                    errors.Add("사용 가능한 확장 BPM ID가 부족합니다.");
                    break;
                }
                usedIds.Add(id);
                bpmToId[canonical] = id;
                definitions.Add(new GeneratedBpmDefinition(id, generated.Bpm, text));
            }
            generated.Id = id;
            generated.BpmText = text;
        }
        return definitions;
    }

    private static HashSet<string> CollectUsedIds(BmsDocument document)
    {
        var usedIds = new HashSet<string>(document.BpmDefinitions.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var existing in document.ExistingBpmEvents.Where(item => item.Channel == "08"))
            usedIds.Add(existing.Token);
        return usedIds;
    }

    private static int CountRequiredNewDefinitions(
        BmsDocument document,
        IEnumerable<GeneratedBpmEvent> events,
        int decimals)
    {
        var existingBpms = document.BpmDefinitions
            .Where(item => item.Key != "00")
            .Select(item => CanonicalBpm(item.Value))
            .ToHashSet(StringComparer.Ordinal);

        return events
            .Select(generated => CanonicalBpm(double.Parse(FormatBpm(generated.Bpm, decimals), CultureInfo.InvariantCulture)))
            .Distinct(StringComparer.Ordinal)
            .Count(canonical => !existingBpms.Contains(canonical));
    }

    private static string CanonicalBpm(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static string ToBase36Id(int number)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return string.Concat(digits[(number / 36) % 36], digits[number % 36]);
    }

    private static List<string> BuildChannelLines(
        IEnumerable<GeneratedBpmEvent> events,
        ICollection<string> errors,
        ICollection<ConversionConflict> conflicts)
    {
        var lines = new List<string>();
        foreach (var group in events.Where(item => item.Id.Length == 2).GroupBy(item => item.Measure).OrderBy(group => group.Key))
        {
            var resolution = group.Aggregate(1, (current, item) => MathUtil.Lcm(current, item.Position.Denominator));
            if (resolution > MaximumChannelResolution)
            {
                errors.Add($"{group.Key:000}마디의 BPM 배치 해상도가 너무 큽니다({resolution}).");
                continue;
            }

            var slots = Enumerable.Repeat("00", resolution).ToArray();
            foreach (var generated in group)
            {
                var index = generated.Position.Numerator * (resolution / generated.Position.Denominator);
                if (index < 0 || index >= resolution)
                {
                    errors.Add($"{group.Key:000}마디의 BPM 위치를 BMS 채널로 변환할 수 없습니다.");
                    continue;
                }
                if (slots[index] != "00" && slots[index] != generated.Id)
                    conflicts.Add(new ConversionConflict("generated", $"{group.Key:000}마디 {index}/{resolution} 위치가 중복됩니다."));
                slots[index] = generated.Id;
            }
            lines.Add($"#{group.Key:000}08:{string.Concat(slots)}");
        }
        return lines;
    }
}
