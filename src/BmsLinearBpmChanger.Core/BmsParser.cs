using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BmsLinearBpmChanger.Core;

public static partial class BmsParser
{
    private const string NumberPattern = @"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?";

    private static readonly Regex BpmDefinitionRegex = new(
        $@"^#BPM([0-9A-Z]{{2}})(?:\s+|:)\s*({NumberPattern})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BaseBpmRegex = new(
        $@"^#BPM\s+({NumberPattern})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MeasureRatioRegex = new(
        $@"^#(\d{{3}})02:\s*({NumberPattern})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChannelRegex = new(
        @"^#(\d{3})([0-9A-Z]{2}):([^\s]*)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static BmsParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static BmsDocument Parse(byte[] bytes, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var lines = SplitLines(bytes);
        var (newlineName, newlineBytes) = DetectNewline(lines);
        var encodingInfo = DetectEncoding(bytes);
        var decoder = GetEncoding(encodingInfo.CodePage, strict: false);
        var measureRatios = new Dictionary<int, double>();
        var bpmDefinitions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var existingEvents = new List<ExistingBpmEvent>();
        var warnings = new List<string>();
        double? baseBpm = null;
        var title = string.Empty;
        var artist = string.Empty;
        var firstMainLine = -1;
        var maxMeasure = 0;

        // First pass: definitions, measure lengths and metadata.
        for (var index = 0; index < lines.Count; index++)
        {
            var ascii = AsciiText(lines[index].Content, index == 0);
            var bpmDefinition = BpmDefinitionRegex.Match(ascii);
            if (bpmDefinition.Success)
            {
                var id = bpmDefinition.Groups[1].Value.ToUpperInvariant();
                if (TryPositiveDouble(bpmDefinition.Groups[2].Value, out var bpm))
                    bpmDefinitions[id] = bpm;
                else
                    warnings.Add($"{index + 1}행: 잘못된 확장 BPM 정의입니다.");
                continue;
            }

            var headerBpm = BaseBpmRegex.Match(ascii);
            if (headerBpm.Success)
            {
                if (TryPositiveDouble(headerBpm.Groups[1].Value, out var bpm))
                    baseBpm = bpm;
                continue;
            }

            var ratioMatch = MeasureRatioRegex.Match(ascii);
            if (ratioMatch.Success)
            {
                var measure = int.Parse(ratioMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                if (TryPositiveDouble(ratioMatch.Groups[2].Value, out var ratio))
                    measureRatios[measure] = ratio;
                else
                    warnings.Add($"{index + 1}행: 잘못된 마디 길이(#xxx02)입니다.");
                maxMeasure = Math.Max(maxMeasure, measure);
                if (firstMainLine < 0)
                    firstMainLine = index;
                continue;
            }

            var channelMatch = ChannelRegex.Match(ascii);
            if (channelMatch.Success)
            {
                var measure = int.Parse(channelMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                maxMeasure = Math.Max(maxMeasure, measure);
                if (firstMainLine < 0)
                    firstMainLine = index;
            }

            if (ascii.StartsWith("#TITLE", StringComparison.OrdinalIgnoreCase))
                title = DecodeDirective(lines[index].Content, decoder, "#TITLE", index == 0);
            else if (ascii.StartsWith("#ARTIST", StringComparison.OrdinalIgnoreCase))
                artist = DecodeDirective(lines[index].Content, decoder, "#ARTIST", index == 0);
        }

        // Second pass: resolve BPM events after every #BPMxx definition is known.
        for (var index = 0; index < lines.Count; index++)
        {
            var ascii = AsciiText(lines[index].Content, index == 0);
            var match = ChannelRegex.Match(ascii);
            if (!match.Success)
                continue;

            var measure = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var channel = match.Groups[2].Value.ToUpperInvariant();
            var data = match.Groups[3].Value.ToUpperInvariant();
            if (channel is not ("03" or "08"))
                continue;

            if (data.Length % 2 != 0)
            {
                warnings.Add($"{index + 1}행: BPM 채널 데이터 길이가 홀수라 무시했습니다.");
                continue;
            }

            var count = data.Length / 2;
            for (var slot = 0; slot < count; slot++)
            {
                var token = data.Substring(slot * 2, 2);
                if (token == "00")
                    continue;

                if (channel == "03" && !token.All(IsHexDigit))
                {
                    warnings.Add($"{index + 1}행: 올바르지 않은 직접 BPM 값 {token}입니다.");
                    continue;
                }

                var position = new PositionFraction(slot, Math.Max(1, count));
                double? bpm = channel == "03"
                    ? Convert.ToInt32(token, 16)
                    : bpmDefinitions.GetValueOrDefault(token);

                existingEvents.Add(new ExistingBpmEvent(measure, position, channel, token, bpm, index + 1));
                if (channel == "08" && bpm is null)
                    warnings.Add($"{index + 1}행: #BPM{token} 정의가 없습니다.");
            }
        }

        return new BmsDocument
        {
            OriginalBytes = bytes.ToArray(),
            Lines = lines.AsReadOnly(),
            NewlineBytes = newlineBytes,
            NewlineName = newlineName,
            Encoding = encodingInfo,
            FileName = fileName ?? "chart.bms",
            MeasureRatios = new Dictionary<int, double>(measureRatios),
            BpmDefinitions = new Dictionary<string, double>(bpmDefinitions, StringComparer.OrdinalIgnoreCase),
            ExistingBpmEvents = existingEvents.AsReadOnly(),
            ParseWarnings = warnings.Distinct().ToList().AsReadOnly(),
            BaseBpm = baseBpm,
            Title = title,
            Artist = artist,
            FirstMainLineIndex = firstMainLine,
            MaxMeasure = maxMeasure,
        };
    }

    public static List<ByteLine> SplitLines(byte[] bytes)
    {
        var lines = new List<ByteLine>();
        var start = 0;
        var index = 0;
        while (index < bytes.Length)
        {
            if (bytes[index] is 0x0D or 0x0A)
            {
                var newlineStart = index;
                if (bytes[index] == 0x0D && index + 1 < bytes.Length && bytes[index + 1] == 0x0A)
                    index += 2;
                else
                    index++;

                lines.Add(new ByteLine(
                    bytes[start..newlineStart],
                    bytes[newlineStart..index]));
                start = index;
            }
            else
            {
                index++;
            }
        }

        if (start < bytes.Length || lines.Count == 0)
            lines.Add(new ByteLine(bytes[start..], Array.Empty<byte>()));

        return lines;
    }

    public static EncodingInfo DetectEncoding(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        if (hasBom)
            return new EncodingInfo("UTF-8 (BOM)", Encoding.UTF8.CodePage, true, "높음");

        var hasNonAscii = bytes.Any(value => value >= 0x80);
        if (CanDecode(bytes, new UTF8Encoding(false, true)))
        {
            return hasNonAscii
                ? new EncodingInfo("UTF-8", Encoding.UTF8.CodePage, false, "높음")
                : new EncodingInfo("ASCII (인코딩 구분 불가, 원본 바이트 유지)", Encoding.UTF8.CodePage, false, "중립");
        }

        var candidates = new[]
        {
            Candidate(bytes, 932, "Shift-JIS"),
            Candidate(bytes, 949, "EUC-KR / CP949"),
        }.Where(candidate => candidate is not null).Cast<EncodingCandidate>().OrderByDescending(candidate => candidate.Score).ToList();

        if (candidates.Count > 0)
            return new EncodingInfo(candidates[0].Name, candidates[0].CodePage, false, candidates.Count == 1 ? "높음" : "추정");

        return new EncodingInfo("알 수 없음 (원본 바이트 유지)", Encoding.UTF8.CodePage, false, "낮음");
    }

    private static (string Name, byte[] Bytes) DetectNewline(IEnumerable<ByteLine> lines)
    {
        var counts = new Dictionary<string, int> { ["CRLF"] = 0, ["LF"] = 0, ["CR"] = 0 };
        foreach (var line in lines)
        {
            if (line.Newline.SequenceEqual(new byte[] { 0x0D, 0x0A }))
                counts["CRLF"]++;
            else if (line.Newline.SequenceEqual(new byte[] { 0x0A }))
                counts["LF"]++;
            else if (line.Newline.SequenceEqual(new byte[] { 0x0D }))
                counts["CR"]++;
        }

        var name = counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key == "CRLF" ? 0 : pair.Key == "LF" ? 1 : 2).First().Key;
        if (counts.Values.All(value => value == 0))
            name = "CRLF";
        return name switch
        {
            "LF" => (name, new byte[] { 0x0A }),
            "CR" => (name, new byte[] { 0x0D }),
            _ => (name, new byte[] { 0x0D, 0x0A }),
        };
    }

    private static string AsciiText(byte[] bytes, bool skipBom)
    {
        var start = skipBom && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var chars = new char[bytes.Length - start];
        for (var index = start; index < bytes.Length; index++)
            chars[index - start] = bytes[index] < 0x80 ? (char)bytes[index] : '?';
        return new string(chars);
    }

    private static string DecodeDirective(byte[] bytes, Encoding encoding, string directive, bool skipBom)
    {
        var start = skipBom && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var decoded = encoding.GetString(bytes, start, bytes.Length - start);
        return decoded.Length >= directive.Length ? decoded[directive.Length..].Trim() : string.Empty;
    }

    private static bool TryPositiveDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value > 0;

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'F';

    private static Encoding GetEncoding(int codePage, bool strict)
    {
        if (codePage == Encoding.UTF8.CodePage)
            return new UTF8Encoding(false, strict);
        return Encoding.GetEncoding(
            codePage,
            strict ? EncoderFallback.ExceptionFallback : EncoderFallback.ReplacementFallback,
            strict ? DecoderFallback.ExceptionFallback : DecoderFallback.ReplacementFallback);
    }

    private static bool CanDecode(byte[] bytes, Encoding encoding)
    {
        try
        {
            _ = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static EncodingCandidate? Candidate(byte[] bytes, int codePage, string name)
    {
        try
        {
            var encoding = GetEncoding(codePage, strict: true);
            var text = encoding.GetString(bytes);
            return new EncodingCandidate(codePage, name, LanguageScore(text));
        }
        catch (Exception error) when (error is DecoderFallbackException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static double LanguageScore(string text)
    {
        var score = 0d;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is >= 0xAC00 and <= 0xD7AF)
                score += 6;
            else if (value is >= 0x3040 and <= 0x30FF or >= 0xFF66 and <= 0xFF9F)
                score += 6;
            else if (value is >= 0x4E00 and <= 0x9FFF)
                score += 2;
            else if (value < 0x20 && value is not (0x09 or 0x0A or 0x0D))
                score -= 8;
            else if (value is >= 0x20 and < 0x7F)
                score += 0.02;
        }
        return score;
    }

    private sealed record EncodingCandidate(int CodePage, string Name, double Score);
}
