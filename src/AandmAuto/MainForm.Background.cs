using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		private NotifyIcon trayIcon;

		private bool backgroundMode;

		private bool backgroundBalloonShown;

		private bool changingWindowState;

		private int webViewAliveReasserts;

        private Timer runtimeHeartbeat;
        private bool runtimePulseBusy;
        private DateTime lastRuntimePulse=DateTime.MinValue,lastLifecyclePulse=DateTime.MinValue;
        private object runtimeController;
        private int runtimePulseCount,runtimePulseErrors,runtimeResumeCount;
		private CoreWebView2 memoryPolicyCore;
		private bool memoryPolicyUnsupported;
		private bool browserMemoryLow;
		private DateTime minimizedIdleSinceUtc;
		private readonly bool measureWithoutBrowserMemoryTarget = Environment.GetEnvironmentVariable("AANDM_IDLE_MEMORY_BASELINE") == "1";
		private bool slowIdleFrames;
		private int runtimeFramePolicyPage = -1;

        private bool wasMinimized;
        private bool repaintQueued;
        private int completedRestoreRepaints;

		private FormWindowState backgroundRestoreState = FormWindowState.Normal;

		private Rectangle backgroundRestoreBounds;
        private string ProfileTrayText(){string label="冒险与挖矿 · "+new AccountProfiles(AppPaths.BaseRoot).Label(AppPaths.ProfileId??"main");return label.Length>63?label.Substring(0,63):label;}

		private void SetupBackgroundMode()
		{
			if (previewMode) return;
			base.Resize += Background_Resize;
            if(Environment.GetEnvironmentVariable("AANDM_BACKGROUND_BASELINE")!="1") {
                runtimeHeartbeat=new Timer{Interval=100};
                runtimeHeartbeat.Tick+=delegate{KeepPageRunning();};runtimeHeartbeat.Start();
            }
			try
			{
				trayIcon = new NotifyIcon
				{
					Icon = base.Icon ?? SystemIcons.Application,
					Text = ProfileTrayText(),
					Visible = true
				};
				var menu = new ContextMenuStrip();
				menu.BackColor = Color.FromArgb(63, 51, 32);
				menu.ForeColor = Color.FromArgb(242, 232, 205);
				var showItem = new ToolStripMenuItem("显示主界面");
				showItem.Click += delegate { RestoreFromBackground(); };
				var exitItem = new ToolStripMenuItem("退出");
				exitItem.Click += delegate { Close(); };
				menu.Items.Add(showItem);
				menu.Items.Add(new ToolStripSeparator());
				menu.Items.Add(exitItem);
				trayIcon.ContextMenuStrip = menu;
				trayIcon.DoubleClick += delegate { RestoreFromBackground(); };
			}
			catch
			{
				trayIcon = null;
			}
			backgroundRestoreState = base.WindowState;
			backgroundRestoreBounds = base.Bounds;
		}

		private void Background_Resize(object sender, EventArgs e)
		{
			if (previewMode || changingWindowState) return;
			if (base.WindowState == FormWindowState.Minimized)
			{
                wasMinimized=true;
				UpdateGamePollInterval();
				// 最小化不再转入托盘：正常留在任务栏，只保证网页在后台继续运行。
				KeepPageRunning();
				return;
			}
            if(wasMinimized)
            {
                wasMinimized=false;
				UpdateGamePollInterval();
				KeepPageRunning();
                if(Program.CheckinLaunch||Program.HarvestLaunch)keepScheduledWindowOpen=true;
                QueueFullRepaint();
            }
			if (!backgroundMode)
			{
				backgroundRestoreState = base.WindowState;
				backgroundRestoreBounds = base.WindowState == FormWindowState.Normal ? base.Bounds : base.RestoreBounds;
			}
		}

        private void QueueFullRepaint()
        {
            if(repaintQueued || IsDisposed || !IsHandleCreated) return;
            repaintQueued=true;
            BeginInvoke((MethodInvoker)delegate {
                repaintQueued=false;
                if(IsDisposed || WindowState==FormWindowState.Minimized) return;
                // Wait for the restore layout before invalidating the entire
                // client tree, including the nested transparent labels.
                PerformLayout();
                Invalidate(true);
                WindowStyle.RedrawAll(Handle);
                completedRestoreRepaints++;
            });
        }

		/// <summary>
		/// 窗口最小化后 Chromium 会把页面标记为隐藏并暂停 requestAnimationFrame，
		/// 游戏和自动任务看起来就停了。这里把 WebView2 控制器保持为可见，
		/// 页面继续渲染和计时，但窗口本身保持最小化的正常行为（不隐藏到托盘）。
		/// 本版 SDK 没有公开的 CoreWebView2Controller 属性，控制器是私有字段，
		/// 因此先找属性、再退回私有字段。
		/// </summary>
		private bool IsMinimizedOrHostMinimized()
		{
			return WindowState == FormWindowState.Minimized || !Visible
				|| (dockHost != 0 && DockWindow.IsIconic(new IntPtr(dockHost)));
		}

		private async void KeepPageRunning()
		{
			if (webView == null || previewMode || IsDisposed || webView.CoreWebView2==null || Environment.GetEnvironmentVariable("AANDM_BACKGROUND_BASELINE")=="1") return;
            bool background = WindowState == FormWindowState.Minimized
                || (dockHost != 0 ? !WindowStyle.IsForeground(new IntPtr(dockHost)) : !WindowStyle.IsForeground(Handle));
            DateTime stamp=DateTime.UtcNow;
            bool active = AnyTaskRunning() || autoLoginReconnecting;
			UpdateBrowserMemoryPolicy(webView.CoreWebView2, stamp, active);
			if (runtimePulseBusy) return;
			bool slowFrames = gameSnapshotReady && !active && IsMinimizedOrHostMinimized();
			bool framePolicyChanged = slowFrames != slowIdleFrames || pageGeneration != runtimeFramePolicyPage;
            int interval = active ? 100 : 400;
            if (runtimeHeartbeat != null && runtimeHeartbeat.Interval != interval)
                runtimeHeartbeat.Interval = interval;
            int minimumGap = active ? 90 : (background ? 600 : 1000);
            if (!framePolicyChanged && (stamp-lastRuntimePulse).TotalMilliseconds < minimumGap) return;
            lastRuntimePulse=stamp;runtimePulseBusy=true;
			try
			{
				object controller = runtimeController;
				Type viewType = webView.GetType();
				System.Reflection.PropertyInfo property = viewType.GetProperty("CoreWebView2Controller",
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (controller==null&&property != null) controller = property.GetValue(webView, null);
				if (controller == null)
				{
					System.Reflection.FieldInfo field = viewType.GetField("_coreWebView2Controller",
						System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
					if (field != null) controller = field.GetValue(webView);
				}
				System.Reflection.PropertyInfo visible = controller==null?null:controller.GetType().GetProperty("IsVisible");
				if (visible != null && visible.CanWrite && !Equals(visible.GetValue(controller, null), true))
				{
					visible.SetValue(controller, true, null);
					webViewAliveReasserts++;
				}
                runtimeController=controller;
                var core=webView.CoreWebView2;
                if(core.IsSuspended){core.Resume();runtimeResumeCount++;}
                if((stamp-lastLifecyclePulse).TotalSeconds>=2) {
                    lastLifecyclePulse=stamp;
                    await ObserveRuntimeCall(core.CallDevToolsProtocolMethodAsync("Page.setWebLifecycleState","{\"state\":\"active\"}"));
                    await ObserveRuntimeCall(core.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled","{\"enabled\":true}"));
                }
                int generation=pageGeneration;
                await ObserveRuntimeCall(core.ExecuteScriptAsync("(function(){var b=window.__MWA_BACKGROUND;if(b){if(b.setIdle)b.setIdle("
                    + (slowFrames ? "true" : "false") + ");b.pulse(performance.now());}var a=window.__MWA_APP;if(a&&typeof a.backgroundPulse==='function')a.backgroundPulse();})()"));
                slowIdleFrames=slowFrames;
                runtimeFramePolicyPage=generation;
                runtimePulseCount++;
			}
			catch(Exception error)
			{
                runtimeController=null;runtimePulseErrors++;
                if(runtimePulseErrors==1&&!IsDisposed)AddLog("后台运行保护将自动重试："+error.Message,YellowColor);
			}
            finally{runtimePulseBusy=false;}
		}

		private void UpdateBrowserMemoryPolicy(CoreWebView2 core, DateTime stamp, bool active)
		{
			if (measureWithoutBrowserMemoryTarget) return;
			if (memoryPolicyCore != core)
			{
				memoryPolicyCore = core;
				memoryPolicyUnsupported = false;
				browserMemoryLow = false;
				minimizedIdleSinceUtc = DateTime.MinValue;
			}
			if (memoryPolicyUnsupported) return;
			bool idle = IsMinimizedOrHostMinimized() && !active;
			if (!idle) minimizedIdleSinceUtc = DateTime.MinValue;
			else if (minimizedIdleSinceUtc == DateTime.MinValue) minimizedIdleSinceUtc = stamp;
			bool useLow = idle && stamp - minimizedIdleSinceUtc >= TimeSpan.FromSeconds(20);
			if (browserMemoryLow == useLow) return;
			try
			{
				core.MemoryUsageTargetLevel = useLow ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
				browserMemoryLow = useLow;
			}
			catch (NotImplementedException) { memoryPolicyUnsupported = true; }
			catch (System.Runtime.InteropServices.COMException) { memoryPolicyUnsupported = true; }
			catch (InvalidOperationException) { memoryPolicyUnsupported = true; }
		}
        private async Task ObserveRuntimeCall(Task call)
        {
            if(await Task.WhenAny(call,Task.Delay(1500))==call){await call;return;}
            var observer=call.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            GC.KeepAlive(observer);
            throw new TimeoutException("浏览器后台响应延迟");
        }

		/// <summary>最小化期间由轮询定时器反复确认页面没有被浏览器暂停。</summary>
		private void KeepPageRunningIfMinimized()
		{
			if (base.WindowState == FormWindowState.Minimized) KeepPageRunning();
		}

		/// <summary>诊断用：能否通过反射拿到控制器并保持可见。</summary>
		internal bool ControllerIsVisible()
		{
			if (webView == null) return false;
			try
			{
				object controller = null;
				Type viewType = webView.GetType();
				System.Reflection.PropertyInfo property = viewType.GetProperty("CoreWebView2Controller",
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (property != null) controller = property.GetValue(webView, null);
				if (controller == null)
				{
					System.Reflection.FieldInfo field = viewType.GetField("_coreWebView2Controller",
						System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
					if (field != null) controller = field.GetValue(webView);
				}
				if (controller == null) return false;
				System.Reflection.PropertyInfo visible = controller.GetType().GetProperty("IsVisible");
				return visible != null && Equals(visible.GetValue(controller, null), true);
			}
			catch
			{
				return false;
			}
		}

		private void EnterBackgroundMode()
		{
			if (backgroundMode || previewMode || base.IsDisposed) return;
			if (trayIcon == null) return;
			backgroundMode = true;
			changingWindowState = true;
			try
			{
				// Never really minimize the WebView host.  Chromium marks a
				// minimized page hidden and stops requestAnimationFrame, which
				// makes the game and the automation appear frozen.  Restoring
				// to a normal window and moving it off-screen keeps the page
				// visibilityState "visible" and the game loop running.
				base.WindowState = FormWindowState.Normal;
				base.Location = new Point(-32000, -32000);
				// Change the extended style instead of ShowInTaskbar: the
				// latter recreates the form handle and would rebuild the
				// WebView2 control (and reload the game).
				WindowStyle.SetTaskbarVisible(base.Handle, false);
				if (trayIcon != null)
				{
					trayIcon.Visible = true;
					if (!backgroundBalloonShown)
					{
						backgroundBalloonShown = true;
						try
						{
							trayIcon.BalloonTipTitle = "助手继续在后台运行";
							trayIcon.BalloonTipText = "双击托盘图标或右键选择“显示主界面”恢复窗口。";
							trayIcon.ShowBalloonTip(3000);
						}
						catch
						{
						}
					}
				}
			}
			finally
			{
				changingWindowState = false;
			}
		}

		private void RestoreFromBackground()
		{
			if (previewMode || base.IsDisposed) return;
            if(dockHost!=0&&DockWindow.IsWindow(new IntPtr(dockHost))){DockWindow.Activate(new IntPtr(dockHost));return;}
			if (!backgroundMode)
			{
				if (base.WindowState == FormWindowState.Minimized) base.WindowState = FormWindowState.Normal;
				base.Show();
				DockWindow.Activate(base.Handle);
				bool prevTop = TopMost;
				TopMost = true;
				TopMost = prevTop;
				Activate();
				QueueFullRepaint();
				return;
			}
			changingWindowState = true;
			try
			{
				backgroundMode = false;
				WindowStyle.SetTaskbarVisible(base.Handle, true);
				base.WindowState = FormWindowState.Normal;
				if (backgroundRestoreBounds.Width > 40 && backgroundRestoreBounds.Height > 40)
				{
					base.Bounds = backgroundRestoreBounds;
				}
				base.Show();
				base.WindowState = backgroundRestoreState == FormWindowState.Maximized
					? FormWindowState.Maximized : FormWindowState.Normal;
				Activate();
				QueueFullRepaint();
			}
			finally
			{
				changingWindowState = false;
			}
		}

		private void DisposeBackgroundMode()
		{
            if(runtimeHeartbeat!=null){runtimeHeartbeat.Stop();runtimeHeartbeat.Dispose();runtimeHeartbeat=null;}
			if (trayIcon != null)
			{
				trayIcon.Visible = false;
				ContextMenuStrip menu = trayIcon.ContextMenuStrip;
				trayIcon.Dispose();
				trayIcon = null;
				if (menu != null)
				{
					menu.Dispose();
				}
			}
		}
	}
}
