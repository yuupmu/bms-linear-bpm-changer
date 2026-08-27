using BmsLinearBpmChanger.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace BmsLinearBpmChanger;

internal sealed class MainForm : Form
{
    private readonly BindingList<SegmentRow> _segments = new();
    private readonly OpenFileDialog _openDialog = new()
    {
        Filter = "BMS 파일 (*.bms;*.bme;*.bml;*.pms)|*.bms;*.bme;*.bml;*.pms|모든 파일 (*.*)|*.*",
        Title = "BMS 파일 열기",
        CheckFileExists = true,
    };
    private readonly System.Windows.Forms.Timer _analysisTimer = new() { Interval = 180 };

    private readonly ToolStripButton _saveButton = new("변환 파일 저장");
    private readonly ToolStripStatusLabel _statusLabel = new("준비") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Panel _dropPanel = new();
    private readonly Label _dropTitle = new();
    private readonly Label _dropSubtitle = new();
    private readonly ListView _fileDetails = new();
    private readonly RadioButton _perMeasure = new() { Text = "마디당 1회", Checked = true, AutoSize = true };
    private readonly RadioButton _perBeat = new() { Text = "박자당 1회", AutoSize = true };
    private readonly RadioButton _timeEquivalent = new() { Text = "시간 등가 평균 (권장)", Checked = true, AutoSize = true };
    private readonly RadioButton _arithmetic = new() { Text = "단순 산술평균", AutoSize = true };
    private readonly NumericUpDown _decimalPlaces = new() { Minimum = 0, Maximum = 6, Value = 2, Width = 55, TextAlign = HorizontalAlignment.Right };
    private readonly DataGridView _segmentGrid = new();
    private readonly Panel _validationBanner = new();
    private readonly Label _validationIcon = new();
    private readonly Label _validationTitle = new();
    private readonly Label _validationSubtitle = new();
    private readonly ListBox _messageList = new();
    private readonly BpmGraphControl _graph = new();
    private readonly Label _graphCaption = new();
    private readonly DataGridView _previewGrid = new();
    private readonly Label _eventMetric = MetricValue();
    private readonly Label _timeMetric = MetricValue();
    private readonly Label _errorMetric = MetricValue();
    private readonly Label _outputHint = new();

    private string? _inputPath;
    private BmsDocument? _document;
    private PreparedConversion? _prepared;
    private bool _loadingGrid;

    public MainForm()
    {
        Text = "BMS Linear BPM Changer - 선형 변속 근사 데모";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 720);
        ClientSize = new Size(1380, 850);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(220, 229, 237);
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;

        MainMenuStrip = BuildMenu();
        Controls.Add(BuildWorkspace());
        Controls.Add(BuildToolbar());
        Controls.Add(MainMenuStrip);
        Controls.Add(BuildStatusBar());

        ConfigureEvents();
        AddSegment(new SegmentRow { StartMeasure = 41, EndMeasure = 49, StartBpm = 120, EndBpm = 180 });
        RenderFileDetails();
        RenderIdle();
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { RenderMode = ToolStripRenderMode.System };
        var file = new ToolStripMenuItem("파일(&F)");
        file.DropDownItems.Add(new ToolStripMenuItem("열기(&O)…", null, (_, _) => OpenFile()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripSeparator());
        var save = new ToolStripMenuItem("변환 파일 저장(&S)", null, (_, _) => SaveOutput()) { ShortcutKeys = Keys.Control | Keys.S };
        file.DropDownItems.Add(save);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("끝내기(&X)", null, (_, _) => Close()));

        var interval = new ToolStripMenuItem("구간(&I)");
        interval.DropDownItems.Add(new ToolStripMenuItem("새 구간 추가(&A)", null, (_, _) => AddDefaultSegment()) { ShortcutKeys = Keys.Insert });

