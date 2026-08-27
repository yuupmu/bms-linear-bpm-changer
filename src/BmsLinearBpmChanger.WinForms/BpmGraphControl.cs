using BmsLinearBpmChanger.Core;
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace BmsLinearBpmChanger;

internal sealed class BpmGraphControl : Panel
{
    private IReadOnlyList<PreviewRow> _rows = Array.Empty<PreviewRow>();

    public BpmGraphControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(70, 83, 94);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<PreviewRow> Rows
    {
        get => _rows;
        set
        {
            _rows = value ?? Array.Empty<PreviewRow>();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.White);

        var plot = new Rectangle(50, 14, Math.Max(10, ClientSize.Width - 66), Math.Max(10, ClientSize.Height - 42));
        if (_rows.Count == 0)
        {
            using var messageBrush = new SolidBrush(Color.FromArgb(115, 130, 140));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString("BMS 파일과 변속 구간을 입력하면 그래프가 표시됩니다.", Font, messageBrush, ClientRectangle, format);
            DrawBorder(graphics, plot);
            return;
        }

        var allBpms = _rows.SelectMany(row => new[] { row.BpmStart, row.BpmEnd, row.OutputBpm }).ToArray();
        var minimum = allBpms.Min();
        var maximum = allBpms.Max();
        var padding = Math.Max(2d, (maximum - minimum) * 0.1d);
        minimum = Math.Max(0d, minimum - padding);
        maximum += padding;
        if (maximum - minimum < 1d)
            maximum = minimum + 1d;

        float X(double index) => plot.Left + (float)(index / _rows.Count * plot.Width);
        float Y(double bpm) => plot.Top + (float)((maximum - bpm) / (maximum - minimum) * plot.Height);

        using var gridPen = new Pen(Color.FromArgb(222, 228, 233));
        using var labelBrush = new SolidBrush(Color.FromArgb(78, 94, 105));
        using var labelFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        for (var grid = 0; grid <= 4; grid++)
        {
            var y = plot.Top + grid / 4f * plot.Height;
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var value = maximum - grid / 4d * (maximum - minimum);
            graphics.DrawString(value.ToString("0.0"), Font, labelBrush, new RectangleF(0, y - 9, plot.Left - 6, 18), labelFormat);
        }
        for (var grid = 0; grid <= 8; grid++)
        {
            var x = plot.Left + grid / 8f * plot.Width;
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
        }

        var previousSegment = -1;
        using (var idealPen = new Pen(Color.FromArgb(205, 70, 59), 2f))
        {
            for (var index = 0; index < _rows.Count; index++)
            {
                var row = _rows[index];
                if (row.SegmentIndex != previousSegment)
                    previousSegment = row.SegmentIndex;
                graphics.DrawLine(idealPen, X(index), Y(row.BpmStart), X(index + 1), Y(row.BpmEnd));
            }
        }

        using (var generatedPen = new Pen(Color.FromArgb(39, 124, 175), 2f))
        {
            for (var index = 0; index < _rows.Count; index++)
            {
                var row = _rows[index];
                graphics.DrawLine(generatedPen, X(index), Y(row.OutputBpm), X(index + 1), Y(row.OutputBpm));
                if (index + 1 < _rows.Count && _rows[index + 1].SegmentIndex == row.SegmentIndex)
                    graphics.DrawLine(generatedPen, X(index + 1), Y(row.OutputBpm), X(index + 1), Y(_rows[index + 1].OutputBpm));
            }
        }

        DrawBorder(graphics, plot);
        using var axisFormat = new StringFormat { Alignment = StringAlignment.Center };
        graphics.DrawString("근사 구간 진행", Font, labelBrush, new RectangleF(plot.Left, plot.Bottom + 7, plot.Width, 18), axisFormat);
    }

    private static void DrawBorder(Graphics graphics, Rectangle plot)
    {
        using var borderPen = new Pen(Color.FromArgb(112, 132, 146));
        graphics.DrawRectangle(borderPen, plot);
    }
}
