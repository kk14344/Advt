namespace AandmAuto
{
	internal static class BuildEdition
	{
		internal const string AppVersion = "1.0.0";
#if TEST_PVP_EDITION
		internal static readonly bool IsLowLoad = false;
		internal static readonly bool IsTestPvp = true;
		internal const string UpdateChannel = "testpvp";
		internal const string EditionDisplayName = "测试服 PvP 专项版";
		internal const string DefaultExeName = "冒险与挖矿自动助手-测试服PvP版.exe";
		internal const string WindowTitle = "冒险与挖矿 · 测试服 PvP 专项版 " + AppVersion;
		internal const string HeaderSubtitle = "测试服 PvP 专项版 v" + AppVersion;
		internal const string TrayTitle = "冒险与挖矿助手 · 测试服 PvP 专项版 " + AppVersion;
		internal const int TaskMinStepMs = 15;
		internal const string StartupBanner = "当前运行：测试服 PvP 专项版 v" + AppVersion + "（默认静音 · 热连19341 · 拖图换阵 · 在途=1实测）";
#elif LOW_LOAD_EDITION
		internal static readonly bool IsLowLoad = true;
		internal static readonly bool IsTestPvp = false;
		internal const string UpdateChannel = "lowload";
		internal const string EditionDisplayName = "正式服低负载版";
		internal const string DefaultExeName = "冒险与挖矿自动助手-低负载版.exe";
		internal const string WindowTitle = "冒险与挖矿 · 冒险者营地 " + AppVersion + " · 低负载版";
		internal const string HeaderSubtitle = "自动助手 v" + AppVersion + " · 低负载";
		internal const string TrayTitle = "冒险与挖矿自动助手 " + AppVersion + " · 低负载版";
		internal const int TaskMinStepMs = 450;
		internal const string StartupBanner = "当前运行：低负载护服版（挖矿间隔 0.80 秒 · 任务步进 0.45 秒，炼化小游戏保持原速）";
#else
		internal static readonly bool IsLowLoad = false;
		internal static readonly bool IsTestPvp = false;
		internal const string UpdateChannel = "official";
		internal const string EditionDisplayName = "正式服优化版";
		internal const string DefaultExeName = "冒险与挖矿自动助手-优化版.exe";
		internal const string WindowTitle = "冒险与挖矿 · 冒险者营地 " + AppVersion;
		internal const string HeaderSubtitle = "自动助手 v" + AppVersion;
		internal const string TrayTitle = "冒险与挖矿自动助手 " + AppVersion;
		internal const int TaskMinStepMs = 30;
		internal const string StartupBanner = "";
#endif
	}
}
