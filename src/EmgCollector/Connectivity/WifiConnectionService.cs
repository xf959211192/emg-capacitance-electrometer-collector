using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace EmgCollector.Connectivity;

public enum BoardWifiKind
{
    None,
    Emg,
    Capacitance,
    Electrostatic
}

public static class WifiConnectionService
{
    private static readonly TimeSpan Ipv4AssignmentTimeout = TimeSpan.FromSeconds(30);
    public const string EmgSsid = "WNZL_Emg";
    public const string CapacitanceSsid = "ESP32_Pcap35";
    public const string ElectrostaticSsid = "WNZL_Emeter8";
    public const string BoardSsid = EmgSsid;
    private const string EmgDefaultPassword = "Eemg1234";
    private const string CapacitanceDefaultPassword = "pcap1234";
    private const string ElectrostaticDefaultPassword = "emeter1234";
    private static readonly Regex ProfileRegex = new(
        @"(?mi)^\s*(?:Profile|配置文件)\s*:\s*(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static WifiConnectionService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<string?> GetCurrentProfileAsync()
    {
        NetshResult result = await RunNetshAsync(["wlan", "show", "interfaces"]);
        Match match = ProfileRegex.Match(result.Output);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public static BoardWifiKind IdentifyBoardWifi(string? profileName)
    {
        if (string.Equals(profileName, EmgSsid, StringComparison.Ordinal))
        {
            return BoardWifiKind.Emg;
        }

        if (string.Equals(profileName, CapacitanceSsid, StringComparison.Ordinal))
        {
            return BoardWifiKind.Capacitance;
        }

        if (string.Equals(profileName, ElectrostaticSsid, StringComparison.Ordinal))
        {
            return BoardWifiKind.Electrostatic;
        }

        return BoardWifiKind.None;
    }

    public static async Task<bool> IsBoardConnectedAsync() =>
        await IsBoardConnectedAsync(EmgSsid);

    public static async Task<bool> IsBoardConnectedAsync(string ssid) =>
        string.Equals(await GetCurrentProfileAsync(), ssid, StringComparison.Ordinal);

    public static Task<WifiConnectionResult> ConnectBoardAsync(CancellationToken cancellationToken = default) =>
        ConnectBoardAsync(EmgSsid, null, cancellationToken);

    public static Task<WifiConnectionResult> ConnectBoardAsync(string ssid, CancellationToken cancellationToken = default) =>
        ConnectBoardAsync(ssid, null, cancellationToken);

    public static async Task<WifiConnectionResult> ConnectBoardAsync(
        string ssid,
        string? suppliedPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? originalProfile = await GetCurrentProfileAsync();
        if (string.Equals(originalProfile, ssid, StringComparison.Ordinal))
        {
            return new WifiConnectionResult(true, originalProfile, GetWirelessIpv4Address(), "板卡 Wi-Fi 已连接");
        }

        string? connectionPassword = string.IsNullOrEmpty(suppliedPassword)
            ? string.Equals(ssid, EmgSsid, StringComparison.Ordinal)
                ? EmgDefaultPassword
                : string.Equals(ssid, CapacitanceSsid, StringComparison.Ordinal)
                    ? CapacitanceDefaultPassword
                    : string.Equals(ssid, ElectrostaticSsid, StringComparison.Ordinal)
                        ? ElectrostaticDefaultPassword
                        : null
            : suppliedPassword;
        bool profileExists = await ProfileExistsAsync(ssid);
        cancellationToken.ThrowIfCancellationRequested();
        if (!profileExists)
        {
            if (connectionPassword is null)
            {
                return new WifiConnectionResult(
                    false,
                    originalProfile,
                    null,
                    $"Windows 中没有保存 {ssid} 配置，请在软件中填写板卡密码后重试");
            }

            bool installed = await InstallProfileAsync(ssid, connectionPassword, cancellationToken);
            if (!installed)
            {
                return new WifiConnectionResult(
                    false,
                    originalProfile,
                    null,
                    $"无法创建 {ssid} 的 Windows Wi-Fi 配置");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RunNetshAsync(["wlan", "disconnect"]);
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        bool connected = await TryConnectWithRetriesAsync(ssid, 2, cancellationToken);

        // 已保存配置可能仍带有旧密码；首次连接失败时用板卡默认凭据刷新后重试。
        if (!connected && profileExists && connectionPassword is not null &&
            await InstallProfileAsync(ssid, connectionPassword, cancellationToken))
        {
            connected = await TryConnectWithRetriesAsync(ssid, 2, cancellationToken);
        }

        if (!connected)
        {
            return new WifiConnectionResult(false, originalProfile, null, $"无法连接 {ssid}");
        }

        string? ipv4 = null;
        // 板卡 DHCP 在部分电脑上建立较慢；SSID 已连接后留足时间等待协议地址。
        DateTime deadline = DateTime.Now.Add(Ipv4AssignmentTimeout);
        while (DateTime.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ipv4 = GetWirelessIpv4Address();
            if (!string.IsNullOrWhiteSpace(ipv4) && !ipv4.StartsWith("169.254.", StringComparison.Ordinal))
            {
                break;
            }
            await Task.Delay(400, cancellationToken);
        }

        if (ipv4 != "192.168.4.2")
        {
            return new WifiConnectionResult(
                false,
                originalProfile,
                ipv4,
                $"已连接 {ssid}，但电脑地址为 {ipv4 ?? "未分配"}，协议要求 192.168.4.2");
        }

        return new WifiConnectionResult(true, originalProfile, ipv4, $"已连接 {ssid}，本机地址 {ipv4}");
    }

    public static async Task<bool> RestoreProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return false;
        }

        await RunNetshAsync(["wlan", "disconnect"]);
        await Task.Delay(800, cancellationToken);
        await RunNetshAsync(["wlan", "connect", $"name={profileName}"]);

        DateTime deadline = DateTime.Now.AddSeconds(15);
        while (DateTime.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(await GetCurrentProfileAsync(), profileName, StringComparison.Ordinal))
            {
                return true;
            }
            await Task.Delay(400, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> TryConnectWithRetriesAsync(string ssid, int attempts, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunNetshAsync(["wlan", "show", "networks", "mode=bssid"]);
            await RunNetshAsync(["wlan", "connect", $"name={ssid}", $"ssid={ssid}"]);

            DateTime deadline = DateTime.Now.AddSeconds(8);
            while (DateTime.Now < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsBoardConnectedAsync(ssid))
                {
                    return true;
                }
                await Task.Delay(300, cancellationToken);
            }
        }

        return false;
    }

    private static async Task<bool> ProfileExistsAsync(string ssid)
    {
        NetshResult result = await RunNetshAsync(["wlan", "show", "profile", $"name={ssid}"]);
        return result.ExitCode == 0 && result.Output.Contains(ssid, StringComparison.Ordinal);
    }

    private static async Task<bool> InstallProfileAsync(string ssid, string password, CancellationToken cancellationToken)
    {
        string profilePath = Path.Combine(Path.GetTempPath(), $"signal-wifi-{Guid.NewGuid():N}.xml");
        string profileXml = BuildProfileXml(ssid, password);

        try
        {
            await File.WriteAllTextAsync(profilePath, profileXml, new UTF8Encoding(false), cancellationToken);
            NetshResult result = await RunNetshAsync(
                ["wlan", "add", "profile", $"filename={profilePath}", "user=current"]);
            return result.ExitCode == 0 && await ProfileExistsAsync(ssid);
        }
        finally
        {
            // 配置文件内含默认密钥，使用后立即覆盖并删除，避免留在临时目录。
            if (File.Exists(profilePath))
            {
                long length = new FileInfo(profilePath).Length;
                if (length > 0 && length <= int.MaxValue)
                {
                    await File.WriteAllBytesAsync(profilePath, new byte[(int)length], CancellationToken.None);
                }
                File.Delete(profilePath);
            }
        }
    }

    private static string BuildProfileXml(string profileSsid, string profilePassword)
    {
        string ssid = SecurityElement.Escape(profileSsid) ?? profileSsid;
        string password = SecurityElement.Escape(profilePassword) ?? profilePassword;
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>{ssid}</name>
              <SSIDConfig>
                <SSID><name>{ssid}</name></SSID>
                <nonBroadcast>false</nonBroadcast>
              </SSIDConfig>
              <connectionType>ESS</connectionType>
              <connectionMode>manual</connectionMode>
              <MSM>
                <security>
                  <authEncryption>
                    <authentication>WPA2PSK</authentication>
                    <encryption>AES</encryption>
                    <useOneX>false</useOneX>
                  </authEncryption>
                  <sharedKey>
                    <keyType>passPhrase</keyType>
                    <protected>false</protected>
                    <keyMaterial>{password}</keyMaterial>
                  </sharedKey>
                </security>
              </MSM>
            </WLANProfile>
            """;
    }

    private static string? GetWirelessIpv4Address()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                              network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .FirstOrDefault();
    }

    private static async Task<NetshResult> RunNetshAsync(IReadOnlyCollection<string> arguments)
    {
        Encoding outputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Windows Wi-Fi 命令");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new NetshResult(process.ExitCode, output, error);
    }

    private sealed record NetshResult(int ExitCode, string Output, string Error);
}

public sealed record WifiConnectionResult(
    bool Success,
    string? OriginalProfile,
    string? Ipv4Address,
    string Message);
