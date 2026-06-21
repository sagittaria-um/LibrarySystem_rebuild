namespace LibrarySystem.Desktop;

public partial class Form1 : Form
{
    private readonly HttpClient _http = new();
    private System.Diagnostics.Process? _serverProcess;
    private const string AppUrl = "http://127.0.0.1:5297";

    public Form1()
    {
        InitializeComponent();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await EnsureServerAsync();
        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.Navigate(AppUrl);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        try
        {
            if (_serverProcess is { HasExited: false })
            {
                _serverProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Closing the desktop shell should not be blocked by process cleanup.
        }
    }

    private async Task EnsureServerAsync()
    {
        if (await IsServerReadyAsync())
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        var packagedServer = Path.Combine(baseDir, "server", "LibrarySystem.Api.exe");
        var devServer = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "server", "bin", "Debug", "net8.0", "LibrarySystem.Api.exe"));
        var serverExe = File.Exists(packagedServer) ? packagedServer : devServer;

        if (!File.Exists(serverExe))
        {
            MessageBox.Show("未找到后端服务程序，请先构建 server 项目。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = serverExe,
            WorkingDirectory = Path.GetDirectoryName(serverExe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["ASPNETCORE_URLS"] = AppUrl;

        _serverProcess = System.Diagnostics.Process.Start(startInfo);

        for (var i = 0; i < 40; i++)
        {
            if (await IsServerReadyAsync())
            {
                return;
            }

            await Task.Delay(500);
        }

        MessageBox.Show("后端服务启动超时，请确认 SQL Server 容器已启动。", "启动提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async Task<bool> IsServerReadyAsync()
    {
        try
        {
            using var response = await _http.GetAsync($"{AppUrl}/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
