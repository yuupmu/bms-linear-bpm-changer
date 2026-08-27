using System.Text;
using BmsLinearBpmChanger.Core;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var tests = new (string Name, Action Run)[]
{
    ("시간 등가 평균과 반올림", TimeEquivalentAverage),
    ("3/4박 4분음표 이벤트", ThreeQuarterQuarterNoteEvents),
    ("4/4박 4분음표와 16분음표 이벤트", FourFourSubdivisionEvents),
    ("구간별 근사 단위 혼합", MixedSegmentSubdivisions),
    ("변박 구간 누적 박자 보간", MixedMeterProgress),
    ("BPM ID 사용량 예상", BpmIdUsage),
    ("기존 BPM 이벤트 충돌", ExistingBpmCollision),
    ("CP949 바이트와 CRLF 보존", Cp949AndCrLfPreservation),
    ("Shift-JIS 바이트 보존", ShiftJisPreservation),
    ("UTF-8 BOM 보존", Utf8BomPreservation),
    ("인접 구간 경계 처리", AdjacentSegments),
    ("출력 파일명", OutputFileName),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.WriteLine($"FAIL  {test.Name}");
        Console.WriteLine($"      {error.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

static void TimeEquivalentAverage()
{
    var document = ParseAscii("#PLAYER 1\r\n#TITLE Demo\r\n#BPM 120\r\n#04102:0.25\r\n#04111:01\r\n");
    var prepared = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.TimeEquivalent));
    Assert(prepared.CanConvert, "변환 가능해야 합니다.");
    Assert(prepared.Rows.Count == 1, "1/4박 한 마디의 4분음표 구간은 1개여야 합니다.");
    AssertNear(147.98, prepared.Rows[0].OutputBpm, 1e-9, "120→180 로그평균");
    Assert(Math.Abs(prepared.TotalErrorMilliseconds) < 0.2, "시간 등가 평균의 반올림 오차가 너무 큽니다.");

    var arithmetic = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.Arithmetic));
    AssertNear(150, arithmetic.Rows[0].OutputBpm, 1e-9, "산술평균");
    Assert(Math.Abs(arithmetic.TotalErrorMilliseconds) > Math.Abs(prepared.TotalErrorMilliseconds), "산술평균이 시간 등가 평균보다 정확한 것으로 계산되었습니다.");
}

static void ThreeQuarterQuarterNoteEvents()
{
    var document = ParseAscii("#BPM 120\n#04102:0.75\n#04111:010101\n");
    var prepared = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.TimeEquivalent));
    Assert(prepared.CanConvert, "변환 가능해야 합니다.");
    Assert(prepared.Rows.Count == 3, "3/4박 한 마디는 4분음표 구간이 3개여야 합니다.");
    Assert(prepared.Events.Count == 4, "4분음표 이벤트 3개와 끝 경계 이벤트가 필요합니다.");
    var line = prepared.ChannelLines.Single(item => item.StartsWith("#04108:", StringComparison.Ordinal));
    Assert(line[7..].Length == 6, "041마디 채널은 3칸(6문자)이어야 합니다.");
}

static void FourFourSubdivisionEvents()
{
    var document = ParseAscii("#BPM 120\n#04111:01010101\n");
    var quarterNote = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.TimeEquivalent));
    Assert(quarterNote.CanConvert, "4/4박 4분음표 변환이 가능해야 합니다.");
    Assert(quarterNote.Rows.Count == 4, "4/4박 한 마디는 4분음표 구간이 4개여야 합니다.");
    Assert(quarterNote.Events.Count == 5, "4분음표 이벤트 4개와 끝 경계 이벤트가 필요합니다.");
    var quarterNoteData = quarterNote.ChannelLines.Single(item => item.StartsWith("#04108:", StringComparison.Ordinal))[7..];
    Assert(quarterNoteData.Length == 8, "4분음표 채널은 4칸이어야 합니다.");
    Assert(HasNoEmptySlots(quarterNoteData), "4분음표 채널의 모든 위치에 BPM 이벤트가 있어야 합니다.");

    var sixteenthNote = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.SixteenthNote, AverageMethod.TimeEquivalent));
    Assert(sixteenthNote.CanConvert, "4/4박 16분음표 변환이 가능해야 합니다.");
    Assert(sixteenthNote.Rows.Count == 16, "4/4박 한 마디는 16분음표 구간이 16개여야 합니다.");
    Assert(sixteenthNote.Events.Count == 17, "16분음표 이벤트 16개와 끝 경계 이벤트가 필요합니다.");
    var sixteenthNoteData = sixteenthNote.ChannelLines.Single(item => item.StartsWith("#04108:", StringComparison.Ordinal))[7..];
    Assert(sixteenthNoteData.Length == 32, "16분음표 채널은 16칸이어야 합니다.");
    Assert(HasNoEmptySlots(sixteenthNoteData), "16분음표 채널의 모든 위치에 BPM 이벤트가 있어야 합니다.");
}

