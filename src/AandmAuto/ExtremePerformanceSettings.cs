using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace AandmAuto
{
	internal static class ExtremePerformanceSettings
	{
		internal const string FileName = "global-settings.json";
		internal const string KeyExtremePerformance = "extremePerformanceMode";

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentProcess();

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

		[DllImport("psapi.dll", SetLastError = true)]
		private static extern bool EmptyWorkingSet(IntPtr hProcess);

		internal static string SettingsPath(string baseRoot)
		{
			string root = string.IsNullOrEmpty(baseRoot) ? AppPaths.BaseRoot : baseRoot;
			return Path.Combine(Path.GetFullPath(root), FileName);
		}

		internal static bool Load(string baseRoot)
		{
			try
			{
				string path = SettingsPath(baseRoot);
				if (!File.Exists(path)) return false;
				string raw = File.ReadAllText(path, Encoding.UTF8);
				if (string.IsNullOrWhiteSpace(raw)) return false;
				var serializer = new JavaScriptSerializer();
				var data = serializer.Deserialize<Dictionary<string, object>>(raw);
				if (data == null) return false;
				object value;
				if (data.TryGetValue(KeyExtremePerformance, out value) && value != null)
				{
					if (value is bool) return (bool)value;
					bool parsed;
					if (bool.TryParse(Convert.ToString(value), out parsed)) return parsed;
				}
			}
			catch
			{
			}
			return false;
		}

		internal static void Save(string baseRoot, bool enabled)
		{
			string path = SettingsPath(baseRoot);
			string dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			var serializer = new JavaScriptSerializer();
			Dictionary<string, object> data = null;
			try
			{
				if (File.Exists(path))
				{
					string existing = File.ReadAllText(path, Encoding.UTF8);
					if (!string.IsNullOrWhiteSpace(existing))
					{
						data = serializer.Deserialize<Dictionary<string, object>>(existing);
					}
				}
			}
			catch
			{
				data = null;
			}
			if (data == null) data = new Dictionary<string, object>();
			data[KeyExtremePerformance] = enabled;

			string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllText(temp, serializer.Serialize(data), Encoding.UTF8);
				if (File.Exists(path))
				{
					try
					{
						File.Replace(temp, path, null);
					}
					catch
					{
						File.Copy(temp, path, true);
					}
				}
				else
				{
					File.Move(temp, path);
				}
			}
			finally
			{
				try
				{
					if (File.Exists(temp)) File.Delete(temp);
				}
				catch
				{
				}
			}
		}

		internal static bool TrimCurrentProcessMemory()
		{
			try
			{
				GC.Collect(2, GCCollectionMode.Optimized, false);
				IntPtr process = GetCurrentProcess();
				if (SetProcessWorkingSetSize(process, new IntPtr(-1), new IntPtr(-1)))
				{
					return true;
				}
				return EmptyWorkingSet(process);
			}
			catch
			{
				return false;
			}
		}
	}
}
