using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AandmAuto
{
	internal sealed class AccountDiskUsage
	{
		public string ProfileId;
		public string Label;
		public long BrowserDataBytes;
		public long BrowserCacheBytes;
		public long AuditBytes;
		public long AuditCleanableBytes;
		public long AppCacheBytes;

		public long CleanableBytes
		{
			get { return BrowserCacheBytes + AuditCleanableBytes + AppCacheBytes; }
		}
	}

	internal sealed class DiskUsageReport
	{
		public readonly List<AccountDiskUsage> Accounts = new List<AccountDiskUsage>();
		public long SharedCacheBytes;
		public long TotalBrowserDataBytes;
		public long TotalBrowserCacheBytes;
		public long TotalAuditBytes;
		public long TotalAuditCleanableBytes;
		public long TotalAppCacheBytes;

		public long TotalCleanableBytes
		{
			get { return TotalBrowserCacheBytes + TotalAuditCleanableBytes + TotalAppCacheBytes + SharedCacheBytes; }
		}
	}

	internal sealed class DiskCleanupResult
	{
		public long EstimatedBytes;
		public long FreedBytes;
		public long RemainingCleanableBytes;
		public int DeletedFiles;
	}

	internal static class DiskGovernance
	{
		private const long MaxSingleAuditFileBytes = 8L * 1024 * 1024;
		private const int KeepTailAuditBytes = 4 * 1024 * 1024;

		private static readonly string[] SafeWebViewCacheRelDirs =
		{
			@"EBWebView\Default\Cache",
			@"EBWebView\Default\Code Cache",
			@"EBWebView\Default\GPUCache",
			@"EBWebView\Default\DawnGraphiteCache",
			@"EBWebView\Default\DawnWebGPUCache",
			@"EBWebView\Default\Service Worker\CacheStorage",
			@"EBWebView\GrShaderCache",
			@"EBWebView\ShaderCache",
			@"EBWebView\BrowserMetrics",
			@"EBWebView\Crashpad\reports"
		};

		internal static DiskUsageReport Scan(string baseRoot, string currentCustomBgPath, int auditRetentionDays)
		{
			var report = new DiskUsageReport();
			if (string.IsNullOrEmpty(baseRoot) || !Directory.Exists(baseRoot)) return report;
			int retention = Math.Max(3, Math.Min(90, auditRetentionDays));
			DateTime cutoffUtc = DateTime.UtcNow.AddDays(-retention);

			var profiles = new AccountProfiles(baseRoot);
			List<AccountProfile> entries = profiles.Read();
			foreach (AccountProfile entry in entries)
			{
				string root = profiles.Root(entry.Id);
				if (!Directory.Exists(root)) continue;
				string activeBg = entry.Id == AppPaths.ProfileId && !string.IsNullOrEmpty(currentCustomBgPath)
					? currentCustomBgPath
					: ReadSavedCustomBgPath(root);

				var usage = new AccountDiskUsage
				{
					ProfileId = entry.Id,
					Label = profiles.Label(entry.Id)
				};

				string webDir = Path.Combine(root, "WebView2");
				usage.BrowserDataBytes = SumDirectoryBytes(webDir);
				foreach (string rel in SafeWebViewCacheRelDirs)
					usage.BrowserCacheBytes += SumDirectoryBytes(Path.Combine(webDir, rel));

				string auditDir = Path.Combine(root, "audit");
				if (Directory.Exists(auditDir))
				{
					foreach (string file in SafeGetFiles(auditDir))
					{
						long len = FileLengthSafe(file);
						usage.AuditBytes += len;
						if (IsAuditExpired(file, cutoffUtc))
							usage.AuditCleanableBytes += len;
						else if (len > MaxSingleAuditFileBytes)
							usage.AuditCleanableBytes += (len - KeepTailAuditBytes);
					}
				}

				foreach (string tempFile in EnumerateUnusedAppTempFiles(root, activeBg))
					usage.AppCacheBytes += FileLengthSafe(tempFile);

				report.Accounts.Add(usage);
				report.TotalBrowserDataBytes += usage.BrowserDataBytes;
				report.TotalBrowserCacheBytes += usage.BrowserCacheBytes;
				report.TotalAuditBytes += usage.AuditBytes;
				report.TotalAuditCleanableBytes += usage.AuditCleanableBytes;
				report.TotalAppCacheBytes += usage.AppCacheBytes;
			}

			report.SharedCacheBytes = SumDirectoryBytes(Path.Combine(baseRoot, "public-bootstrap-cache"));
			return report;
		}

		internal static DiskCleanupResult Clean(string baseRoot, string currentCustomBgPath, int auditRetentionDays)
		{
			DiskUsageReport before = Scan(baseRoot, currentCustomBgPath, auditRetentionDays);
			int retention = Math.Max(3, Math.Min(90, auditRetentionDays));
			int deleted = 0;

			if (!string.IsNullOrEmpty(baseRoot) && Directory.Exists(baseRoot))
			{
				var profiles = new AccountProfiles(baseRoot);
				foreach (AccountProfile entry in profiles.Read())
				{
					string root = profiles.Root(entry.Id);
					if (!Directory.Exists(root)) continue;
					string activeBg = entry.Id == AppPaths.ProfileId && !string.IsNullOrEmpty(currentCustomBgPath)
						? currentCustomBgPath
						: ReadSavedCustomBgPath(root);

					string webDir = Path.Combine(root, "WebView2");
					foreach (string rel in SafeWebViewCacheRelDirs)
						deleted += CleanDirectoryContents(Path.Combine(webDir, rel));

					deleted += RotateAuditLogs(root, retention);

					foreach (string tempFile in EnumerateUnusedAppTempFiles(root, activeBg))
					{
						if (TryDeleteFile(tempFile)) deleted++;
					}
				}
				deleted += CleanDirectoryContents(Path.Combine(baseRoot, "public-bootstrap-cache"));
			}

			DiskUsageReport after = Scan(baseRoot, currentCustomBgPath, auditRetentionDays);
			long freed = Math.Max(0, before.TotalCleanableBytes - after.TotalCleanableBytes);
			return new DiskCleanupResult
			{
				EstimatedBytes = before.TotalCleanableBytes,
				FreedBytes = freed,
				RemainingCleanableBytes = after.TotalCleanableBytes,
				DeletedFiles = deleted
			};
		}

		internal static int RotateAuditLogs(string profileRoot, int auditRetentionDays)
		{
			if (string.IsNullOrEmpty(profileRoot)) return 0;
			string auditDir = Path.Combine(profileRoot, "audit");
			if (!Directory.Exists(auditDir)) return 0;
			int retention = Math.Max(3, Math.Min(90, auditRetentionDays));
			DateTime cutoffUtc = DateTime.UtcNow.AddDays(-retention);
			int removed = 0;
			foreach (string file in SafeGetFiles(auditDir))
			{
				if (IsAuditExpired(file, cutoffUtc))
				{
					if (TryDeleteFile(file)) removed++;
					continue;
				}
				long len = FileLengthSafe(file);
				if (len > MaxSingleAuditFileBytes)
				{
					TrimAuditFileTail(file, KeepTailAuditBytes);
				}
			}
			return removed;
		}

		internal static int ExportAuditLogs(string baseRoot, string targetFolder)
		{
			if (string.IsNullOrEmpty(baseRoot) || string.IsNullOrEmpty(targetFolder)) return 0;
			Directory.CreateDirectory(targetFolder);
			int copied = 0;
			var profiles = new AccountProfiles(baseRoot);
			foreach (AccountProfile entry in profiles.Read())
			{
				string root = profiles.Root(entry.Id);
				string auditDir = Path.Combine(root, "audit");
				if (!Directory.Exists(auditDir)) continue;
				string[] files = SafeGetFiles(auditDir);
				if (files.Length == 0) continue;
				string destDir = Path.Combine(targetFolder, entry.Id);
				Directory.CreateDirectory(destDir);
				foreach (string file in files)
				{
					try
					{
						File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
						copied++;
					}
					catch { }
				}
			}
			return copied;
		}

		internal static string FormatSize(long bytes)
		{
			if (bytes <= 0) return "0 KB";
			if (bytes < 1024L * 1024L) return Math.Max(1, (int)Math.Round(bytes / 1024.0)) + " KB";
			return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MiB";
		}

		private static List<string> EnumerateUnusedAppTempFiles(string profileRoot, string activeBgPath)
		{
			var list = new List<string>();
			if (!Directory.Exists(profileRoot)) return list;
			string normActive = NormalizePath(activeBgPath);
			string cropBg = NormalizePath(Path.Combine(profileRoot, "custom-bg-crop.png"));
			try
			{
				foreach (string file in Directory.GetFiles(profileRoot))
				{
					string name = Path.GetFileName(file) ?? "";
					string lower = name.ToLowerInvariant();
					if (lower.StartsWith("wallpaper-smoke.") && (lower.EndsWith(".png") || lower.EndsWith(".json")))
					{
						list.Add(file);
						continue;
					}
					if ((lower.StartsWith("wallpaper-") || lower.StartsWith("wallpaper-frame-"))
						&& (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".bmp") || lower.EndsWith(".webp") || lower.EndsWith(".gif")))
					{
						string normFile = NormalizePath(file);
						if (!string.Equals(normFile, normActive, StringComparison.OrdinalIgnoreCase)
							&& !string.Equals(normFile, cropBg, StringComparison.OrdinalIgnoreCase))
						{
							list.Add(file);
						}
					}
				}
			}
			catch { }
			return list;
		}

		private static string ReadSavedCustomBgPath(string profileRoot)
		{
			try
			{
				string opt = Path.Combine(profileRoot, "options.json");
				if (!File.Exists(opt)) return "";
				var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(opt, Encoding.UTF8));
				object v;
				if (dict != null && dict.TryGetValue("customBgPath", out v) && v != null)
					return Convert.ToString(v) ?? "";
			}
			catch { }
			return "";
		}

		private static string NormalizePath(string path)
		{
			if (string.IsNullOrEmpty(path)) return "";
			try { return Path.GetFullPath(path).TrimEnd('\\'); }
			catch { return path; }
		}

		private static bool IsAuditExpired(string file, DateTime cutoffUtc)
		{
			try { return File.GetLastWriteTimeUtc(file) < cutoffUtc; }
			catch { return false; }
		}

		private static void TrimAuditFileTail(string file, int keepBytes)
		{
			try
			{
				byte[] tail;
				using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					if (fs.Length <= keepBytes) return;
					fs.Seek(-keepBytes, SeekOrigin.End);
					tail = new byte[keepBytes];
					int read = fs.Read(tail, 0, keepBytes);
					if (read <= 0) return;
					if (read < tail.Length) Array.Resize(ref tail, read);
				}
				int start = 0;
				while (start < tail.Length && tail[start] != (byte)'\n') start++;
				if (start + 1 < tail.Length) start++;
				else start = 0;
				string temp = file + ".trim.tmp";
				using (var outFs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
					outFs.Write(tail, start, tail.Length - start);
				File.Replace(temp, file, null);
			}
			catch { }
		}

		private static long SumDirectoryBytes(string dir)
		{
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
			long total = 0;
			try
			{
				foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
					total += FileLengthSafe(file);
			}
			catch { }
			return total;
		}

		private static int CleanDirectoryContents(string dir)
		{
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
			int deleted = 0;
			try
			{
				foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
				{
					if (TryDeleteFile(file)) deleted++;
				}
				foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
				{
					try { if (Directory.GetFiles(sub).Length == 0 && Directory.GetDirectories(sub).Length == 0) Directory.Delete(sub, false); }
					catch { }
				}
			}
			catch { }
			return deleted;
		}

		private static string[] SafeGetFiles(string dir)
		{
			try { return Directory.GetFiles(dir); }
			catch { return new string[0]; }
		}

		private static long FileLengthSafe(string path)
		{
			try { return new FileInfo(path).Length; }
			catch { return 0; }
		}

		private static bool TryDeleteFile(string path)
		{
			try
			{
				File.Delete(path);
				return true;
			}
			catch { return false; }
		}
	}
}
