namespace Itda.Launcher;

/// <summary>시작 중 진행 창: 로고, 현재 단계, 진행 막대, 부가 설명, 취소 버튼.</summary>
internal sealed class ProgressForm : Form
{
    private static readonly Color Ink = Color.FromArgb(0x1F, 0x3A, 0x5F);
    private static readonly Color Muted = Color.FromArgb(0x6B, 0x72, 0x80);

    private readonly Label _title;
    private readonly Label _detail;
    private readonly ProgressBar _bar;

    /// <summary>사용자가 취소 버튼이나 창 닫기를 눌러 시작을 멈추기로 했을 때.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>true면 닫기를 막지 않는다 (시작이 끝났거나 오류로 끝낼 때).</summary>
    public bool AllowClose { get; set; }

    public ProgressForm(Icon icon, Image logo)
    {
        Text = "잇다";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(480, 200);
        ShowInTaskbar = true;

        var picture = new PictureBox
        {
            Image = logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(24, 36),
            Size = new Size(96, 96),
        };

        _title = new Label
        {
            Font = UiFonts.Get(15F),
            ForeColor = Ink,
            UseCompatibleTextRendering = true,
            AutoEllipsis = true,
            Location = new Point(140, 30),
            Size = new Size(316, 36),
            Text = "잇다를 시작하고 있어요",
        };

        _bar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Location = new Point(142, 74),
            Size = new Size(314, 14),
        };

        _detail = new Label
        {
            Font = UiFonts.Get(10.5F),
            ForeColor = Muted,
            UseCompatibleTextRendering = true,
            AutoEllipsis = true,
            Location = new Point(140, 98),
            Size = new Size(316, 44),
        };

        var cancel = new Button
        {
            Text = "취소",
            Font = UiFonts.Get(10.5F),
            UseCompatibleTextRendering = true,
            Location = new Point(366, 152),
            Size = new Size(90, 32),
        };
        cancel.Click += (_, _) => Close();

        Controls.AddRange([picture, _title, _bar, _detail, cancel]);
        CancelButton = cancel;
    }

    public void UpdateStatus(StartupStatus status)
    {
        _title.Text = status.Title;
        _detail.Text = status.Detail;
        if (status.Percent is { } pct)
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = Math.Clamp(pct, 0, 100);
        }
        else if (_bar.Style != ProgressBarStyle.Marquee)
        {
            _bar.Style = ProgressBarStyle.Marquee;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            var answer = MessageBox.Show(this, "잇다 시작을 멈출까요?", "잇다",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes) CancelRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnFormClosing(e);
    }
}
