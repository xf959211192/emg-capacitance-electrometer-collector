using System.Diagnostics;
using System.Text;

ApplicationConfiguration.Initialize();

const string ruleName = "EMG采集软件 UDP 8060";
string directory = AppContext.BaseDirectory;
string emgProgram = Path.GetFullPath(Path.Combine(directory, "EMG采集软件.exe"));
string reportPath = Path.Combine(directory, "防火墙规则检查.txt");

try
{
    if (!File.Exists(emgProgram))
    {
        throw new FileNotFoundException("配置工具必须与 EMG采集软件.exe 放在同一文件夹。", emgProgram);
    }

    RunNetsh([
        "advfirewall", "firewall", "delete", "rule", $"name={ruleName}"
    ], allowFailure: true);

    RunNetsh([
        "advfirewall", "firewall", "add", "rule",
        $"name={ruleName}",
        "dir=in",
        "action=allow",
        "enable=yes",
        "profile=private",
        "protocol=UDP",
        "localport=8060",
        "remoteip=192.168.4.1",
        $"program={emgProgram}"
    ]);

    string verification = RunNetsh([
        "advfirewall", "firewall", "show", "rule", $"name={ruleName}", "verbose"
    ]);
    File.WriteAllText(reportPath, verification, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    MessageBox.Show(
        $"配置成功。\n\n程序：{emgProgram}\n来源：192.168.4.1\n协议：UDP\n端口：8060\n网络：专用网络\n\n检查结果已保存到：\n{reportPath}",
        "EMG 防火墙配置",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
}
catch (Exception ex)
{
    MessageBox.Show(
        $"配置失败：\n{ex.Message}",
        "EMG 防火墙配置",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    Environment.ExitCode = 1;
}

static string RunNetsh(IEnumerable<string> arguments, bool allowFailure = false)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 netsh.exe");
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (!allowFailure && process.ExitCode != 0)
    {
        throw new InvalidOperationException($"netsh 执行失败（{process.ExitCode}）：{error}{output}");
    }

    return output;
}
