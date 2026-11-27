using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AandmAuto
{
	/// <summary>
	/// System-level background speed & stability guard:
	/// 1. Disables Windows 11 EcoQoS (Efficiency Mode) throttling on the assistant host process
	///    and its WebView2 browser/renderer child processes so minimized runs keep full CPU speed.
	/// 2. Prevents Windows system sleep / Modern Standby while an automated task is active
	///    (allows the display to turn off normally; immediately releases when idle or on exit).
	/// </summary>
	internal static class BackgroundPowerGuard
	{
		private const int ProcessPowerThrottling = 4;
		private const uint ProcessPowerThrottlingCurrentVersion = 1;
		private const uint ProcessPowerThrottlingExecutionSpeed = 0x1;
		private const uint ProcessSetInformation = 0x0200;
		private const uint Th32csSnapProcess = 0x00000002;
		private const uint EsContinuous = 0x80000000;
		private const uint EsSystemRequired = 0x00000001;

		[StructLayout(LayoutKind.Sequential)]
		private struct ProcessPowerThrottlingState
		{
			public uint Version;
			public uint ControlMask;
			public uint StateMask;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct ProcessEntry32W
		{
			public uint dwSize;
			public uint cntUsage;
			public uint th32ProcessID;
			public IntPtr th32DefaultHeapID;
			public uint th32ModuleID;
			public uint cntThreads;
			public uint th32ParentProcessID;
			public int pcPriClassBase;
			public uint dwFlags;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string szExeFile;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetProcessInformation(
			IntPtr hProcess,
			int processInformationClass,
			ref ProcessPowerThrottlingState processInformation,
			int processInformationSize);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr hObject);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

		[DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool Process32First(IntPtr hSnapshot, ref ProcessEntry32W lppe);

		[DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32W lppe);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern uint SetThreadExecutionState(uint esFlags);

		private static bool sleepPrevented;
		private static long lastEcoScanTimestamp;
		private static uint lastBrowserPid;

		static BackgroundPowerGuard()
		{
			AppDomain.CurrentDomain.ProcessExit += delegate { SetSystemSleepPrevention(false); };
		}

		internal static void SetSystemSleepPrevention(bool preventSleep)
		{
			if (sleepPrevented == preventSleep) return;
			try
			{
				uint flags = preventSleep ? (EsContinuous | EsSystemRequired) : EsContinuous;
				if (SetThreadExecutionState(flags) != 0)
				{
					sleepPrevented = preventSleep;
				}
			}
			catch
			{
			}
		}

		internal static void DisableEcoQoSForCurrentProcess()
		{
			try
			{
				using (Process proc = Process.GetCurrentProcess())
				{
					DisableEcoQoSHandle(proc.Handle);
				}
			}
			catch
			{
			}
		}

		internal static void EnsureEcoQoSDisabledForTree(uint browserProcessId, bool force = false)
		{
			long now = Stopwatch.GetTimestamp();
			bool pidChanged = browserProcessId != 0 && browserProcessId != lastBrowserPid;
			if (!force && !pidChanged && lastEcoScanTimestamp != 0 &&
				(now - lastEcoScanTimestamp) < Stopwatch.Frequency * 60L)
			{
				return;
			}
			lastEcoScanTimestamp = now;
			if (browserProcessId != 0) lastBrowserPid = browserProcessId;

			DisableEcoQoSForCurrentProcess();
			if (browserProcessId == 0) return;

			IntPtr snap = IntPtr.Zero;
			try
			{
				uint selfPid = (uint)Process.GetCurrentProcess().Id;
				snap = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
				if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return;

				var childrenMap = new Dictionary<uint, List<uint>>();
				var entry = new ProcessEntry32W { dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32W)) };
				if (Process32First(snap, ref entry))
				{
					do
					{
						List<uint> list;
						if (!childrenMap.TryGetValue(entry.th32ParentProcessID, out list))
						{
							list = new List<uint>();
							childrenMap[entry.th32ParentProcessID] = list;
						}
						list.Add(entry.th32ProcessID);
					}
					while (Process32Next(snap, ref entry));
				}

				var targets = new HashSet<uint>();
				var queue = new Queue<uint>();
				targets.Add(browserProcessId);
				queue.Enqueue(browserProcessId);
				List<uint> selfChildren;
				if (childrenMap.TryGetValue(selfPid, out selfChildren))
				{
					foreach (uint c in selfChildren)
					{
						if (targets.Add(c)) queue.Enqueue(c);
					}
				}
				int guard = 0;
				while (queue.Count > 0 && guard++ < 64)
				{
					uint pid = queue.Dequeue();
					List<uint> kids;
					if (childrenMap.TryGetValue(pid, out kids))
					{
						foreach (uint k in kids)
						{
							if (targets.Add(k)) queue.Enqueue(k);
						}
					}
				}

				foreach (uint pid in targets)
				{
					if (pid == 0 || pid == selfPid) continue;
					DisableEcoQoSPid(pid);
				}
			}
			catch
			{
			}
			finally
			{
				if (snap != IntPtr.Zero && snap != new IntPtr(-1))
				{
					try { CloseHandle(snap); } catch { }
				}
			}
		}

		private static void DisableEcoQoSPid(uint pid)
		{
			IntPtr handle = IntPtr.Zero;
			try
			{
				handle = OpenProcess(ProcessSetInformation, false, pid);
				if (handle != IntPtr.Zero) DisableEcoQoSHandle(handle);
			}
			catch
			{
			}
			finally
			{
				if (handle != IntPtr.Zero)
				{
					try { CloseHandle(handle); } catch { }
				}
			}
		}

		private static void DisableEcoQoSHandle(IntPtr processHandle)
		{
			if (processHandle == IntPtr.Zero) return;
			try
			{
				var state = new ProcessPowerThrottlingState
				{
					Version = ProcessPowerThrottlingCurrentVersion,
					ControlMask = ProcessPowerThrottlingExecutionSpeed,
					StateMask = 0 // Explicitly disable EcoQoS / Efficiency Mode
				};
				SetProcessInformation(processHandle, ProcessPowerThrottling, ref state, Marshal.SizeOf(typeof(ProcessPowerThrottlingState)));
			}
			catch
			{
			}
		}
	}
}
