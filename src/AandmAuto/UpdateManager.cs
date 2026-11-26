using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
	internal enum UpdateCheckState
	{
		NotConfigured,
		UpToDate,
		UpdateAvailable,
		Error
	}

	internal sealed class UpdateConfig
	{
		public string Provider = "unconfigured";
		public string RepoUrl = "";
		public string ReleasePageUrl = "";
		public List<string> ManifestUrls = new List<string>();
		public List<string> DownloadMirrors = new List<string>();

		public bool IsConfigured
		{
			get { return ManifestUrls != null && ManifestUrls.Count > 0; }
		}
	}

	internal sealed class UpdateCheckResult
	{
		public UpdateCheckState State = UpdateCheckState.NotConfigured;
		public string CurrentVersion = BuildEdition.AppVersion;
		public string Channel = BuildEdition.UpdateChannel;
		public string LatestVersion = "";
		public string Tag = "";
		public string FileName = "";
		public string Sha256 = "";
		public long Size;
		public string PublishedAt = "";
		public string Notes = "";
		public string DownloadUrl = "";
		public string ReleaseUrl = "";
		public string SourceUrl = "";
		public string ErrorMessage = "";
		public DateTime CheckedAt = DateTime.Now;
	}

	internal static class UpdateManager
	{
		private static readonly object StateLock = new object();
		private static UpdateCheckResult lastResult;

		internal static UpdateCheckResult LastResult
		{
			get { lock (StateLock) { return lastResult; } }
			private set { lock (StateLock) { lastResult = value; } }
		}

		internal static string LastCheckSummary()
		{
			UpdateCheckResult r = LastResult;
			if (r == null) return "未检查";
			switch (r.State)
			{
			case UpdateCheckState.NotConfigured:
				return "未绑定公开更新源";
			case UpdateCheckState.UpToDate:
				return "已是最新 (v" + r.CurrentVersion + ")";
			case UpdateCheckState.UpdateAvailable:
				return "发现新版本 v" + r.LatestVersion;
			default:
				return "上次检查失败";
			}
		}

		private static void EnsureTls()
		{
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
			}
			catch
			{
			}
		}

		internal static UpdateConfig LoadConfig()
		{
			var cfg = new UpdateConfig();
			try
			{
				using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.update-config.json"))
				{
					if (stream != null)
					{
						using (var reader = new StreamReader(stream, Encoding.UTF8))
						{
							MergeConfigJson(cfg, reader.ReadToEnd());
						}
					}
				}
			}
			catch
			{
			}

			try
			{
				string localOverride = Path.Combine(AppPaths.DataRoot, "update-source.json");
				if (File.Exists(localOverride))
				{
					MergeConfigJson(cfg, File.ReadAllText(localOverride, Encoding.UTF8));
				}
			}
			catch
			{
			}

			string envUrl = Environment.GetEnvironmentVariable("AANDM_UPDATE_MANIFEST_URL");
			if (!string.IsNullOrEmpty(envUrl))
			{
				cfg.Provider = "custom";
				cfg.ManifestUrls = new List<string> { envUrl.Trim() };
			}
			return cfg;
		}

		private static void MergeConfigJson(UpdateConfig cfg, string rawJson)
		{
			if (string.IsNullOrEmpty(rawJson)) return;
			var js = new JavaScriptSerializer();
			var map = js.DeserializeObject(rawJson) as Dictionary<string, object>;
			if (map == null) return;

			string s;
			if (TryGetString(map, "provider", out s)) cfg.Provider = s;
			if (TryGetString(map, "repoUrl", out s)) cfg.RepoUrl = s;
			if (TryGetString(map, "releasePageUrl", out s)) cfg.ReleasePageUrl = s;

			List<string> urls;
			if (TryGetStringList(map, "manifestUrls", out urls)) cfg.ManifestUrls = urls;
			List<string> mirrors;
			if (TryGetStringList(map, "downloadMirrors", out mirrors)) cfg.DownloadMirrors = mirrors;
		}

		private static bool TryGetString(Dictionary<string, object> map, string key, out string value)
		{
			value = "";
			object obj;
			if (map == null || !map.TryGetValue(key, out obj) || obj == null) return false;
			value = Convert.ToString(obj).Trim();
			return true;
		}

		private static bool TryGetLong(Dictionary<string, object> map, string key, out long value)
		{
			value = 0;
			object obj;
			if (map == null || !map.TryGetValue(key, out obj) || obj == null) return false;
			try
			{
				value = Convert.ToInt64(obj);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static bool TryGetStringList(Dictionary<string, object> map, string key, out List<string> list)
		{
			list = new List<string>();
			object obj;
			if (map == null || !map.TryGetValue(key, out obj) || obj == null) return false;
			var arr = obj as object[];
			if (arr == null)
			{
				var al = obj as System.Collections.ArrayList;
				if (al != null) arr = al.ToArray();
			}
			if (arr == null) return false;
			foreach (object item in arr)
			{
				string str = item == null ? "" : Convert.ToString(item).Trim();
				if (!string.IsNullOrEmpty(str)) list.Add(str);
			}
			return true;
		}

		internal static List<string> BuildCandidateUrls(string url, List<string> mirrors)
		{
			var result = new List<string>();
			if (string.IsNullOrEmpty(url)) return result;
			string trimmed = url.Trim();
			AddUnique(result, trimmed);

			bool isGitHub = trimmed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
				|| trimmed.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase);

			if (isGitHub && mirrors != null)
			{
				foreach (string rawMirror in mirrors)
				{
					if (string.IsNullOrEmpty(rawMirror)) continue;
					string m = rawMirror.Trim();
					if (!m.EndsWith("/")) m += "/";
					AddUnique(result, m + trimmed);
				}
			}
			return result;
		}

		private static void AddUnique(List<string> list, string item)
		{
			if (string.IsNullOrEmpty(item)) return;
			for (int i = 0; i < list.Count; i++)
			{
				if (string.Equals(list[i], item, StringComparison.OrdinalIgnoreCase)) return;
			}
			list.Add(item);
		}

		internal static int CompareSemVer(string remoteVersion, string localVersion)
		{
			int[] a = ParseVersionParts(remoteVersion);
			int[] b = ParseVersionParts(localVersion);
			int len = Math.Max(a.Length, b.Length);
			for (int i = 0; i < len; i++)
			{
				int va = i < a.Length ? a[i] : 0;
				int vb = i < b.Length ? b[i] : 0;
				if (va != vb) return va > vb ? 1 : -1;
			}
			return 0;
		}

		private static int[] ParseVersionParts(string version)
		{
			if (string.IsNullOrEmpty(version)) return new[] { 0, 0, 0 };
			string clean = version.Trim();
			if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(1);
			int dash = clean.IndexOf('-');
			if (dash >= 0) clean = clean.Substring(0, dash);
			int plus = clean.IndexOf('+');
			if (plus >= 0) clean = clean.Substring(0, plus);
			string[] tokens = clean.Split('.');
			var nums = new int[Math.Max(3, tokens.Length)];
			for (int i = 0; i < tokens.Length; i++)
			{
				int parsed;
				if (int.TryParse(tokens[i], out parsed) && parsed >= 0) nums[i] = parsed;
			}
			return nums;
		}

		internal static UpdateCheckResult CheckForUpdates(CancellationToken cancel)
		{
			EnsureTls();
			UpdateConfig cfg = LoadConfig();
			var res = new UpdateCheckResult
			{
				CurrentVersion = BuildEdition.AppVersion,
				Channel = BuildEdition.UpdateChannel,
				ReleaseUrl = !string.IsNullOrEmpty(cfg.ReleasePageUrl) ? cfg.ReleasePageUrl : cfg.RepoUrl,
				CheckedAt = DateTime.Now
			};

			if (!cfg.IsConfigured)
			{
				res.State = UpdateCheckState.NotConfigured;
				res.ErrorMessage = "当前版本尚未绑定公开更新仓库地址。绑定可公开访问的仓库（GitHub / Gitee）并重新编译后即可启用在线检查更新。";
				LastResult = res;
				return res;
			}

			var candidates = new List<string>();
			foreach (string baseUrl in cfg.ManifestUrls)
			{
				foreach (string u in BuildCandidateUrls(baseUrl, cfg.DownloadMirrors))
				{
					AddUnique(candidates, u);
				}
			}

			string lastError = "所有更新源均连接失败";
			foreach (string url in candidates)
			{
				if (cancel.IsCancellationRequested)
				{
					res.State = UpdateCheckState.Error;
					res.ErrorMessage = "已取消检查更新。";
					return res;
				}
				try
				{
					string body = DownloadText(url, 8000, cancel);
					if (string.IsNullOrEmpty(body))
					{
						lastError = "更新清单为空";
						continue;
					}
					UpdateCheckResult parsed = ParseManifest(body, url, cfg);
					if (parsed != null)
					{
						LastResult = parsed;
						return parsed;
					}
					lastError = "更新清单格式不含当前通道：" + BuildEdition.UpdateChannel;
				}
				catch (OperationCanceledException)
				{
					res.State = UpdateCheckState.Error;
					res.ErrorMessage = "已取消检查更新。";
					return res;
				}
				catch (Exception ex)
				{
					lastError = ex.Message;
				}
			}

			res.State = UpdateCheckState.Error;
			res.ErrorMessage = "无法连接到更新服务器：" + lastError;
			LastResult = res;
			return res;
		}

		private static UpdateCheckResult ParseManifest(string body, string sourceUrl, UpdateConfig cfg)
		{
			var js = new JavaScriptSerializer();
			var root = js.DeserializeObject(body) as Dictionary<string, object>;
			if (root == null) return null;

			object channelsObj;
			if (!root.TryGetValue("channels", out channelsObj)) return null;
			var channels = channelsObj as Dictionary<string, object>;
			if (channels == null) return null;

			object entryObj;
			if (!channels.TryGetValue(BuildEdition.UpdateChannel, out entryObj)) return null;
			var entry = entryObj as Dictionary<string, object>;
			if (entry == null) return null;

			var res = new UpdateCheckResult
			{
				CurrentVersion = BuildEdition.AppVersion,
				Channel = BuildEdition.UpdateChannel,
				SourceUrl = sourceUrl,
				CheckedAt = DateTime.Now
			};

			TryGetString(entry, "version", out res.LatestVersion);
			TryGetString(entry, "tag", out res.Tag);
			TryGetString(entry, "fileName", out res.FileName);
			TryGetString(entry, "sha256", out res.Sha256);
			TryGetLong(entry, "size", out res.Size);
			TryGetString(entry, "publishedAt", out res.PublishedAt);
			TryGetString(entry, "notes", out res.Notes);
			TryGetString(entry, "downloadUrl", out res.DownloadUrl);
			TryGetString(entry, "releaseUrl", out res.ReleaseUrl);

			if (string.IsNullOrEmpty(res.ReleaseUrl))
			{
				res.ReleaseUrl = !string.IsNullOrEmpty(cfg.ReleasePageUrl) ? cfg.ReleasePageUrl : cfg.RepoUrl;
			}
			if (string.IsNullOrEmpty(res.FileName))
			{
				res.FileName = BuildEdition.DefaultExeName;
			}
			if (string.IsNullOrEmpty(res.LatestVersion))
			{
				return null;
			}

			int cmp = CompareSemVer(res.LatestVersion, res.CurrentVersion);
			res.State = cmp > 0 ? UpdateCheckState.UpdateAvailable : UpdateCheckState.UpToDate;
			return res;
		}

		private static string DownloadText(string url, int timeoutMs, CancellationToken cancel)
		{
			var req = (HttpWebRequest)WebRequest.Create(url);
			req.Method = "GET";
			req.UserAgent = "AandmAuto/" + BuildEdition.AppVersion;
			req.Timeout = timeoutMs;
			req.ReadWriteTimeout = timeoutMs;
			req.AllowAutoRedirect = true;
			req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

			using (cancel.Register(delegate { try { req.Abort(); } catch { } }))
			using (var resp = (HttpWebResponse)req.GetResponse())
			{
				if ((int)resp.StatusCode < 200 || (int)resp.StatusCode >= 300)
				{
					throw new InvalidOperationException("HTTP " + (int)resp.StatusCode);
				}
				using (Stream stream = resp.GetResponseStream())
				using (var reader = new StreamReader(stream, Encoding.UTF8))
				{
					return reader.ReadToEnd();
				}
			}
		}

		internal static string DownloadAndVerify(UpdateCheckResult info, Action<int, long, long, string> onProgress, CancellationToken cancel)
		{
			EnsureTls();
			if (info == null || string.IsNullOrEmpty(info.DownloadUrl))
			{
				throw new InvalidOperationException("更新清单中缺少下载地址。");
			}
			string expectedHash = (info.Sha256 ?? "").Trim().ToLowerInvariant();
			if (expectedHash.Length != 64 || !IsHexString(expectedHash))
			{
				throw new InvalidOperationException("更新清单缺少有效的 SHA-256 校验码，为安全起见已拒绝自动替换，请使用浏览器手动下载。");
			}

			UpdateConfig cfg = LoadConfig();
			List<string> candidates = BuildCandidateUrls(info.DownloadUrl, cfg.DownloadMirrors);
			string tempDir = Path.Combine(Path.GetTempPath(), "AandmAutoUpdate", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(tempDir);
			string safeName = SanitizeFileName(string.IsNullOrEmpty(info.FileName) ? BuildEdition.DefaultExeName : info.FileName);
			string tempPath = Path.Combine(tempDir, safeName + ".download");

			string lastError = "下载失败";
			for (int i = 0; i < candidates.Count; i++)
			{
				cancel.ThrowIfCancellationRequested();
				string url = candidates[i];
				string sourceLabel = i == 0 ? "直连线路" : ("加速镜像 " + i);
				try
				{
					if (File.Exists(tempPath)) File.Delete(tempPath);
					DownloadFile(url, tempPath, info.Size, sourceLabel, onProgress, cancel);
					if (onProgress != null) onProgress(100, info.Size, info.Size, "正在校验 SHA-256…");
					string actualHash = ComputeSha256(tempPath);
					if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
					{
						try { File.Delete(tempPath); } catch { }
						lastError = sourceLabel + " 文件 SHA-256 校验不匹配，已自动丢弃";
						continue;
					}
					return tempPath;
				}
				catch (OperationCanceledException)
				{
					try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
					throw;
				}
				catch (Exception ex)
				{
					try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
					lastError = sourceLabel + " 失败：" + ex.Message;
				}
			}

			try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
			throw new InvalidOperationException("所有下载线路均失败：" + lastError);
		}

		private static void DownloadFile(string url, string destination, long expectedSize, string sourceLabel, Action<int, long, long, string> onProgress, CancellationToken cancel)
		{
			var req = (HttpWebRequest)WebRequest.Create(url);
			req.Method = "GET";
			req.UserAgent = "AandmAuto/" + BuildEdition.AppVersion;
			req.Timeout = 15000;
			req.ReadWriteTimeout = 30000;
			req.AllowAutoRedirect = true;

			using (cancel.Register(delegate { try { req.Abort(); } catch { } }))
			using (var resp = (HttpWebResponse)req.GetResponse())
			{
				if ((int)resp.StatusCode < 200 || (int)resp.StatusCode >= 300)
				{
					throw new InvalidOperationException("HTTP " + (int)resp.StatusCode);
				}
				long total = resp.ContentLength > 0 ? resp.ContentLength : expectedSize;
				using (Stream input = resp.GetResponseStream())
				using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					byte[] buffer = new byte[65536];
					long readTotal = 0;
					int lastPct = -1;
					int read;
					while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
					{
						cancel.ThrowIfCancellationRequested();
						output.Write(buffer, 0, read);
						readTotal += read;
						int pct = total > 0 ? (int)Math.Min(99, (readTotal * 100L) / total) : 0;
						if (onProgress != null && (pct != lastPct || total <= 0))
						{
							lastPct = pct;
							onProgress(pct, readTotal, total, sourceLabel);
						}
					}
					output.Flush();
				}
			}
		}

		internal static string ComputeSha256(string filePath)
		{
			using (var sha = SHA256.Create())
			using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				byte[] hash = sha.ComputeHash(stream);
				var sb = new StringBuilder(64);
				for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
				return sb.ToString();
			}
		}

		internal static void VerifyWritePermission(string targetExePath)
		{
			string dir = Path.GetDirectoryName(targetExePath);
			if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("无法确定当前程序目录。");
			string probe = Path.Combine(dir, ".aandm-update-probe-" + Guid.NewGuid().ToString("N"));
			try
			{
				File.WriteAllText(probe, "ok", Encoding.UTF8);
				File.Delete(probe);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException("当前程序目录没有写入权限，无法原地自动替换（请改用浏览器手动下载，或将助手移动到桌面/D盘后重试）。\r\n原因：" + ex.Message);
			}
		}

		internal static void LaunchSelfReplaceAndRestart(string verifiedTempFile, string targetExePath)
		{
			if (!File.Exists(verifiedTempFile)) throw new FileNotFoundException("找不到已校验的新版临时文件。", verifiedTempFile);
			VerifyWritePermission(targetExePath);

			int pid = Process.GetCurrentProcess().Id;
			string workDir = Path.GetDirectoryName(targetExePath) ?? Environment.CurrentDirectory;
			string scriptDir = Path.GetDirectoryName(verifiedTempFile) ?? Path.GetTempPath();
			string scriptPath = Path.Combine(scriptDir, "apply-update.ps1");

			var sb = new StringBuilder();
			sb.AppendLine("param(");
			sb.AppendLine("  [int]$ParentPid,");
			sb.AppendLine("  [string]$SourceFile,");
			sb.AppendLine("  [string]$TargetExe,");
			sb.AppendLine("  [string]$WorkDir");
			sb.AppendLine(")");
			sb.AppendLine("$ErrorActionPreference = 'Stop'");
			sb.AppendLine("for ($i = 0; $i -lt 120; $i++) {");
			sb.AppendLine("  if (-not (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) { break }");
			sb.AppendLine("  Start-Sleep -Milliseconds 250");
			sb.AppendLine("}");
			sb.AppendLine("$backup = $TargetExe + '.old'");
			sb.AppendLine("$replaced = $false");
			sb.AppendLine("for ($try = 0; $try -lt 25; $try++) {");
			sb.AppendLine("  try {");
			sb.AppendLine("    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue }");
			sb.AppendLine("    if (Test-Path -LiteralPath $TargetExe) { Move-Item -LiteralPath $TargetExe -Destination $backup -Force }");
			sb.AppendLine("    Move-Item -LiteralPath $SourceFile -Destination $TargetExe -Force");
			sb.AppendLine("    $replaced = $true");
			sb.AppendLine("    break");
			sb.AppendLine("  } catch {");
			sb.AppendLine("    if ((-not (Test-Path -LiteralPath $TargetExe)) -and (Test-Path -LiteralPath $backup)) {");
			sb.AppendLine("      try { Move-Item -LiteralPath $backup -Destination $TargetExe -Force } catch {}");
			sb.AppendLine("    }");
			sb.AppendLine("    Start-Sleep -Milliseconds 300");
			sb.AppendLine("  }");
			sb.AppendLine("}");
			sb.AppendLine("if ($replaced) {");
			sb.AppendLine("  try { Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue } catch {}");
			sb.AppendLine("  Start-Process -FilePath $TargetExe -WorkingDirectory $WorkDir");
			sb.AppendLine("}");
			sb.AppendLine("try { Remove-Item -LiteralPath $PSScriptRoot -Recurse -Force -ErrorAction SilentlyContinue } catch {}");

			// UTF-8 with BOM so Windows PowerShell 5.1 reads Chinese paths cleanly
			File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(true));

			string args = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "
				+ AppPaths.QuoteArgument(scriptPath)
				+ " -ParentPid " + pid
				+ " -SourceFile " + AppPaths.QuoteArgument(verifiedTempFile)
				+ " -TargetExe " + AppPaths.QuoteArgument(targetExePath)
				+ " -WorkDir " + AppPaths.QuoteArgument(workDir);

			var psi = new ProcessStartInfo
			{
				FileName = "powershell.exe",
				Arguments = args,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			};
			Process.Start(psi);
		}

		private static bool IsHexString(string text)
		{
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
				if (!ok) return false;
			}
			return true;
		}

		private static string SanitizeFileName(string name)
		{
			string clean = Path.GetFileName(name ?? "update.exe");
			foreach (char c in Path.GetInvalidFileNameChars()) clean = clean.Replace(c, '_');
			return string.IsNullOrEmpty(clean) ? "update.exe" : clean;
		}

		internal static void OpenUrlInBrowser(string url)
		{
			if (string.IsNullOrEmpty(url)) return;
			try
			{
				Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			}
			catch
			{
			}
		}

		/// <summary>
		/// 自动化回归自检入口（--updater-smoke &lt;outDir&gt;）：不弹窗、不退出外部进程，验证版本比较、通道隔离、镜像回退、SHA-256 防篡改校验与正常下载流程。
		/// </summary>
		internal static void RunSmokeSelfTest(string outDir)
		{
			Directory.CreateDirectory(outDir);
			var report = new Dictionary<string, object>();
			report["channel"] = BuildEdition.UpdateChannel;
			report["version"] = BuildEdition.AppVersion;

			// 1. SemVer comparison checks
			bool semverOk = CompareSemVer("1.0.1", "1.0.0") > 0
				&& CompareSemVer("1.1.0", "1.0.9") > 0
				&& CompareSemVer("v1.0.0", "1.0.0") == 0
				&& CompareSemVer("1.0.0-testpvp", "1.0.0") == 0
				&& CompareSemVer("0.9.9", "1.0.0") < 0;
			report["semverOk"] = semverOk;

			// 2. Candidate URL mirror expansion check
			var mirrors = new List<string> { "https://ghfast.top/", "https://gh-proxy.com/" };
			var ghUrls = BuildCandidateUrls("https://github.com/demo/repo/releases/download/v1.0.1/app.exe", mirrors);
			var giteeUrls = BuildCandidateUrls("https://gitee.com/demo/repo/raw/main/updates.json", mirrors);
			bool mirrorOrderOk = ghUrls.Count == 3
				&& ghUrls[0] == "https://github.com/demo/repo/releases/download/v1.0.1/app.exe"
				&& ghUrls[1] == "https://ghfast.top/https://github.com/demo/repo/releases/download/v1.0.1/app.exe"
				&& giteeUrls.Count == 1;
			report["mirrorOrderOk"] = mirrorOrderOk;

			// 3. Live local HTTP mock test if AANDM_UPDATE_MANIFEST_URL is set
			string envUrl = Environment.GetEnvironmentVariable("AANDM_UPDATE_MANIFEST_URL");
			if (!string.IsNullOrEmpty(envUrl))
			{
				UpdateCheckResult res = CheckForUpdates(CancellationToken.None);
				report["checkState"] = res.State.ToString();
				report["latestVersion"] = res.LatestVersion;
				report["notes"] = res.Notes;
				report["error"] = res.ErrorMessage;

				if (res.State == UpdateCheckState.UpdateAvailable)
				{
					// Test tampered hash rejection
					var tampered = new UpdateCheckResult
					{
						DownloadUrl = res.DownloadUrl,
						FileName = res.FileName,
						Sha256 = new string('0', 64),
						Size = res.Size
					};
					bool tamperRejected = false;
					try
					{
						DownloadAndVerify(tampered, null, CancellationToken.None);
					}
					catch (Exception)
					{
						tamperRejected = true;
					}
					report["tamperRejected"] = tamperRejected;

					// Test valid download & SHA-256 verification
					string verified = DownloadAndVerify(res, null, CancellationToken.None);
					bool verifiedExists = File.Exists(verified);
					report["downloadVerified"] = verifiedExists;
					try { if (verifiedExists) File.Delete(verified); } catch { }
				}
			}

			var js = new JavaScriptSerializer();
			File.WriteAllText(Path.Combine(outDir, "updater-smoke-" + BuildEdition.UpdateChannel + ".json"), js.Serialize(report), Encoding.UTF8);
		}
	}
}
