namespace BmsLinearBpmChanger;

internal sealed class SegmentRow
{
    public int Number { get; set; }
    public int StartMeasure { get; set; }
    public int EndMeasure { get; set; }
    public double StartBpm { get; set; }
    public double EndBpm { get; set; }
}