static void MixedSegmentSubdivisions()
{
    var document = ParseAscii("#BPM 120\n#04111:01010101\n#04211:01010101\n");
    var options = new ConversionOptions(
        new[]
        {
            new SegmentInput(41, 42, 120, 140, SubdivisionUnit.QuarterNote),
            new SegmentInput(42, 43, 140, 180, SubdivisionUnit.SixteenthNote),
        },
        AverageMethod.TimeEquivalent,
        2);
    var prepared = LinearBpmEngine.Prepare(document, options);

    Assert(prepared.CanConvert, "서로 다른 근사 단위를 사용하는 인접 구간을 변환할 수 있어야 합니다.");
    Assert(prepared.Rows.Count(row => row.SegmentIndex == 0) == 4, "첫 구간은 4분음표 단위여야 합니다.");
    Assert(prepared.Rows.Count(row => row.SegmentIndex == 1) == 16, "둘째 구간은 16분음표 단위여야 합니다.");
    Assert(prepared.ChannelLines.Single(item => item.StartsWith("#04108:", StringComparison.Ordinal))[7..].Length == 8, "첫 구간 채널 해상도가 잘못되었습니다.");
    Assert(prepared.ChannelLines.Single(item => item.StartsWith("#04208:", StringComparison.Ordinal))[7..].Length == 32, "둘째 구간 채널 해상도가 잘못되었습니다.");
}

static void MixedMeterProgress()
{
    var document = ParseAscii("#BPM 120\n#04102:0.75\n#04111:010101\n#04202:1.25\n#04211:0101010101\n");
    var options = new ConversionOptions(
        new[] { new SegmentInput(41, 43, 120, 200, SubdivisionUnit.QuarterNote) },
        AverageMethod.TimeEquivalent,
        2);
    var prepared = LinearBpmEngine.Prepare(document, options);
    var secondMeasureStart = prepared.Rows.Single(row => row.Measure == 42 && row.Slot == 0);

    AssertNear(150, secondMeasureStart.BpmStart, 1e-9, "3/4 뒤 5/4 경계 BPM");
}

static void BpmIdUsage()
{
    var document = ParseAscii("#BPM 120\n#BPM01 90\n#04111:01010101\n");
    var quarterNote = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.TimeEquivalent));
    var sixteenthNote = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.SixteenthNote, AverageMethod.TimeEquivalent));

    Assert(quarterNote.ExistingBpmIdCount == 1, "기존 BPM ID 수가 잘못되었습니다.");
    Assert(quarterNote.RequiredNewBpmIdCount == 5, "4분음표 선택의 신규 BPM ID 예상 수가 잘못되었습니다.");
    Assert(quarterNote.TotalBpmIdCount == 6, "4분음표 선택의 전체 BPM ID 수가 잘못되었습니다.");
    Assert(sixteenthNote.RequiredNewBpmIdCount == 17, "16분음표 선택의 신규 BPM ID 예상 수가 잘못되었습니다.");

    var reusable = ParseAscii("#BPM 120\n#BPM01 120\n#04111:01010101\n");
    var reusePrepared = LinearBpmEngine.Prepare(
        reusable,
        new ConversionOptions(new[] { new SegmentInput(41, 42, 120, 120, SubdivisionUnit.QuarterNote) }, AverageMethod.TimeEquivalent, 2));
    Assert(reusePrepared.RequiredNewBpmIdCount == 0, "기존 BPM 정의와 같은 값은 ID를 재사용해야 합니다.");
    Assert(reusePrepared.Definitions.Count == 0, "재사용 가능한 BPM에 새 정의를 만들었습니다.");

    var reserved = ParseAscii("#BPM 120\n#BPM00 120\n#04111:01010101\n");
    var reservedPrepared = LinearBpmEngine.Prepare(
        reserved,
        new ConversionOptions(new[] { new SegmentInput(41, 42, 120, 120, SubdivisionUnit.QuarterNote) }, AverageMethod.TimeEquivalent, 2));
    Assert(reservedPrepared.RequiredNewBpmIdCount == 1, "예약 ID 00은 재사용 가능한 BPM ID로 계산하면 안 됩니다.");
    Assert(reservedPrepared.Events.All(item => item.Id != "00"), "생성 이벤트에 예약 ID 00을 할당했습니다.");
}

static void ExistingBpmCollision()
{
    var document = ParseAscii("#BPM 120\n#BPM01 140\n#04108:01\n");
    var prepared = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.TimeEquivalent));
    Assert(!prepared.CanConvert, "기존 BPM 이벤트가 있으면 저장을 막아야 합니다.");
    Assert(prepared.Conflicts.Any(item => item.Message.Contains("기존 BPM 이벤트", StringComparison.Ordinal)), "충돌 설명이 없습니다.");
}

