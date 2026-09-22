using EmgCollector.Connectivity;

string? originalProfile = await WifiConnectionService.GetCurrentProfileAsync();
Console.WriteLine($"测试前 Wi-Fi：{originalProfile ?? "未连接"}");
if (args.Contains("--status", StringComparer.OrdinalIgnoreCase))
{
    return originalProfile is null ? 3 : 0;
}

WifiConnectionResult result = await WifiConnectionService.ConnectBoardAsync();
Console.WriteLine(result.Message);
if (!result.Success || result.Ipv4Address != "192.168.4.2")
{
    if (!string.IsNullOrWhiteSpace(originalProfile) && originalProfile != WifiConnectionService.BoardSsid)
    {
        await WifiConnectionService.RestoreProfileAsync(originalProfile);
    }
    return 1;
}

if (!string.IsNullOrWhiteSpace(originalProfile) && originalProfile != WifiConnectionService.BoardSsid)
{
    bool restored = await WifiConnectionService.RestoreProfileAsync(originalProfile);
    Console.WriteLine(restored ? $"已恢复：{originalProfile}" : $"恢复失败：{originalProfile}");
    return restored ? 0 : 2;
}

return 0;
