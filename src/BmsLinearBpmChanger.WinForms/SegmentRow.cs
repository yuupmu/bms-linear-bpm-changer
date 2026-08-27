using BmsLinearBpmChanger.Core;

namespace BmsLinearBpmChanger;

internal sealed class SegmentRow
{
    public int Number { get; set; }
    public int StartMeasure { get; set; }
    public int EndMeasure { get; set; }
    public double StartBpm { get; set; }
    public double EndBpm { get; set; }
    public SubdivisionUnit Subdivision { get; set; } = SubdivisionUnit.QuarterNote;
}

internal sealed record SubdivisionChoice(SubdivisionUnit Value, string Label);