        var help = new ToolStripMenuItem("도움말(&H)");
        help.DropDownItems.Add(new ToolStripMenuItem("프로그램 정보(&A)", null, (_, _) => ShowAbout()));
        menu.Items.AddRange(new ToolStripItem[] { file, interval, help });
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(5, 3, 5, 3),
            Height = 36,
        };
        var open = new ToolStripButton("BMS 열기", null, (_, _) => OpenFile()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        var add = new ToolStripButton("구간 추가", null, (_, _) => AddDefaultSegment()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        var refresh = new ToolStripButton("미리보기 갱신", null, (_, _) => Analyze()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _saveButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _saveButton.Enabled = false;
        _saveButton.Font = new Font(Font, FontStyle.Bold);
        _saveButton.Click += (_, _) => SaveOutput();
        toolbar.Items.AddRange(new ToolStripItem[] { open, add, refresh, new ToolStripSeparator(), _saveButton });
        return toolbar;
    }

    private StatusStrip BuildStatusBar()
    {
        var status = new StatusStrip { SizingGrip = true, RenderMode = ToolStripRenderMode.System };
        status.Items.Add(_statusLabel);
        status.Items.Add(new ToolStripStatusLabel("BMS Extended BPM · #BPMxx / #xxx08"));
        return status;
    }

    private Control BuildWorkspace()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 470,
            SplitterWidth = 7,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Color.FromArgb(177, 192, 204),
            Padding = new Padding(7),
        };
        split.Panel1.BackColor = BackColor;
        split.Panel2.BackColor = BackColor;
        split.Panel1.Controls.Add(BuildLeftPane());
        split.Panel2.Controls.Add(BuildRightPane());
        return split;
    }

    private Control BuildLeftPane()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 215));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(BuildFileGroup(), 0, 0);
        layout.Controls.Add(BuildSettingsGroup(), 0, 1);
        layout.Controls.Add(BuildSegmentsGroup(), 0, 2);
        return layout;
    }

    private Control BuildFileGroup()
    {
        var group = NewGroup("1. BMS 파일");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 79));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _dropPanel.Dock = DockStyle.Fill;
        _dropPanel.Margin = new Padding(3, 4, 3, 5);
        _dropPanel.BackColor = Color.FromArgb(244, 249, 253);
        _dropPanel.BorderStyle = BorderStyle.FixedSingle;
        _dropPanel.Cursor = Cursors.Hand;
        _dropPanel.AllowDrop = true;

        var fileBadge = new Label
        {
            Text = "BMS",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 90, 125),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(18, 13),
            Size = new Size(47, 49),
        };
        _dropTitle.Text = "여기에 BMS 파일을 드래그 앤 드롭";
        _dropTitle.Font = new Font(Font, FontStyle.Bold);
        _dropTitle.Location = new Point(80, 15);
        _dropTitle.AutoSize = true;
        _dropSubtitle.Text = "또는 클릭하여 파일을 선택하세요.";
        _dropSubtitle.ForeColor = SystemColors.GrayText;
        _dropSubtitle.Location = new Point(80, 39);
        _dropSubtitle.AutoSize = true;
        _dropPanel.Controls.AddRange(new Control[] { fileBadge, _dropTitle, _dropSubtitle });

        _fileDetails.Dock = DockStyle.Fill;
        _fileDetails.View = View.Details;
        _fileDetails.HeaderStyle = ColumnHeaderStyle.None;
        _fileDetails.FullRowSelect = true;
        _fileDetails.GridLines = true;
        _fileDetails.MultiSelect = false;
        _fileDetails.Columns.Add("항목", 102);
        _fileDetails.Columns.Add("값", 325);
        layout.Controls.Add(_dropPanel, 0, 0);
        layout.Controls.Add(_fileDetails, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildSettingsGroup()
    {
        var group = NewGroup("2. 근사 설정");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(5, 4, 5, 2) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var granularity = SettingPanel("배치 간격", _perMeasure, _perBeat);
        var average = SettingPanel("평균 방식", _timeEquivalent, _arithmetic);
        var rounding = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2, 5, 0, 0) };
        rounding.Controls.Add(new Label { Text = "BPM 소수점 자리", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 5, 8, 0) });
        rounding.Controls.Add(_decimalPlaces);
        rounding.Controls.Add(new Label { Text = "자리", AutoSize = true, Margin = new Padding(3, 5, 0, 0) });
        var hint = new Label
        {
            Text = "시간 등가 평균은 선형 변속의 실제 통과시간과 같아지도록 계산합니다.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(70, 90, 103),
            Padding = new Padding(3, 7, 0, 0),
        };
        layout.Controls.Add(granularity, 0, 0);
        layout.Controls.Add(average, 1, 0);
        layout.Controls.Add(rounding, 0, 1);
        layout.Controls.Add(hint, 1, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildSegmentsGroup()
    {
        var group = NewGroup("3. 변속 구간");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(4, 4, 4, 4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        var note = new Label
        {
            Text = "끝 마디는 목표 BPM에 도달하는 경계입니다.\r\n예: 041→049는 041 이상, 049 미만 구간을 변속합니다.",
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(238, 246, 251),
            ForeColor = Color.FromArgb(65, 85, 99),
            Padding = new Padding(6, 5, 4, 3),
        };
        ConfigureSegmentGrid();
        var add = new Button { Text = "＋ 새 구간", AutoSize = true, Anchor = AnchorStyles.Left, FlatStyle = FlatStyle.System };
        add.Click += (_, _) => AddDefaultSegment();
        layout.Controls.Add(note, 0, 0);
        layout.Controls.Add(_segmentGrid, 0, 1);
        layout.Controls.Add(add, 0, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildRightPane()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 285));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(BuildValidationGroup(), 0, 0);
        layout.Controls.Add(BuildGraphGroup(), 0, 1);
        layout.Controls.Add(BuildPreviewGroup(), 0, 2);
        return layout;
    }

    private Control BuildValidationGroup()
    {
        var group = NewGroup("충돌 검사");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _validationBanner.Dock = DockStyle.Fill;
        _validationBanner.BorderStyle = BorderStyle.FixedSingle;
        _validationIcon.Location = new Point(9, 11);
        _validationIcon.Size = new Size(30, 30);
        _validationIcon.TextAlign = ContentAlignment.MiddleCenter;
        _validationIcon.Font = new Font(Font, FontStyle.Bold);
        _validationIcon.BorderStyle = BorderStyle.FixedSingle;
        _validationTitle.Location = new Point(49, 8);
        _validationTitle.AutoSize = true;
        _validationTitle.Font = new Font(Font, FontStyle.Bold);
        _validationSubtitle.Location = new Point(49, 29);
        _validationSubtitle.AutoSize = true;
        _validationSubtitle.ForeColor = SystemColors.GrayText;
        _validationBanner.Controls.AddRange(new Control[] { _validationIcon, _validationTitle, _validationSubtitle });
        _messageList.Dock = DockStyle.Fill;
        _messageList.BorderStyle = BorderStyle.FixedSingle;
        _messageList.HorizontalScrollbar = true;
        layout.Controls.Add(_validationBanner, 0, 0);
        layout.Controls.Add(_messageList, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildGraphGroup()
    {
        var group = NewGroup("변환 전 BPM 그래프");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var legend = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        legend.Controls.Add(LegendLabel("━  입력 선형 BPM", Color.FromArgb(205, 70, 59)));
        legend.Controls.Add(LegendLabel("━  출력 근사 BPM", Color.FromArgb(39, 124, 175)));
        _graphCaption.Text = "파일을 불러오면 표시됩니다.";
        _graphCaption.AutoSize = true;
        _graphCaption.Margin = new Padding(18, 4, 0, 0);
        _graphCaption.ForeColor = SystemColors.GrayText;
        legend.Controls.Add(_graphCaption);
        _graph.Dock = DockStyle.Fill;
        _graph.BorderStyle = BorderStyle.FixedSingle;
        layout.Controls.Add(legend, 0, 0);
        layout.Controls.Add(_graph, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildPreviewGroup()
    {
        var group = NewGroup("BPM 표 · 누적 오차 미리보기");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
        for (var index = 0; index < 3; index++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        metrics.Controls.Add(MetricPanel("생성 이벤트", _eventMetric), 0, 0);
        metrics.Controls.Add(MetricPanel("구간 실제시간", _timeMetric), 1, 0);
        metrics.Controls.Add(MetricPanel("예상 누적 오차", _errorMetric), 2, 0);
        ConfigurePreviewGrid();
        _outputHint.Dock = DockStyle.Fill;
        _outputHint.TextAlign = ContentAlignment.MiddleLeft;
        _outputHint.ForeColor = Color.FromArgb(75, 91, 103);
        layout.Controls.Add(metrics, 0, 0);
        layout.Controls.Add(_previewGrid, 0, 1);
        layout.Controls.Add(_outputHint, 0, 2);
        group.Controls.Add(layout);
        return group;
    }

    private void ConfigureSegmentGrid()
    {
        _segmentGrid.Dock = DockStyle.Fill;
        _segmentGrid.AutoGenerateColumns = false;
        _segmentGrid.AllowUserToAddRows = false;
        _segmentGrid.AllowUserToDeleteRows = false;
        _segmentGrid.AllowUserToResizeRows = false;
        _segmentGrid.RowHeadersVisible = false;
        _segmentGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _segmentGrid.BackgroundColor = SystemColors.Window;
        _segmentGrid.BorderStyle = BorderStyle.Fixed3D;
        _segmentGrid.DataSource = _segments;
        _segmentGrid.Columns.Add(TextColumn("No.", nameof(SegmentRow.Number), 42, readOnly: true));
        _segmentGrid.Columns.Add(TextColumn("시작 마디", nameof(SegmentRow.StartMeasure), 83));
        _segmentGrid.Columns.Add(TextColumn("끝 마디", nameof(SegmentRow.EndMeasure), 83));
        _segmentGrid.Columns.Add(TextColumn("시작 BPM", nameof(SegmentRow.StartBpm), 88));
        _segmentGrid.Columns.Add(TextColumn("끝 BPM", nameof(SegmentRow.EndBpm), 88));
        _segmentGrid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Text = "×", UseColumnTextForButtonValue = true, Width = 34, FlatStyle = FlatStyle.System });
    }

    private void ConfigurePreviewGrid()
    {
        _previewGrid.Dock = DockStyle.Fill;
        _previewGrid.AllowUserToAddRows = false;
        _previewGrid.AllowUserToDeleteRows = false;
        _previewGrid.AllowUserToResizeRows = false;
        _previewGrid.ReadOnly = true;
        _previewGrid.RowHeadersVisible = false;
        _previewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _previewGrid.MultiSelect = false;
        _previewGrid.BackgroundColor = SystemColors.Window;
        _previewGrid.BorderStyle = BorderStyle.Fixed3D;
        _previewGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _previewGrid.Columns.Add("Segment", "구간");
        _previewGrid.Columns.Add("Position", "배치 위치");
        _previewGrid.Columns.Add("StartBpm", "선형 시작 BPM");
        _previewGrid.Columns.Add("EndBpm", "선형 끝 BPM");
        _previewGrid.Columns.Add("OutputBpm", "출력 BPM");
        _previewGrid.Columns.Add("IntervalTime", "구간 시간");
        _previewGrid.Columns.Add("Error", "누적 오차");
        _previewGrid.Columns[0].FillWeight = 45;
        _previewGrid.Columns[1].FillWeight = 75;
    }

    private void ConfigureEvents()
    {
        _analysisTimer.Tick += (_, _) =>
        {
            _analysisTimer.Stop();
            Analyze();
        };
        _perMeasure.CheckedChanged += (_, _) => ScheduleAnalysis();
        _perBeat.CheckedChanged += (_, _) => ScheduleAnalysis();
        _timeEquivalent.CheckedChanged += (_, _) => ScheduleAnalysis();
        _arithmetic.CheckedChanged += (_, _) => ScheduleAnalysis();
        _decimalPlaces.ValueChanged += (_, _) => ScheduleAnalysis();
        _segmentGrid.CellValueChanged += (_, _) => ScheduleAnalysis();
        _segmentGrid.CellEndEdit += (_, _) => ScheduleAnalysis();
        _segmentGrid.DataError += (_, eventArgs) =>
        {
            eventArgs.ThrowException = false;
            SetStatus("표의 숫자 입력값을 확인하세요.");
        };
        _segmentGrid.CellContentClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != _segmentGrid.Columns.Count - 1)
                return;
            if (_segments.Count <= 1)
            {
                SetStatus("구간은 하나 이상 필요합니다.");
                return;
            }
            _segments.RemoveAt(eventArgs.RowIndex);
            RenumberSegments();
            Analyze();
        };

        foreach (var control in new Control[] { _dropPanel, _dropTitle, _dropSubtitle })
        {
            control.Click += (_, _) => OpenFile();
            control.AllowDrop = true;
            control.DragEnter += FileDragEnter;
            control.DragDrop += FileDragDrop;
        }
        DragEnter += FileDragEnter;
        DragDrop += FileDragDrop;
    }

    private void OpenFile()
    {
        if (_openDialog.ShowDialog(this) == DialogResult.OK)
            LoadBms(_openDialog.FileName);
    }

    private void LoadBms(string path)
    {
        try
        {
            SetStatus($"{Path.GetFileName(path)} 읽는 중…");
            var bytes = File.ReadAllBytes(path);
            _document = BmsParser.Parse(bytes, Path.GetFileName(path));
            _inputPath = path;
            _dropTitle.Text = Path.GetFileName(path);
            _dropSubtitle.Text = $"{FormatBytes(bytes.LongLength)} · 다른 파일을 놓으면 교체됩니다.";
            RenderFileDetails();
            Analyze();
            SetStatus($"{Path.GetFileName(path)} 불러오기 완료");
        }
        catch (Exception error)
        {
            _document = null;
            _inputPath = null;
            RenderIdle();
            MessageBox.Show(this, $"BMS 파일을 읽지 못했습니다.\r\n\r\n{error.Message}", "파일 열기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("파일을 불러오지 못했습니다.");
        }
    }

    private void FileDragEnter(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.Effect = eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void FileDragDrop(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;
        var extension = Path.GetExtension(files[0]);
        if (!new[] { ".bms", ".bme", ".bml", ".pms" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "BMS/BME/BML/PMS 파일을 놓아주세요.", "지원하지 않는 파일", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        LoadBms(files[0]);
    }

    private void AddDefaultSegment()
    {
        var prior = _segments.LastOrDefault();
        AddSegment(prior is null
            ? new SegmentRow { StartMeasure = 0, EndMeasure = 8, StartBpm = 120, EndBpm = 180 }
            : new SegmentRow { StartMeasure = prior.EndMeasure, EndMeasure = Math.Min(999, prior.EndMeasure + 8), StartBpm = prior.EndBpm, EndBpm = prior.EndBpm });
    }

    private void AddSegment(SegmentRow segment)
    {
        _segments.Add(segment);
        RenumberSegments();
        ScheduleAnalysis();
    }

    private void RenumberSegments()
    {
        _loadingGrid = true;
        for (var index = 0; index < _segments.Count; index++)
            _segments[index].Number = index + 1;
        _segmentGrid.Refresh();
        _loadingGrid = false;
    }

    private void ScheduleAnalysis()
    {
        if (_loadingGrid)
            return;
        _analysisTimer.Stop();
        _analysisTimer.Start();
    }

    private void Analyze()
    {
        if (_document is null)
        {
            RenderIdle();
            return;
        }

        try
        {
            _segmentGrid.EndEdit();
            var options = new ConversionOptions(
                _segments.Select(segment => new SegmentInput(segment.StartMeasure, segment.EndMeasure, segment.StartBpm, segment.EndBpm)).ToList(),
                _perBeat.Checked ? ApproximationGranularity.PerBeat : ApproximationGranularity.PerMeasure,
                _arithmetic.Checked ? AverageMethod.Arithmetic : AverageMethod.TimeEquivalent,
                (int)_decimalPlaces.Value);
            _prepared = LinearBpmEngine.Prepare(_document, options);
            RenderValidation(_prepared);
            RenderPreview(_prepared);
            _graph.Rows = _prepared.Rows;
            _saveButton.Enabled = _prepared.CanConvert;
            SetStatus(_prepared.CanConvert
                ? $"미리보기 완료 · BPM 이벤트 {_prepared.Events.Count}개 생성 예정"
                : "입력 오류 또는 BPM 충돌을 확인하세요.");
        }
        catch (Exception error)
        {
            _prepared = null;
            _saveButton.Enabled = false;
            RenderAnalysisError(error.Message);
        }
    }

    private void SaveOutput()
    {
        if (_document is null || _prepared is null || !_prepared.CanConvert || _inputPath is null)
            return;

        var outputPath = BmsWriter.OutputPath(_inputPath);
        if (File.Exists(outputPath))
        {
            var answer = MessageBox.Show(
                this,
                $"이미 같은 이름의 파일이 있습니다. 덮어쓸까요?\r\n\r\n{outputPath}",
                "변환 파일 덮어쓰기",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;
        }

        try
        {
            File.WriteAllBytes(outputPath, BmsWriter.Build(_document, _prepared));
            SetStatus($"{Path.GetFileName(outputPath)} 저장 완료 · 원본은 변경되지 않았습니다.");
            var answer = MessageBox.Show(
                this,
                $"변환 파일을 저장했습니다.\r\n\r\n{outputPath}\r\n\r\n파일 위치를 열까요?",
                "변환 완료",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{outputPath}\"") { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"변환 파일을 저장하지 못했습니다.\r\n\r\n{error.Message}", "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("변환 파일 저장 실패");
        }
    }

    private void RenderFileDetails()
    {
        _fileDetails.Items.Clear();
        if (_document is null || _inputPath is null)
        {
            AddDetail("파일", "선택되지 않음");
            AddDetail("곡 정보", "—");
            AddDetail("인코딩", "—");
            AddDetail("줄바꿈", "—");
            AddDetail("기본 BPM", "—");
            AddDetail("기존 BPM 이벤트", "—");
            return;
        }

        var info = new FileInfo(_inputPath);
        var song = string.Join(" / ", new[] { _document.Title, _document.Artist }.Where(value => !string.IsNullOrWhiteSpace(value)));
        AddDetail("파일", $"{info.Name} · {FormatBytes(info.Length)}");
        AddDetail("곡 정보", song.Length == 0 ? "메타데이터 없음" : song);
        AddDetail("인코딩", _document.Encoding.DisplayName);
        AddDetail("줄바꿈", _document.NewlineName == "CRLF" ? "CRLF (Windows)" : _document.NewlineName);
        AddDetail("기본 BPM", _document.BaseBpm?.ToString(CultureInfo.InvariantCulture) ?? "미정의");
        AddDetail("기존 BPM 이벤트", $"{_document.ExistingBpmEvents.Count}개 · 마지막 {_document.MaxMeasure:000}마디");
    }

    private void AddDetail(string name, string value)
    {
        var item = new ListViewItem(name);
        item.SubItems.Add(value);
        _fileDetails.Items.Add(item);
    }

    private void RenderIdle()
    {
        _prepared = null;
        _saveButton.Enabled = false;
        SetValidationBanner("i", "BMS 파일을 선택하세요.", "기존 #03/#08 BPM 이벤트와 구간 겹침을 검사합니다.", Color.FromArgb(240, 244, 247), Color.FromArgb(56, 119, 157));
        _messageList.Items.Clear();
        _previewGrid.Rows.Clear();
        _eventMetric.Text = "0";
        _timeMetric.Text = "0.000 s";
        _errorMetric.Text = "0.000 ms";
        _graph.Rows = Array.Empty<PreviewRow>();
        _graphCaption.Text = "파일을 불러오면 표시됩니다.";
        _outputHint.Text = "원본을 유지하고 파일명_linear_bpm.bms로 출력합니다.";
    }

    private void RenderValidation(PreparedConversion prepared)
    {
        var errors = prepared.Errors.Count + prepared.Conflicts.Count;
        if (errors > 0)
            SetValidationBanner("×", $"{errors}개의 오류 또는 BPM 충돌이 있습니다.", "기존 BPM 이벤트는 자동으로 덮어쓰지 않습니다.", Color.FromArgb(255, 239, 236), Color.FromArgb(177, 55, 43));
        else if (prepared.Warnings.Count > 0)
            SetValidationBanner("!", $"변환 가능 · 확인할 경고 {prepared.Warnings.Count}개", $"새 BPM 이벤트 {prepared.Events.Count}개를 생성합니다.", Color.FromArgb(255, 248, 222), Color.FromArgb(185, 132, 29));
        else
            SetValidationBanner("✓", "충돌 없음 — 변환할 수 있습니다.", $"새 확장 BPM 정의 {prepared.Definitions.Count}개와 이벤트 {prepared.Events.Count}개를 생성합니다.", Color.FromArgb(237, 247, 232), Color.FromArgb(69, 143, 51));

        _messageList.BeginUpdate();
        _messageList.Items.Clear();
        foreach (var error in prepared.Errors)
            _messageList.Items.Add($"×  {error}");
        foreach (var conflict in prepared.Conflicts)
            _messageList.Items.Add($"×  {conflict.Message}");
        foreach (var warning in prepared.Warnings)
            _messageList.Items.Add($"!  {warning}");
        _messageList.EndUpdate();
    }

    private void RenderPreview(PreparedConversion prepared)
    {
        _eventMetric.Text = prepared.Events.Count.ToString(CultureInfo.InvariantCulture);
        _timeMetric.Text = $"{prepared.TotalExactSeconds:F3} s";
        _errorMetric.Text = $"{Signed(prepared.TotalErrorMilliseconds, 3)} ms";
        _errorMetric.ForeColor = Math.Abs(prepared.TotalErrorMilliseconds) <= 1d
            ? Color.FromArgb(39, 101, 35)
            : Math.Abs(prepared.TotalErrorMilliseconds) <= 10d
                ? Color.FromArgb(139, 101, 13)
                : Color.FromArgb(163, 40, 32);

        _previewGrid.SuspendLayout();
        _previewGrid.Rows.Clear();
        foreach (var row in prepared.Rows.Take(1_000))
        {
            var position = _perBeat.Checked ? $"{row.Measure:000} · {row.Slot + 1}박" : $"{row.Measure:000}";
            _previewGrid.Rows.Add(
                row.SegmentIndex + 1,
                position,
                row.BpmStart.ToString("F3", CultureInfo.InvariantCulture),
                row.BpmEnd.ToString("F3", CultureInfo.InvariantCulture),
                LinearBpmEngine.FormatBpm(row.OutputBpm, prepared.Options.DecimalPlaces),
                $"{row.ApproximateSeconds * 1000d:F3} ms",
                $"{Signed(row.GlobalCumulativeMilliseconds, 3)} ms");
        }
        _previewGrid.ResumeLayout();

        _graphCaption.Text = $"{prepared.Rows.Count}개 시간 구간 · 오차 {Signed(prepared.TotalErrorMilliseconds, 3)} ms";
        var outputName = BmsWriter.OutputFileName(_document?.FileName ?? "chart.bms");
        _outputHint.Text = prepared.Rows.Count > 1_000
            ? $"표는 처음 1,000개만 표시합니다. 출력: {outputName}"
            : $"원본은 변경하지 않습니다. 출력: {outputName}";
    }

    private void RenderAnalysisError(string message)
    {
        SetValidationBanner("×", "입력값을 해석하지 못했습니다.", message, Color.FromArgb(255, 239, 236), Color.FromArgb(177, 55, 43));
        _messageList.Items.Clear();
        _messageList.Items.Add($"×  {message}");
        SetStatus("표의 숫자 입력값을 확인하세요.");
    }

    private void SetValidationBanner(string icon, string title, string subtitle, Color background, Color accent)
    {
        _validationBanner.BackColor = background;
        _validationIcon.Text = icon;
        _validationIcon.ForeColor = Color.White;
        _validationIcon.BackColor = accent;
        _validationTitle.Text = title;
        _validationSubtitle.Text = subtitle;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            "BMS Linear BPM Changer Demo\r\n\r\n선형 BPM 곡선을 마디 또는 박자 단위의 확장 BPM 이벤트로 근사합니다.\r\n파일의 비 ASCII 바이트와 줄바꿈은 재인코딩하지 않고 그대로 보존합니다.",
            "프로그램 정보",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static GroupBox NewGroup(string text) => new() { Text = text, Dock = DockStyle.Fill, Padding = new Padding(7) };

    private static Control SettingPanel(string title, params RadioButton[] buttons)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2, 1, 0, 0) };
        panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 3) });
        foreach (var button in buttons)
        {
            button.Margin = new Padding(0, 0, 0, 2);
            panel.Controls.Add(button);
        }
        return panel;
    }

    private static Label LegendLabel(string text, Color color) => new() { Text = text, ForeColor = color, AutoSize = true, Margin = new Padding(0, 4, 14, 0), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

    private static Label MetricValue() => new() { Text = "0", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12f), ForeColor = Color.FromArgb(23, 62, 92), TextAlign = ContentAlignment.TopLeft };

    private static Control MetricPanel(string caption, Label value)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(6, 3, 4, 2) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Color.FromArgb(85, 104, 117) }, 0, 0);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private static DataGridViewTextBoxColumn TextColumn(string header, string property, int width, bool readOnly = false) => new()
    {
        HeaderText = header,
        DataPropertyName = property,
        Width = width,
        ReadOnly = readOnly,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private static string Signed(double value, int digits)
    {
        if (Math.Abs(value) < 0.5d * Math.Pow(10, -digits))
            value = 0d;
        return value > 0 ? $"+{value.ToString($"F{digits}", CultureInfo.InvariantCulture)}" : value.ToString($"F{digits}", CultureInfo.InvariantCulture);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024d:F1} KB";
        return $"{bytes / 1024d / 1024d:F2} MB";
    }
}
