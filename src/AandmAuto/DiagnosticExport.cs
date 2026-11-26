using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class DiagnosticExportResult
	{
		public string ZipPath = "";
		public List<string> IncludedFiles = new List<string>();
		public long ZipBytes;
	}

	/// <summary>
	/// 严格白名单模式的一键脱敏诊断包导出器：
	/// 绝不读取或打包 account.dat、accounts.json、WebView2/EBWebView Cookie 缓存或任何账号凭据，
	/// 并对文本内容中的用户目录名与潜在 Token/密钥做脱敏替换。
	/// </summary>
	internal static class DiagnosticExport
	{
		private static readonly Regex UsersPathRegex = new Regex(@"([A-Za-z]:[\\/]+Users[\\/]+)([^\\/""'\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex UsersEscapedPathRegex = new Regex(@"([A-Za-z]:\\\\Users\\\\)([^\\""'\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex TokenRegex = new Regex(@"\b(ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|gho_[A-Za-z0-9]{20,})\b", RegexOptions.Compiled);
		private static readonly Regex BearerRegex = new Regex(@"(Authorization\s*[:=]\s*Bearer\s+)[^\s""'\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex PasswordJsonRegex = new Regex(@"(""(?:password|passwd|token|cookie|secret)""\s*:\s*"")[^""]*("")", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		internal static string DefaultFileName()
		{
			return "AandmAuto-诊断包-" + BuildEdition.UpdateChannel + "-v" + BuildEdition.AppVersion + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
		}

		internal static string SanitizeText(string text)
		{
			if (string.IsNullOrEmpty(text)) return "";
			string s = text;
			try
			{
				string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				if (!string.IsNullOrEmpty(profile))
				{
					s = ReplaceIgnoreCase(s, profile, "<USERPROFILE>");
					s = ReplaceIgnoreCase(s, profile.Replace("\\", "\\\\"), "<USERPROFILE>");
					s = ReplaceIgnoreCase(s, profile.Replace("\\", "/"), "<USERPROFILE>");
				}
				string userName = Environment.UserName;
				if (!string.IsNullOrEmpty(userName) && userName.Length >= 2)
				{
					s = Regex.Replace(s, @"([\\/])" + Regex.Escape(userName) + @"([\\/])", "$1<USER>$2", RegexOptions.IgnoreCase);
					s = Regex.Replace(s, @"(\\\\)" + Regex.Escape(userName) + @"(\\\\)", "$1<USER>$2", RegexOptions.IgnoreCase);
				}
			}
			catch
			{
			}
			s = UsersEscapedPathRegex.Replace(s, "$1<USER>");
			s = UsersPathRegex.Replace(s, "$1<USER>");
			s = TokenRegex.Replace(s, "<REDACTED_TOKEN>");
			s = BearerRegex.Replace(s, "$1<REDACTED>");
			s = PasswordJsonRegex.Replace(s, "$1<REDACTED>$2");
			return s;
		}

		private static string ReplaceIgnoreCase(string input, string search, string replacement)
		{
			if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(search)) return input;
			return Regex.Replace(input, Regex.Escape(search), replacement.Replace("$", "$$"), RegexOptions.IgnoreCase);
		}

		private static string ReadTailText(string path, int maxBytes)
		{
			using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				long len = fs.Length;
				long start = len > maxBytes ? (len - maxBytes) : 0;
				fs.Seek(start, SeekOrigin.Begin);
				int toRead = (int)(len - start);
				byte[] buf = new byte[toRead];
				int offset = 0;
				while (offset < toRead)
				{
					int n = fs.Read(buf, offset, toRead - offset);
					if (n <= 0) break;
					offset += n;
				}
				int startIdx = 0;
				if (start > 0)
				{
					// 跳过首部可能被截断的半个 UTF-8 多字节字符，直接从首个换行后开始
					while (startIdx < offset && buf[startIdx] != (byte)'\n') startIdx++;
					if (startIdx < offset && buf[startIdx] == (byte)'\n') startIdx++;
					else
					{
						startIdx = 0;
						while (startIdx < offset && (buf[startIdx] & 0xC0) == 0x80) startIdx++;
					}
				}
				string prefix = start > 0 ? "[... 已截断较早日志 ...]\r\n" : "";
				return prefix + Encoding.UTF8.GetString(buf, startIdx, offset - startIdx);
			}
		}

		private static void AddTextEntry(ZipArchive zip, string entryName, string content, List<string> included)
		{
			ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
			using (Stream s = entry.Open())
			using (var w = new StreamWriter(s, new UTF8Encoding(false)))
			{
				w.Write(SanitizeText(content ?? ""));
			}
			included.Add(entryName);
		}

		internal static DiagnosticExportResult ExportToZip(string zipPath, string dataRoot, string liveAdventureLog)
		{
			string dir = Path.GetDirectoryName(zipPath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			if (File.Exists(zipPath)) File.Delete(zipPath);

			var included = new List<string>();
			var js = new JavaScriptSerializer();

			using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
			using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, false))
			{
				// 1. Environment & version summary (no personal identifiers)
				UpdateCheckResult lastUpd = UpdateManager.LastResult;
				var summary = new Dictionary<string, object>
				{
					{ "exportedAtLocal", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
					{ "exportedAtUtc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
					{ "edition", BuildEdition.EditionDisplayName },
					{ "channel", BuildEdition.UpdateChannel },
					{ "version", BuildEdition.AppVersion },
					{ "osVersion", Environment.OSVersion.VersionString },
					{ "is64BitOs", Environment.Is64BitOperatingSystem },
					{ "is64BitProcess", Environment.Is64BitProcess },
					{ "clrVersion", Environment.Version.ToString() },
					{ "exePathMasked", SanitizeText(Application.ExecutablePath) },
					{ "dataRootMasked", SanitizeText(dataRoot ?? "") },
					{ "lastUpdateState", lastUpd != null ? lastUpd.State.ToString() : "NotChecked" },
					{ "lastUpdateLatest", lastUpd != null ? lastUpd.LatestVersion : "" },
					{ "lastUpdateError", lastUpd != null ? SanitizeText(lastUpd.ErrorMessage) : "" },
					{ "privacyNotice", "本诊断包仅包含白名单日志与脱敏配置，不包含 account.dat、accounts.json、Cookie 或任何账号密码。" }
				};
				AddTextEntry(zip, "summary.json", js.Serialize(summary), included);

				// 2. Current live adventure log
				if (!string.IsNullOrEmpty(liveAdventureLog))
				{
					AddTextEntry(zip, "adventure-log.txt", liveAdventureLog, included);
				}

				// 3. Whitelisted diagnostic files inside dataRoot
				if (!string.IsNullOrEmpty(dataRoot) && Directory.Exists(dataRoot))
				{
					string startupErr = Path.Combine(dataRoot, "startup-error.log");
					if (File.Exists(startupErr))
					{
						AddTextEntry(zip, "startup-error.log", ReadTailText(startupErr, 131072), included);
					}

					string schedErr = Path.Combine(dataRoot, "schedule-read-error.txt");
					if (File.Exists(schedErr))
					{
						AddTextEntry(zip, "schedule-read-error.txt", ReadTailText(schedErr, 65536), included);
					}

					string optsPath = Path.Combine(dataRoot, "options.json");
					if (File.Exists(optsPath))
					{
						string optsSanitized = SanitizeOptionsJson(File.ReadAllText(optsPath, Encoding.UTF8));
						AddTextEntry(zip, "options-sanitized.json", optsSanitized, included);
					}

					// Latest 2 audit log files (if present)
					string auditDir = Path.Combine(dataRoot, "audit");
					if (Directory.Exists(auditDir))
					{
						var files = new List<FileInfo>();
						foreach (string fp in Directory.GetFiles(auditDir, "*.*"))
						{
							string ext = Path.GetExtension(fp).ToLowerInvariant();
							string name = Path.GetFileName(fp).ToLowerInvariant();
							if ((ext == ".log" || ext == ".jsonl" || ext == ".txt")
								&& name.IndexOf("account", StringComparison.Ordinal) < 0
								&& name.IndexOf("cookie", StringComparison.Ordinal) < 0)
							{
								files.Add(new FileInfo(fp));
							}
						}
						files.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
						int limit = Math.Min(2, files.Count);
						for (int i = 0; i < limit; i++)
						{
							AddTextEntry(zip, "audit/" + files[i].Name, ReadTailText(files[i].FullName, 65536), included);
						}
					}
				}
			}

			return new DiagnosticExportResult
			{
				ZipPath = zipPath,
				IncludedFiles = included,
				ZipBytes = new FileInfo(zipPath).Length
			};
		}

		private static string SanitizeOptionsJson(string rawJson)
		{
			try
			{
				var js = new JavaScriptSerializer();
				var dict = js.DeserializeObject(rawJson) as Dictionary<string, object>;
				if (dict == null) return SanitizeText(rawJson);
				string[] stripKeys =
				{
					"customBgPath", "customBgLabel",
					"customBackgroundPath", "customBackgroundLabel",
					"wallpaperEngineDir", "wallpaperEngineProject",
					"account", "username", "password"
				};
				foreach (string k in stripKeys)
				{
					if (dict.ContainsKey(k)) dict[k] = "<REDACTED>";
				}
				return SanitizeText(js.Serialize(dict));
			}
			catch
			{
				return SanitizeText(rawJson);
			}
		}

		internal static void ExportInteractive(IWin32Window owner, string dataRoot, string liveAdventureLog, Action<string, System.Drawing.Color> onLog)
		{
			using (var sfd = new SaveFileDialog())
			{
				sfd.Title = "导出脱敏诊断包（不含任何账号密码与 Cookie）";
				sfd.Filter = "ZIP 压缩包|*.zip";
				sfd.FileName = DefaultFileName();
				try
				{
					string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
					if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop)) sfd.InitialDirectory = desktop;
				}
				catch
				{
				}
				if (sfd.ShowDialog(owner) != DialogResult.OK) return;

				try
				{
					DiagnosticExportResult res = ExportToZip(sfd.FileName, dataRoot, liveAdventureLog);
					if (onLog != null) onLog("已导出脱敏诊断包：" + Path.GetFileName(res.ZipPath) + "（共 " + res.IncludedFiles.Count + " 个文件，不含账号与 Cookie）。", UiTheme.Palette.BlueColor);

					DialogResult open = MessageBox.Show(
						owner,
						"脱敏诊断包导出成功！\r\n\r\n包含文件（" + res.IncludedFiles.Count + " 项）：\r\n- " + string.Join("\r\n- ", res.IncludedFiles.ToArray())
							+ "\r\n\r\n已严格排除账号密码、Cookie 与登录缓存。\r\n是否立即打开文件所在目录？",
						"导出诊断包成功",
						MessageBoxButtons.YesNo,
						MessageBoxIcon.Information);
					if (open == DialogResult.Yes)
					{
						Process.Start("explorer.exe", "/select,\"" + res.ZipPath + "\"");
					}
				}
				catch (Exception ex)
				{
					MessageBox.Show(owner, "导出诊断包失败：" + ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
		}

		/// <summary>
		/// 自动化隐私烟测（--diag-smoke &lt;outDir&gt;）：在临时目录植入模拟敏感文件，验证导出包 100% 排除凭据且正确脱敏路径/Token。
		/// </summary>
		internal static void RunSmokeSelfTest(string outDir)
		{
			Directory.CreateDirectory(outDir);
			string fakeRoot = Path.Combine(outDir, "fake-data-" + BuildEdition.UpdateChannel);
			if (Directory.Exists(fakeRoot)) Directory.Delete(fakeRoot, true);
			Directory.CreateDirectory(fakeRoot);

			File.WriteAllText(Path.Combine(fakeRoot, "account.dat"), "{\"username\":\"REAL_USER_123\",\"password\":\"TOP_SECRET_PASSWORD_999\"}", Encoding.UTF8);
			File.WriteAllText(Path.Combine(fakeRoot, "accounts.json"), "[{\"id\":\"main\",\"username\":\"MY_REAL_LOGIN_ID\"}]", Encoding.UTF8);
			Directory.CreateDirectory(Path.Combine(fakeRoot, "EBWebView", "Default"));
			File.WriteAllText(Path.Combine(fakeRoot, "EBWebView", "Default", "Cookies"), "SESSION_COOKIE_XYZ", Encoding.UTF8);
			File.WriteAllText(Path.Combine(fakeRoot, "startup-error.log"), "Error at C:\\Users\\SecretPlayer\\Desktop\\app.exe token ghp_1234567890abcdef1234567890abcdef1234", Encoding.UTF8);
			File.WriteAllText(Path.Combine(fakeRoot, "options.json"), "{\"autoMine\":true,\"customBgPath\":\"C:\\\\Users\\\\SecretPlayer\\\\pic.png\",\"customBgLabel\":\"MySecretWallpaperName\",\"wallpaperEngineProject\":\"D:\\\\Secret\\\\proj.json\"}", Encoding.UTF8);

			string zipPath = Path.Combine(outDir, "diag-test-" + BuildEdition.UpdateChannel + ".zip");
			DiagnosticExportResult res = ExportToZip(zipPath, fakeRoot, "12:00:00 测试日志 C:\\Users\\SecretPlayer\\run");

			bool leakedSecret = false;
			bool hasSummary = false;
			bool hasStartup = false;
			bool hasOptions = false;
			bool hasForbiddenEntry = false;
			using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
			using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
			{
				foreach (ZipArchiveEntry entry in zip.Entries)
				{
					string name = entry.FullName.ToLowerInvariant();
					if (name == "summary.json") hasSummary = true;
					if (name == "startup-error.log") hasStartup = true;
					if (name == "options-sanitized.json") hasOptions = true;
					if (name.Contains("account") || name.Contains("cookie") || name.Contains("ebwebview")) hasForbiddenEntry = true;
					using (Stream s = entry.Open())
					using (var r = new StreamReader(s, Encoding.UTF8))
					{
						string body = r.ReadToEnd();
						if (body.Contains("TOP_SECRET_PASSWORD_999")
							|| body.Contains("MY_REAL_LOGIN_ID")
							|| body.Contains("SESSION_COOKIE_XYZ")
							|| body.Contains("SecretPlayer")
							|| body.Contains("MySecretWallpaperName")
							|| body.Contains("ghp_1234567890abcdef1234567890abcdef1234"))
						{
							leakedSecret = true;
						}
					}
				}
			}

			var report = new Dictionary<string, object>
			{
				{ "channel", BuildEdition.UpdateChannel },
				{ "includedCount", res.IncludedFiles.Count },
				{ "hasSummary", hasSummary },
				{ "hasStartup", hasStartup },
				{ "hasOptions", hasOptions },
				{ "hasForbiddenEntry", hasForbiddenEntry },
				{ "leakedSecret", leakedSecret },
				{ "passed", hasSummary && hasStartup && hasOptions && !hasForbiddenEntry && !leakedSecret }
			};
			var js = new JavaScriptSerializer();
			File.WriteAllText(Path.Combine(outDir, "diag-smoke-" + BuildEdition.UpdateChannel + ".json"), js.Serialize(report), Encoding.UTF8);
			try { Directory.Delete(fakeRoot, true); } catch { }
			try { File.Delete(zipPath); } catch { }
		}
	}
}
