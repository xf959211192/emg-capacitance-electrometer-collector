using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace EmgCollector.Security;

public static class FirewallPermissionService
{
    public const string ConfigureArgument = "--configure-firewall";
    private static readonly int[] SupportedPorts = [8060, 8080];

    public static async Task<bool> IsConfiguredAsync()
    {
        try
        {
            foreach (int port in SupportedPorts)
            {
                if (!await IsRuleConfiguredAsync(port))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> RequestPermissionAsync(IWin32Window owner)
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            MessageBox.Show(owner, "无法确定当前程序路径。", "防火墙权限", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                Verb = "runas"
            };
            startInfo.ArgumentList.Add(ConfigureArgument);
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动管理员配置进程");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                MessageBox.Show(owner, "防火墙权限配置未完成。", "防火墙权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            bool configured = await IsConfiguredAsync();
            if (!configured)
            {
                MessageBox.Show(owner, "管理员进程已结束，但没有检测到有效规则。", "防火墙权限", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return configured;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show(owner, "已取消管理员授权，真实板卡数据可能被 Windows 防火墙拦截。", "防火墙权限", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"请求防火墙权限失败：{ex.Message}", "防火墙权限", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    public static int ConfigureElevated()
    {
        try
        {
            foreach (int port in SupportedPorts)
            {
                DeleteManagedRules(port);
                AddRule(port);

                string verification = RunNetsh([
                    "advfirewall", "firewall", "show", "rule", $"name={GetRuleName(port)}", "verbose"
                ]);
                if (!ContainsExpectedRule(verification, port))
                {
                    throw new InvalidOperationException($"UDP {port} 规则创建后校验失败");
                }
            }

            MessageBox.Show(
                "防火墙权限配置成功。\n\n" +
                "已同时允许专用网络和公用网络。\n" +
                "仅开放板卡地址 192.168.4.1 使用的 UDP 8060、8080 端口。",
                "防火墙权限",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"配置失败：{ex.Message}", "防火墙权限", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static async Task<bool> IsRuleConfiguredAsync(int port)
    {
        string ruleName = GetRuleName(port);
        ProcessResult result = await RunNetshAsync([
            "advfirewall", "firewall", "show", "rule", $"name={ruleName}", "verbose"
        ]);
        return result.ExitCode == 0 && ContainsExpectedRule(result.Output, port);
    }

    private static void DeleteManagedRules(int port)
    {
        // 同时清理旧版仅专用网络规则，避免重复规则造成误判。
        foreach (string ruleName in new[] { GetLegacyRuleName(port), GetRuleName(port) })
        {
            RunNetsh([
                "advfirewall", "firewall", "delete", "rule", $"name={ruleName}"
            ], allowFailure: true);
        }
    }

    private static void AddRule(int port)
    {
        RunNetsh([
            "advfirewall", "firewall", "add", "rule",
            $"name={GetRuleName(port)}",
            "dir=in",
            "action=allow",
            "enable=yes",
            "profile=private,public",
            "protocol=UDP",
            $"localport={port}",
            "remoteip=192.168.4.1",
            $"description=Allow sensor board 192.168.4.1 to send UDP {port} data on private and public networks"
        ]);
    }

    private static bool ContainsExpectedRule(string output, int port) =>
        output.Contains(GetRuleName(port), StringComparison.OrdinalIgnoreCase) &&
        output.Contains(port.ToString(), StringComparison.Ordinal) &&
        output.Contains("192.168.4.1", StringComparison.Ordinal);

    private static string GetRuleName(int port) => $"Signal Collector v2 UDP {port}";

    private static string GetLegacyRuleName(int port) => $"Signal Collector UDP {port}";

    private static string RunNetsh(IReadOnlyCollection<string> arguments, bool allowFailure = false)
    {
        ProcessResult result = RunNetshAsync(arguments).GetAwaiter().GetResult();
        if (!allowFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Windows 防火墙命令执行失败：{result.Error}{result.Output}");
        }

        return result.Output;
    }

    private static async Task<ProcessResult> RunNetshAsync(IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Windows 防火墙命令");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
