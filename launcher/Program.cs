namespace Itda.Launcher;

internal static class Program
{
    // 사용자 세션마다 하나만 실행 (Local\). 이름을 바꾸면 installer/itda.iss의 설명도 같이 본다.
    private const string MutexName = @"Local\ITDA-Launcher-93b97b6a-3634-48c4-90eb-52d789b3205d";
    private const string ActivateEventName = @"Local\ITDA-Launcher-Activate-93b97b6a-3634-48c4-90eb-52d789b3205d";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // 이미 실행 중: 첫 번째 런처에게 알린다. 켜져 있으면 브라우저를, 시작 중이면 진행 창을 보여 준다.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Log.Error("처리되지 않은 오류", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("처리되지 않은 오류", e.ExceptionObject as Exception);

        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        using var context = new TrayContext();
        var ui = SynchronizationContext.Current!; // TrayContext가 폼을 만들면서 WinForms 컨텍스트가 설치됨
        var listener = new Thread(() =>
        {
            while (activate.WaitOne())
                ui.Post(_ => context.Activate(), null);
        }) { IsBackground = true, Name = "activate-listener" };
        listener.Start();

        Application.Run(context);
        GC.KeepAlive(mutex);
    }
}
