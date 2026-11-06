using System.Diagnostics;

static class EldenOfflineGuard
{
    public static void Check()
    {
        foreach (string name in new[] { "EasyAntiCheat", "EasyAntiCheat_EOS", "start_protected_game" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0)
                    throw new IOException("法环采集包仅限离线单机。检测到反作弊或受保护启动器，请退出游戏并使用你已配置的离线启动方式；本包不会停用或绕过反作弊。");
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
}
