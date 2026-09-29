using System.Diagnostics;
using System.Reflection;

namespace Itda.Launcher;

/// <summary>
/// 트레이 아이콘과 진행 창을 가진 앱 본체. 시작 과정을 돌리고, 서버가 켜져 있는 동안 트레이에 남는다.
/// 종료하면 Job Object를 닫아 서버(와 직접 띄운 ollama serve)를 함께 끝낸다.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private enum State { Starting, Running, Stopping }

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly JobObject _job = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly LauncherConfig _config = LauncherConfig.Load();
    private readonly Ollama _ollama;
    private readonly Server _server;
    private readonly ProgressForm _form;
    private readonly NotifyIcon _tray;
    private State _state = State.Starting;

    public TrayContext()
    {
        _ollama = new Ollama(_http, _job);
        _server = new Server(_http, _job);

        var icon = LoadIcon(Size.Empty);
        _form = new ProgressForm(icon, LoadImage("itda-256.png"));
        // FormClosing 안에서 다시 Close하지 않도록 한 박자 뒤에 끝낸다
        _form.CancelRequested += (_, _) => _form.BeginInvoke(Quit);
        _form.Shown += async (_, _) => await StartAsync();

        var menu = new ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => Activate());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ConfirmQuit());
        _tray = new NotifyIcon
        {
            Icon = LoadIcon(SystemInformation.SmallIconSize),
            Text = "잇다 (시작 중)",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => Activate();
        _tray.BalloonTipClicked += (_, _) => Activate();

        _form.Show();
    }

    /// <summary>트레이 "열기", 아이콘 더블클릭, 또는 바로가기를 한 번 더 눌렀을 때.</summary>
    public void Activate()
    {
        switch (_state)
        {
            case State.Running:
                OpenBrowser();
                break;
            case State.Starting:
                _form.Show();
                if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
                _form.Activate();
                break;
        }
    }

    private async Task StartAsync()
    {
        Log.Info($"런처 시작 (설치 폴더 {AppPaths.AppDir})");
        var progress = new Progress<StartupStatus>(s =>
        {
            if (_state == State.Starting) _form.UpdateStatus(s);
        });
        var bootstrapper = new Bootstrapper(_config, _ollama, _server);
        try
        {
            var result = await Task.Run(() => bootstrapper.RunAsync(progress, _cts.Token));
            if (_state == State.Stopping) return;

            OpenBrowser();
            if (result == StartupResult.AlreadyRunning)
            {
                Quit();
                return;
            }

            _state = State.Running;
            _server.Process!.Exited += (_, _) => _form.BeginInvoke(OnServerExited);
            _form.AllowClose = true;
            _form.Hide();
            _tray.Text = "잇다 (실행 중)";
            _tray.ShowBalloonTip(5000, "잇다가 켜졌어요",
                "브라우저에서 잇다를 쓰세요. 끄려면 이 아이콘을 오른쪽 클릭 → 종료.", ToolTipIcon.Info);
        }
        catch (OperationCanceledException)
        {
            Log.Info("사용자가 시작을 취소했습니다.");
            Quit();
        }
        catch (UserFacingException ex)
        {
            Log.Error("시작 실패: " + ex.Message);
            ShowError(ex.Message);
            Quit();
        }
        catch (Exception ex)
        {
            Log.Error("시작 중 예상하지 못한 오류", ex);
            ShowError($"잇다를 시작하지 못했습니다.\n\n{ex.Message}\n\n로그: {Log.FilePath}");
            Quit();
        }
    }

    private void OnServerExited()
    {
        if (_state == State.Stopping) return;
        Log.Error($"서버가 예기치 않게 종료됨 (종료 코드 {_server.Process?.ExitCode})");
        ShowError("잇다 서버가 멈췄습니다. 바탕화면의 잇다 아이콘을 눌러 다시 실행해 주세요.\n\n" +
                  $"로그: {_server.Log.Path}");
        Quit();
    }

    private void ConfirmQuit()
    {
        if (_state == State.Running)
        {
            var answer = MessageBox.Show("잇다를 끌까요?\n브라우저의 잇다 화면도 더 이상 쓸 수 없게 됩니다.", "잇다",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }
        Quit();
    }

    private void Quit()
    {
        if (_state == State.Stopping) return;
        _state = State.Stopping;
        Log.Info("런처 종료");
        _cts.Cancel();
        _tray.Visible = false;
        _job.Dispose(); // 서버·직접 띄운 ollama serve·등록 중인 ollama create를 함께 끝냄
        _form.AllowClose = true;
        _form.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _job.Dispose();
            _tray.Dispose();
            _form.Dispose();
            _http.Dispose();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ShowError(string message)
    {
        var owner = _form.Visible ? _form : null;
        MessageBox.Show(owner, message, "잇다", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.ServerUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("브라우저를 열지 못했습니다", ex);
            MessageBox.Show($"브라우저를 열지 못했습니다. 브라우저 주소창에 직접 입력해 주세요:\n\n{AppPaths.ServerUrl}",
                "잇다", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static Stream Resource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"리소스 없음: {name}");

    private static Icon LoadIcon(Size size)
    {
        using var s = Resource("itda.ico");
        return size.IsEmpty ? new Icon(s) : new Icon(s, size);
    }

    private static Image LoadImage(string name)
    {
        using var s = Resource(name);
        using var image = Image.FromStream(s);
        return new Bitmap(image);
    }
}
