using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace AandmAuto
{
	internal sealed class WallpaperEngineItem
	{
		public string FolderPath;
		public string ProjectJsonPath;
		public string Title;
		public string Type;
		public string PreviewPath;
		public string AnimatedPreviewPath;
		public string StaticImagePath;
		public string ContentFilePath;
		public string WorkshopId;
		public DateTime LastModifiedUtc;

		public string TypeLabel
		{
			get
			{
				string t = (Type ?? "").Trim().ToLowerInvariant();
				switch (t)
				{
				case "video": return "视频";
				case "scene": return "场景";
				case "web": return "网页";
				case "image": return "图片";
				case "application": return "应用";
				default: return string.IsNullOrEmpty(t) ? "壁纸" : t;
				}
			}
		}

		public bool HasAnimatedPreview
		{
			get { return !string.IsNullOrEmpty(AnimatedPreviewPath) && File.Exists(AnimatedPreviewPath); }
		}

		public string PlaybackSource
		{
			get
			{
				switch (Type)
				{
				case "video":
					return ContentFilePath ?? "";
				case "scene":
					return File.Exists(ContentFilePath) || File.Exists(Path.Combine(FolderPath, "scene.pkg"))
						? ProjectJsonPath : "";
				case "web":
					return File.Exists(ContentFilePath) ? ContentFilePath : "";
				default:
					return "";
				}
			}
		}

		public string BestStaticImagePath
		{
			get
			{
				if (!string.IsNullOrEmpty(StaticImagePath) && File.Exists(StaticImagePath)) return StaticImagePath;
				if (!string.IsNullOrEmpty(PreviewPath) && File.Exists(PreviewPath)) return PreviewPath;
				if (!string.IsNullOrEmpty(AnimatedPreviewPath) && File.Exists(AnimatedPreviewPath)) return AnimatedPreviewPath;
				return "";
			}
		}
	}

	internal static class WallpaperEngineLocator
	{
		private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
		private static readonly Regex SteamPathRegex = new Regex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		internal static string FindInstallDir(string overrideDir = "")
		{
			var candidates = new List<string>();
			AddCandidateInstallDir(candidates, overrideDir);

			// Check running process first
			foreach (string procName in new[] { "wallpaper64", "wallpaper32" })
			{
				try
				{
					Process[] procs = Process.GetProcessesByName(procName);
					foreach (Process p in procs)
					{
						try
						{
							if (p.MainModule != null && !string.IsNullOrEmpty(p.MainModule.FileName))
								AddCandidateInstallDir(candidates, Path.GetDirectoryName(p.MainModule.FileName));
						}
						catch { }
						finally { try { p.Dispose(); } catch { } }
					}
				}
				catch { }
			}

			// Check Steam install & libraries
			foreach (string steamDir in FindSteamInstallDirs())
			{
				foreach (string lib in FindSteamLibraries(steamDir))
				{
					AddCandidateInstallDir(candidates, Path.Combine(lib, @"steamapps\common\wallpaper_engine"));
				}
			}

			// Fallback fixed drives scan
			foreach (string root in new[] { @"C:\", @"D:\", @"E:\", @"F:\", @"G:\" })
			{
				AddCandidateInstallDir(candidates, Path.Combine(root, @"Program Files (x86)\Steam\steamapps\common\wallpaper_engine"));
				AddCandidateInstallDir(candidates, Path.Combine(root, @"Program Files\Steam\steamapps\common\wallpaper_engine"));
				AddCandidateInstallDir(candidates, Path.Combine(root, @"SteamLibrary\steamapps\common\wallpaper_engine"));
				AddCandidateInstallDir(candidates, Path.Combine(root, @"Steam\steamapps\common\wallpaper_engine"));
				AddCandidateInstallDir(candidates, Path.Combine(root, @"wallpaper_engine"));
			}

			foreach (string dir in candidates)
			{
				if (IsInstallDirValid(dir)) return dir;
			}
			foreach (string dir in candidates)
			{
				if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
			}
			return "";
		}

		private static void AddCandidateInstallDir(List<string> list, string raw)
		{
			if (string.IsNullOrEmpty(raw)) return;
			string dir = raw.Trim().Trim('"');
			if (File.Exists(dir)) dir = Path.GetDirectoryName(dir);
			if (string.IsNullOrEmpty(dir)) return;

			// If user selected parent steamapps or 431960 dir, infer common\wallpaper_engine if adjacent
			string trimmed = dir.TrimEnd('\\', '/');
			if (!ContainsIgnoreCase(list, trimmed)) list.Add(trimmed);
		}

		private static bool IsInstallDirValid(string dir)
		{
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
			return File.Exists(Path.Combine(dir, "wallpaper64.exe"))
				|| File.Exists(Path.Combine(dir, "wallpaper32.exe"))
				|| File.Exists(Path.Combine(dir, "config.json"));
		}

		internal static string FindExecutable(string installDir)
		{
			string resolved = string.IsNullOrEmpty(installDir) ? FindInstallDir("") : installDir;
			if (string.IsNullOrEmpty(resolved) || !Directory.Exists(resolved)) return "";
			string exe64 = Path.Combine(resolved, "wallpaper64.exe");
			if (File.Exists(exe64)) return exe64;
			string exe32 = Path.Combine(resolved, "wallpaper32.exe");
			if (File.Exists(exe32)) return exe32;
			return "";
		}

        private const string TrayRegistryKey = @"Software\WallpaperEngine";
        private const string TrayRegistryValue = "hideTrayIcon";

        internal static bool TrayIconHidden()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(TrayRegistryKey))
                return key != null && Equals(key.GetValue(TrayRegistryValue), 1);
        }

        internal static void SetTrayIconHidden(bool hidden)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(TrayRegistryKey))
                key.SetValue(TrayRegistryValue, hidden ? 1 : 0, RegistryValueKind.DWord);
        }

		internal static string FindPlaybackExecutable(string customDir)
		{
			string install = FindInstallDir(customDir);
			foreach (string name in new[] { "wallpaper32", "wallpaper64" })
			{
				foreach (Process process in Process.GetProcessesByName(name))
				{
					using (process)
					{
						try
						{
							string path = process.MainModule.FileName;
							if (string.Equals(Path.GetDirectoryName(path), install, StringComparison.OrdinalIgnoreCase))
								return path;
						}
						catch (System.ComponentModel.Win32Exception) { }
						catch (InvalidOperationException) { }
					}
				}
			}
			string fallback = FindExecutable(install);
			if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback))
				return fallback;
			return "";
		}

        internal static Size ReadVideoSize(string path)
        {
            double fps;
            return ReadVideoInfo(path, out fps);
        }

        internal static Size ReadVideoInfo(string path, out double fps)
		{
            fps = 0;
			Type shellType = System.Type.GetTypeFromProgID("Shell.Application");
			if (shellType == null) return Size.Empty;
			object shell = null, folder = null, file = null;
			try
			{
				shell = Activator.CreateInstance(shellType);
				folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell,
					new object[] { Path.GetDirectoryName(path) });
				if (folder == null) return Size.Empty;
				file = folder.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, folder,
					new object[] { Path.GetFileName(path) });
				if (file == null) return Size.Empty;
				int width = ReadVideoDimension(file, "System.Video.FrameWidth");
				int height = ReadVideoDimension(file, "System.Video.FrameHeight");
                fps = ReadVideoDimension(file, "System.Video.FrameRate") / 1000.0;
				return width > 0 && height > 0 ? new Size(width, height) : Size.Empty;
			}
			catch (COMException) { return Size.Empty; }
			catch (TargetInvocationException) { return Size.Empty; }
			catch (InvalidCastException) { return Size.Empty; }
			finally
			{
				if (file != null) Marshal.ReleaseComObject(file);
				if (folder != null) Marshal.ReleaseComObject(folder);
				if (shell != null) Marshal.ReleaseComObject(shell);
			}
		}

		private static int ReadVideoDimension(object file, string property)
		{
			object value = file.GetType().InvokeMember("ExtendedProperty", BindingFlags.InvokeMethod, null, file, new object[] { property });
			int pixels;
			return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out pixels) ? pixels : 0;
		}

		internal static string FindConfigPath(string installDir)
		{
			string resolved = string.IsNullOrEmpty(installDir) ? FindInstallDir("") : installDir;
			if (!string.IsNullOrEmpty(resolved))
			{
				string cfg = Path.Combine(resolved, "config.json");
				if (File.Exists(cfg)) return cfg;
			}
			return "";
		}

		internal static List<string> FindLibraryRoots(string installDir, string customDir = "")
		{
			var roots = new List<string>();
			string resolvedInstall = !string.IsNullOrEmpty(installDir) ? installDir : FindInstallDir(customDir);

			if (!string.IsNullOrEmpty(customDir) && Directory.Exists(customDir))
			{
				AddLibraryRootIfExists(roots, customDir);
				AddLibraryRootIfExists(roots, Path.Combine(customDir, @"steamapps\workshop\content\431960"));
				AddLibraryRootIfExists(roots, Path.Combine(customDir, @"projects\myprojects"));
				AddLibraryRootIfExists(roots, Path.Combine(customDir, @"projects\defaultprojects"));
			}

			if (!string.IsNullOrEmpty(resolvedInstall) && Directory.Exists(resolvedInstall))
			{
				AddLibraryRootIfExists(roots, Path.Combine(resolvedInstall, @"projects\myprojects"));
				AddLibraryRootIfExists(roots, Path.Combine(resolvedInstall, @"projects\defaultprojects"));
				// Derive steamapps from <lib>\steamapps\common\wallpaper_engine
				try
				{
					DirectoryInfo commonDir = Directory.GetParent(resolvedInstall);
					DirectoryInfo steamappsDir = commonDir != null ? commonDir.Parent : null;
					if (steamappsDir != null && steamappsDir.Exists)
					{
						AddLibraryRootIfExists(roots, Path.Combine(steamappsDir.FullName, @"workshop\content\431960"));
					}
				}
				catch { }
			}

			foreach (string steamDir in FindSteamInstallDirs())
			{
				foreach (string lib in FindSteamLibraries(steamDir))
				{
					AddLibraryRootIfExists(roots, Path.Combine(lib, @"steamapps\workshop\content\431960"));
				}
			}

			foreach (string drive in new[] { @"C:\", @"D:\", @"E:\", @"F:\", @"G:\" })
			{
				AddLibraryRootIfExists(roots, Path.Combine(drive, @"Program Files (x86)\Steam\steamapps\workshop\content\431960"));
				AddLibraryRootIfExists(roots, Path.Combine(drive, @"Program Files\Steam\steamapps\workshop\content\431960"));
				AddLibraryRootIfExists(roots, Path.Combine(drive, @"SteamLibrary\steamapps\workshop\content\431960"));
				AddLibraryRootIfExists(roots, Path.Combine(drive, @"Steam\steamapps\workshop\content\431960"));
			}

			return roots;
		}

		private static void AddLibraryRootIfExists(List<string> roots, string path)
		{
			if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
			try
			{
				string full = Path.GetFullPath(path).TrimEnd('\\', '/');
				if (!ContainsIgnoreCase(roots, full)) roots.Add(full);
			}
			catch { }
		}

		private static List<string> FindSteamInstallDirs()
		{
			var dirs = new List<string>();
			TryReadRegPath(dirs, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
			TryReadRegPath(dirs, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
			TryReadRegPath(dirs, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
			return dirs;
		}

		private static void TryReadRegPath(List<string> dirs, RegistryKey hive, string subKey, string valueName)
		{
			try
			{
				using (RegistryKey key = hive.OpenSubKey(subKey))
				{
					if (key == null) return;
					object val = key.GetValue(valueName);
					if (val == null) return;
					string path = Convert.ToString(val).Replace('/', '\\').Trim();
					if (Directory.Exists(path) && !ContainsIgnoreCase(dirs, path)) dirs.Add(path);
				}
			}
			catch { }
		}

		private static List<string> FindSteamLibraries(string steamDir)
		{
			var libs = new List<string>();
			if (string.IsNullOrEmpty(steamDir) || !Directory.Exists(steamDir)) return libs;
			libs.Add(steamDir);
			string vdf = Path.Combine(steamDir, @"steamapps\libraryfolders.vdf");
			if (!File.Exists(vdf)) vdf = Path.Combine(steamDir, @"config\libraryfolders.vdf");
			if (!File.Exists(vdf)) return libs;
			try
			{
				string text = File.ReadAllText(vdf, Encoding.UTF8);
				foreach (Match m in SteamPathRegex.Matches(text))
				{
					string p = m.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\').Trim();
					if (Directory.Exists(p) && !ContainsIgnoreCase(libs, p)) libs.Add(p);
				}
			}
			catch { }
			return libs;
		}

		internal static List<WallpaperEngineItem> ScanWallpapers(string customDir = "")
		{
			var results = new List<WallpaperEngineItem>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string installDir = FindInstallDir(customDir);
			List<string> roots = FindLibraryRoots(installDir, customDir);

			foreach (string root in roots)
			{
				// Allow customDir to point directly to a single wallpaper folder
				string directProject = Path.Combine(root, "project.json");
				if (File.Exists(directProject))
				{
					WallpaperEngineItem single = ParseProjectJson(directProject);
					if (single != null && seen.Add(single.ProjectJsonPath)) results.Add(single);
				}

				string[] subdirs;
				try { subdirs = Directory.GetDirectories(root); }
				catch { continue; }

				foreach (string sub in subdirs)
				{
					string pj = Path.Combine(sub, "project.json");
					if (!File.Exists(pj)) continue;
					WallpaperEngineItem item = ParseProjectJson(pj);
					if (item != null && seen.Add(item.ProjectJsonPath)) results.Add(item);
				}
			}

			results.Sort(delegate(WallpaperEngineItem a, WallpaperEngineItem b)
			{
				return b.LastModifiedUtc.CompareTo(a.LastModifiedUtc);
			});
			return results;
		}

		internal static WallpaperEngineItem ParseProjectJson(string projectJsonPath)
		{
			if (string.IsNullOrEmpty(projectJsonPath) || !File.Exists(projectJsonPath)) return null;
			try
			{
				string jsonText = ReadSharedText(projectJsonPath);
				var dict = Json.DeserializeObject(jsonText) as Dictionary<string, object>;
				if (dict == null) return null;

				string folder = Path.GetDirectoryName(projectJsonPath);
				string folderName = Path.GetFileName(folder);
				string title = StrVal(dict, "title");
				if (string.IsNullOrEmpty(title)) title = folderName;
				string type = StrVal(dict, "type").ToLowerInvariant();
				string previewName = StrVal(dict, "preview");
				string fileName = StrVal(dict, "file");

				string previewPath = ResolveLocalFile(folder, previewName);
				if (string.IsNullOrEmpty(previewPath))
				{
					foreach (string fallback in new[] { "preview.jpg", "preview.png", "preview.jpeg", "preview.bmp", "preview.gif" })
					{
						string cand = Path.Combine(folder, fallback);
						if (File.Exists(cand)) { previewPath = cand; break; }
					}
				}

				string contentFile = ResolveLocalFile(folder, fileName);
				string animatedPath = "";
				if (!string.IsNullOrEmpty(previewPath) && previewPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
					animatedPath = previewPath;
				else
				{
					string candGif = Path.Combine(folder, "preview.gif");
					if (File.Exists(candGif)) animatedPath = candGif;
				}
				if (string.IsNullOrEmpty(animatedPath) && !string.IsNullOrEmpty(contentFile) && contentFile.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
					animatedPath = contentFile;

				string staticImagePath = "";
				if (!string.IsNullOrEmpty(contentFile) && IsStaticImageFile(contentFile))
				{
					staticImagePath = contentFile;
				}
				else
				{
					string bestCandidate = FindLargestStaticImageInFolder(folder);
					long bestLen = !string.IsNullOrEmpty(bestCandidate) ? new FileInfo(bestCandidate).Length : 0;
					long prevLen = (!string.IsNullOrEmpty(previewPath) && File.Exists(previewPath)) ? new FileInfo(previewPath).Length : 0;
					if (!string.IsNullOrEmpty(bestCandidate) && bestLen > prevLen * 2)
						staticImagePath = bestCandidate;
					else if (!string.IsNullOrEmpty(previewPath) && IsStaticImageFile(previewPath))
						staticImagePath = previewPath;
					else if (!string.IsNullOrEmpty(previewPath))
						staticImagePath = previewPath;
					else if (!string.IsNullOrEmpty(animatedPath))
						staticImagePath = animatedPath;
				}

				if (string.IsNullOrEmpty(previewPath) && string.IsNullOrEmpty(staticImagePath) && string.IsNullOrEmpty(animatedPath)
					&& string.IsNullOrEmpty(contentFile) && !File.Exists(Path.Combine(folder, "scene.pkg")))
					return null;

				DateTime mtime = File.GetLastWriteTimeUtc(projectJsonPath);
				return new WallpaperEngineItem
				{
					FolderPath = folder,
					ProjectJsonPath = Path.GetFullPath(projectJsonPath),
					Title = title,
					Type = type,
					PreviewPath = !string.IsNullOrEmpty(previewPath) ? previewPath : staticImagePath,
					AnimatedPreviewPath = animatedPath,
					StaticImagePath = staticImagePath,
					ContentFilePath = contentFile,
					WorkshopId = folderName,
					LastModifiedUtc = mtime
				};
			}
			catch
			{
				return null;
			}
		}

		internal static WallpaperEngineItem ReadActiveDesktopWallpaper(string customDir = "")
		{
			string installDir = FindInstallDir(customDir);
			string cfgPath = FindConfigPath(installDir);
			if (string.IsNullOrEmpty(cfgPath) || !File.Exists(cfgPath)) return null;
			try
			{
				string text = ReadSharedText(cfgPath);
				var root = Json.DeserializeObject(text) as Dictionary<string, object>;
				if (root == null) return null;

				string projectRef = ExtractSelectedWallpaperPath(root);
				if (string.IsNullOrEmpty(projectRef)) return null;

				string norm = projectRef.Replace('/', '\\').Trim();
				if (File.Exists(norm))
				{
					if (Path.GetFileName(norm).Equals("project.json", StringComparison.OrdinalIgnoreCase))
						return ParseProjectJson(norm);
					string dir = Path.GetDirectoryName(norm);
					string pj = !string.IsNullOrEmpty(dir) ? Path.Combine(dir, "project.json") : "";
					if (!string.IsNullOrEmpty(pj) && File.Exists(pj)) return ParseProjectJson(pj);
					if (IsStaticImageFile(norm) || norm.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
					{
						return new WallpaperEngineItem
						{
							FolderPath = dir ?? "",
							ProjectJsonPath = norm,
							Title = Path.GetFileNameWithoutExtension(norm),
							Type = "image",
							PreviewPath = norm,
							AnimatedPreviewPath = norm.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? norm : "",
							StaticImagePath = norm,
							ContentFilePath = norm,
							WorkshopId = "",
							LastModifiedUtc = File.GetLastWriteTimeUtc(norm)
						};
					}
				}
				return null;
			}
			catch
			{
				return null;
			}
		}

		private static string ExtractSelectedWallpaperPath(Dictionary<string, object> root)
		{
			// Wallpaper Engine config.json typically nests general.user.selectedwallpapers.Monitor0.file
			var queue = new Queue<Dictionary<string, object>>();
			queue.Enqueue(root);
			while (queue.Count > 0)
			{
				Dictionary<string, object> cur = queue.Dequeue();
				object sw;
				if (cur.TryGetValue("selectedwallpapers", out sw))
				{
					var map = sw as Dictionary<string, object>;
					if (map != null)
					{
						string hit = PickMonitorFile(map, "Monitor0");
						if (!string.IsNullOrEmpty(hit)) return hit;
						foreach (KeyValuePair<string, object> kv in map)
						{
							hit = PickMonitorFile(map, kv.Key);
							if (!string.IsNullOrEmpty(hit)) return hit;
						}
					}
				}
				object single;
				if (cur.TryGetValue("selectedwallpaper", out single))
				{
					string s = Convert.ToString(single);
					if (!string.IsNullOrEmpty(s)) return s;
				}
				foreach (object v in cur.Values)
				{
					var sub = v as Dictionary<string, object>;
					if (sub != null) queue.Enqueue(sub);
				}
			}
			return "";
		}

		private static string PickMonitorFile(Dictionary<string, object> map, string monitorKey)
		{
			object obj;
			if (!map.TryGetValue(monitorKey, out obj)) return "";
			var mon = obj as Dictionary<string, object>;
			if (mon == null) return Convert.ToString(obj) ?? "";
			string file = StrVal(mon, "file");
			return file;
		}

		internal static bool LaunchWallpaperEngine(string customDir = "")
		{
			try
			{
				string exe = FindExecutable(FindInstallDir(customDir));
				if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
				{
					Process.Start(new ProcessStartInfo { FileName = exe, Arguments = "", UseShellExecute = true });
					return true;
				}
				Process.Start(new ProcessStartInfo { FileName = "steam://rungameid/431960", UseShellExecute = true });
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal static bool ApplyToDesktop(string projectJsonPath, string customDir = "")
		{
			if (string.IsNullOrEmpty(projectJsonPath) || !File.Exists(projectJsonPath)) return false;
			try
			{
				string exe = FindExecutable(FindInstallDir(customDir));
				if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;
				var psi = new ProcessStartInfo
				{
					FileName = exe,
					Arguments = "-control openWallpaper -file \"" + projectJsonPath + "\"",
					UseShellExecute = false,
					CreateNoWindow = true
				};
				using (Process proc = Process.Start(psi))
				{
					if (proc != null) proc.WaitForExit(3000);
				}
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static string ResolveLocalFile(string folder, string rel)
		{
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(rel)) return "";
			try
			{
				string norm = rel.Replace('/', '\\').Trim();
				string full = Path.IsPathRooted(norm) ? norm : Path.Combine(folder, norm);
				return File.Exists(full) ? full : "";
			}
			catch { return ""; }
		}

		private static string FindLargestStaticImageInFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return "";
			string best = "";
			long bestSize = 0;
			try
			{
				foreach (string file in Directory.GetFiles(folder))
				{
					if (!IsStaticImageFile(file)) continue;
					string name = Path.GetFileName(file) ?? "";
					if (name.StartsWith("preview", StringComparison.OrdinalIgnoreCase)) continue;
					long len = new FileInfo(file).Length;
					if (len > bestSize) { bestSize = len; best = file; }
				}
			}
			catch { }
			return best;
		}

		private static bool IsStaticImageFile(string path)
		{
			string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
			return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp";
		}

		private static string ReadSharedText(string path)
		{
			using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			using (var sr = new StreamReader(fs, Encoding.UTF8))
			{
				return sr.ReadToEnd();
			}
		}

		private static string StrVal(Dictionary<string, object> d, string key)
		{
			object v;
			return (d != null && d.TryGetValue(key, out v) && v != null) ? Convert.ToString(v).Trim() : "";
		}

		private static bool ContainsIgnoreCase(List<string> list, string value)
		{
			foreach (string s in list)
			{
				if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return true;
			}
			return false;
		}
	}
}