static void Cp949AndCrLfPreservation()
{
    const string source = "#PLAYER 1\r\n#TITLE 테스트 곡\r\n#ARTIST 뽀무\r\n#BPM 120\r\n#04102:0.75\r\n#04111:010101\r\n";
    var encoding = Encoding.GetEncoding(949);
    var sourceBytes = encoding.GetBytes(source);
    var document = BmsParser.Parse(sourceBytes, "korean.bms");
    var prepared = LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.TimeEquivalent));
    var output = BmsWriter.Build(document, prepared);
    var titleBytes = encoding.GetBytes("#TITLE 테스트 곡");

    Assert(Contains(output, titleBytes), "CP949 제목 바이트가 그대로 남아 있지 않습니다.");
    Assert(document.NewlineName == "CRLF", "CRLF 감지 실패");
    Assert(HasOnlyCrLf(output), "출력에 다른 줄바꿈이 섞였습니다.");
}

static void ShiftJisPreservation()
{
    const string source = "#TITLE 竹テスト\r\n#BPM 120\r\n#04102:0.75\r\n#04111:010101\r\n";
    var encoding = Encoding.GetEncoding(932);
    var sourceBytes = encoding.GetBytes(source);
    var document = BmsParser.Parse(sourceBytes, "japanese.bms");
    var output = BmsWriter.Build(document, LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.TimeEquivalent)));
    Assert(Contains(output, encoding.GetBytes("#TITLE 竹テスト")), "Shift-JIS 제목 바이트가 그대로 남아 있지 않습니다.");
}

static void Utf8BomPreservation()
{
    const string source = "#TITLE BOM 테스트\n#BPM 120\n#04111:01";
    var payload = Encoding.UTF8.GetBytes(source);
    var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(payload).ToArray();
    var document = BmsParser.Parse(bytes, "bom.bms");
    var output = BmsWriter.Build(document, LinearBpmEngine.Prepare(document, Options(SubdivisionUnit.QuarterNote, AverageMethod.TimeEquivalent)));
    Assert(output.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 BOM이 보존되지 않았습니다.");
    Assert(output[^1] != 0x0A && output[^1] != 0x0D, "원본에 없던 마지막 줄바꿈이 추가되었습니다.");
}

static void AdjacentSegments()
{
    var document = ParseAscii("#BPM 120\n#04111:01\n");
    var options = new ConversionOptions(
        new[] { new SegmentInput(41, 49, 120, 180), new SegmentInput(49, 53, 180, 140) },
        AverageMethod.TimeEquivalent,
        2);
    var prepared = LinearBpmEngine.Prepare(document, options);
    Assert(prepared.CanConvert, "같은 BPM으로 이어지는 인접 구간은 허용해야 합니다.");
    Assert(prepared.Events.Count(item => item.Measure == 49 && item.Position.IsZero) == 1, "인접 경계 이벤트가 중복되었습니다.");

    var mismatched = options with
    {
        Segments = new[] { new SegmentInput(41, 49, 120, 180), new SegmentInput(49, 53, 170, 140) },
    };
    var discontinuous = LinearBpmEngine.Prepare(document, mismatched);
    Assert(discontinuous.CanConvert, "BPM이 다른 인접 경계도 의도적인 즉시 전환으로 허용해야 합니다.");
    Assert(discontinuous.Warnings.Any(item => item.Contains("즉시 전환", StringComparison.Ordinal)), "불연속 경계 경고가 없습니다.");
}

static void OutputFileName()
{
    Assert(BmsWriter.OutputFileName("second_song_test.bms") == "second_song_test_linear_bpm.bms", "출력 파일명이 요구 형식과 다릅니다.");
}

static ConversionOptions Options(SubdivisionUnit subdivision, AverageMethod average) =>
    new(new[] { new SegmentInput(41, 42, 120, 180, subdivision) }, average, 2);

static BmsDocument ParseAscii(string text) => BmsParser.Parse(Encoding.ASCII.GetBytes(text), "demo.bms");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertNear(double expected, double actual, double tolerance, string label)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
}

static bool Contains(byte[] haystack, byte[] needle)
{
    if (needle.Length == 0)
        return true;
    for (var start = 0; start <= haystack.Length - needle.Length; start++)
    {
        if (haystack.AsSpan(start, needle.Length).SequenceEqual(needle))
            return true;
    }
    return false;
}

static bool HasNoEmptySlots(string data) =>
    Enumerable.Range(0, data.Length / 2).All(index => data.Substring(index * 2, 2) != "00");

static bool HasOnlyCrLf(byte[] bytes)
{
    for (var index = 0; index < bytes.Length; index++)
    {
        if (bytes[index] == 0x0A && (index == 0 || bytes[index - 1] != 0x0D))
            return false;
        if (bytes[index] == 0x0D && (index + 1 >= bytes.Length || bytes[index + 1] != 0x0A))
            return false;
    }
    return true;
}
