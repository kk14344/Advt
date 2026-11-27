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
        private System.Reflection.PropertyInfo runtimeControllerVisibility;
        private bool quietGamePresentation;
        private bool runtimeFocusEmulation = true;
        private DateTime nextLoginSleepProbeUtc;
        private string loginSleepResult = "";
        private int runtimePulseCount,runtimePulseErrors,runtimeResumeCount;
		private CoreWebView2 memoryPolicyCore;
		private bool memoryPolicyUnsupported;
		private bool browserMemoryLow;
		private DateTime minimizedIdleSinceUtc;
		private readonly bool measureWithoutBrowserMemoryTarget = Environment.GetEnvironmentVariable("AANDM_IDLE_MEMORY_BASELINE") == "1";
		private bool slowIdleFrames;
		private int runtimeFramePolicyPage = -1;
		private DateTime backgroundSinceUtc = DateTime.MinValue;
		private bool extremePerfJsActive;
		private bool extremePerfJsSuspend;
		private bool extremePerfJsTask;
        private bool lowDefinitionJsActive;
		private bool bgBoostJsActive;
		private bool bgBoostJsLowRes;
		private bool pendingImmediatePulse;

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
			base.Activated += Background_Activated;
			base.Deactivate += Background_Deactivate;
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

		private void Background_Activated(object sender, EventArgs e)
		{
			if (previewMode || IsDisposed) return;
			backgroundSinceUtc = DateTime.MinValue;
			if (logUiDirty && currentBottomTab == "log") FlushDeferredLogUi();
			if (extremePerformanceMode || IsBackgroundBoostEnabled())
			{
				UpdateGamePollInterval();
				KeepPageRunning();
			}
		}

		private void Background_Deactivate(object sender, EventArgs e)
		{
			if (previewMode || IsDisposed) return;
			if (backgroundSinceUtc == DateTime.MinValue) backgroundSinceUtc = DateTime.UtcNow;
			if (extremePerformanceMode || IsBackgroundBoostEnabled())
			{
				UpdateGamePollInterval();
				KeepPageRunning();
			}
		}

		private void Background_Resize(object sender, EventArgs e)
		{
			if (previewMode || changingWindowState) return;
			if (base.WindowState == FormWindowState.Minimized)
			{
                wasMinimized=true;
				if (backgroundSinceUtc == DateTime.MinValue) backgroundSinceUtc = DateTime.UtcNow;
				UpdateGamePollInterval();
				// 最小化不再转入托盘：正常留在任务栏，只保证网页在后台继续运行。
				KeepPageRunning();
				if (extremePerformanceMode) TrimRuntimeMemory();
				return;
			}
            if(wasMinimized)
            {
                wasMinimized=false;
				backgroundSinceUtc = DateTime.MinValue;
				if (logUiDirty && currentBottomTab == "log") FlushDeferredLogUi();
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
            if (webView == null || previewMode || IsDisposed || webView.CoreWebView2 == null ||
                Environment.GetEnvironmentVariable("AANDM_BACKGROUND_BASELINE") == "1") return;
            var core = webView.CoreWebView2;
            bool minimized = IsMinimizedOrHostMinimized();
            bool background = minimized ||
                (dockHost != 0 ? !WindowStyle.IsForeground(new IntPtr(dockHost)) : !WindowStyle.IsForeground(Handle));
            DateTime stamp = DateTime.UtcNow;
            if (background)
            {
                if (backgroundSinceUtc == DateTime.MinValue) backgroundSinceUtc = stamp;
            }
            else
            {
                backgroundSinceUtc = DateTime.MinValue;
            }
            bool active = AnyTaskRunning() || autoLoginReconnecting;
            bool boost = IsBackgroundBoostEnabled();
            BackgroundPowerGuard.SetSystemSleepPrevention(boost && active);
            if (boost && (active || background))
            {
                try { BackgroundPowerGuard.EnsureEcoQoSDisabledForTree(core.BrowserProcessId); } catch { }
            }
            UpdateBrowserMemoryPolicy(core, stamp, active);
            bool extreme = extremePerformanceMode;
            bool lowDefinition = LowDefinitionLiveEnabled;
            bool suspendRender = extreme && !lowDefinition && gameSnapshotReady && (minimized ||
                (background && !autoLoginReconnecting && backgroundSinceUtc != DateTime.MinValue &&
                 stamp - backgroundSinceUtc >= TimeSpan.FromSeconds(5)));
            bool slowFrames = !lowDefinition && (extreme ? (suspendRender && !active) : (gameSnapshotReady && !active && minimized));
            bool boostActive = boost && active && background;
            bool boostLowRes = boost && !lowDefinition && gameSnapshotReady && minimized;
            bool quiet = extreme
                ? (suspendRender && !boostActive)
                : (slowFrames && !autoLoginEnabled && minimizedIdleSinceUtc != DateTime.MinValue &&
                   stamp - minimizedIdleSinceUtc >= TimeSpan.FromSeconds(20));
            bool canSleepLogin = CanSleepLogin(stamp);
            if (core.IsSuspended)
            {
                if (canSleepLogin) return;
                try { core.Resume(); runtimeResumeCount++; }
                catch (Exception error) { ReportRuntimePulseError(error); }
            }
            bool presentationChanged = quiet != quietGamePresentation;
            bool extremePolicyChanged = extreme != extremePerfJsActive || suspendRender != extremePerfJsSuspend || active != extremePerfJsTask;
            bool boostPolicyChanged = boostActive != bgBoostJsActive || boostLowRes != bgBoostJsLowRes;
            bool framePolicyChanged = lowDefinition != lowDefinitionJsActive || slowFrames != slowIdleFrames || extremePolicyChanged || boostPolicyChanged || pageGeneration != runtimeFramePolicyPage;
            int interval = extreme
                ? (suspendRender ? (active ? (boostActive ? 400 : 250) : 1500) : (active ? 400 : 1000))
                : (quiet ? 1500 : (active ? (boost ? 400 : 100) : 500));
            if (runtimeHeartbeat != null && runtimeHeartbeat.Interval != interval) runtimeHeartbeat.Interval = interval;

            // Restore the WebView immediately on task start or window restore,
            // even while a previous asynchronous pulse is finishing.
            if (presentationChanged)
            {
                try
                {
                    SetRuntimeControllerVisible(!quiet);
                    quietGamePresentation = quiet;
                    UpdateGamePollInterval();
                    lastLifecyclePulse = DateTime.MinValue;
                }
                catch (Exception error) { ReportRuntimePulseError(error); }
            }
            if (runtimePulseBusy)
            {
                if ((presentationChanged && !quiet) || extremePolicyChanged || boostPolicyChanged || lowDefinition != lowDefinitionJsActive) pendingImmediatePulse = true;
                return;
            }
            // When background boost is active, JS drives its own ~33Hz timer; avoid 100ms C# IPC contention
            // and push policy immediately on transitions + lightweight 1.2s keepalive heartbeat!
            int minimumGap = extreme
                ? (suspendRender ? (active ? (boostActive ? 1200 : 220) : 1400) : (active ? (boost ? 1200 : 180) : 900))
                : (quiet ? 1400 : (active ? (boost ? 1200 : 90) : (background ? 900 : 1200)));
            if (!presentationChanged && !framePolicyChanged &&
                (stamp - lastRuntimePulse).TotalMilliseconds < minimumGap) return;
            lastRuntimePulse = stamp;
            runtimePulseBusy = true;
            try
            {
                SetRuntimeControllerVisible(!quiet);
                if (core.IsSuspended) { core.Resume(); runtimeResumeCount++; }
                bool needActiveLifecycle = !quiet || (extreme && active);
                double lifecycleGapSec = boost ? 5.0 : 2.0;
                bool repairLifecycle = needActiveLifecycle && (stamp - lastLifecyclePulse).TotalSeconds >= lifecycleGapSec;
                if (repairLifecycle)
                {
                    lastLifecyclePulse = stamp;
                    await ObserveRuntimeCall(core.CallDevToolsProtocolMethodAsync("Page.setWebLifecycleState","{\"state\":\"active\"}"));
                }
                bool wantFocusEmulation = !quiet || (extreme && active);
                if (repairLifecycle || runtimeFocusEmulation != wantFocusEmulation)
                {
                    await ObserveRuntimeCall(core.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled",
                        wantFocusEmulation ? "{\"enabled\":true}" : "{\"enabled\":false}"));
                    runtimeFocusEmulation = wantFocusEmulation;
                }
                if (canSleepLogin && await TrySleepIdleLoginAsync(core, stamp)) return;
                int generation = pageGeneration;
                await ObserveRuntimeCall(core.ExecuteScriptAsync("(function(){var b=window.__MWA_BACKGROUND;if(b){if(b.setIdle)b.setIdle("
                    + (slowFrames ? "true" : "false")
                    + ");if(b.setExtremePerf)b.setExtremePerf("
                    + (extreme ? "true" : "false") + ","
                    + (suspendRender ? "true" : "false") + ","
                    + (active ? "true" : "false")
                    + ");if(b.setLowDefinition)b.setLowDefinition(" + (lowDefinition ? "true" : "false")
                    + ");if(b.setBackgroundBoost)b.setBackgroundBoost(" + (boostActive ? "true" : "false") + "," + (boostLowRes ? "true" : "false")
                    + ");b.pulse(performance.now());}var a=window.__MWA_APP;if(a&&typeof a.backgroundPulse==='function')a.backgroundPulse();})()"));
                lowDefinitionJsActive = lowDefinition;
                slowIdleFrames = slowFrames;
                extremePerfJsActive = extreme;
                extremePerfJsSuspend = suspendRender;
                extremePerfJsTask = active;
                bgBoostJsActive = boostActive;
                bgBoostJsLowRes = boostLowRes;
                runtimeFramePolicyPage = generation;
                runtimePulseCount++;
            }
            catch (Exception error) { ReportRuntimePulseError(error); }
            finally
            {
                runtimePulseBusy = false;
                if (pendingImmediatePulse && !IsDisposed && IsHandleCreated)
                {
                    pendingImmediatePulse = false;
                    BeginInvoke((MethodInvoker)KeepPageRunning);
                }
            }
        }

        private bool LowDefinitionLiveEnabled
        {
            get { bool enabled; return options.TryGetValue("lowDefinitionLive", out enabled) && enabled; }
        }

        private bool CanSleepLogin(DateTime nowUtc)
        {
            TimeSpan idleWait = extremePerformanceMode ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(20);
            return !LowDefinitionLiveEnabled && dockHost == 0 && gameSnapshotReady && WindowState == FormWindowState.Minimized &&
                !autoLoginEnabled && !autoLoginReconnecting && !AnyTaskRunning() &&
                minimizedIdleSinceUtc != DateTime.MinValue && nowUtc - minimizedIdleSinceUtc >= idleWait &&
                scheduleBook.Pending.Count == 0 && !scheduleBook.CheckinDue(nowUtc) && scheduleBook.DueHarvest(nowUtc) == null;
        }

        private async Task<bool> TrySleepIdleLoginAsync(CoreWebView2 core, DateTime stamp)
        {
            if (stamp < nextLoginSleepProbeUtc || runtimeControllerVisibility == null) return false;
            nextLoginSleepProbeUtc = stamp.AddSeconds(15);
            try
            {
                int generation = pageGeneration;
                string scene = await ExecuteLuaAsync("return require('scene.scene_manager'):GetCurrentSceneName()");
                if (scene != "\"login\"" || generation != pageGeneration || !CanSleepLogin(DateTime.UtcNow))
                {
                    if (Program.QuietResourceTest) loginSleepResult = "scene=" + scene;
                    return false;
                }
                bool asleep = await core.TrySuspendAsync();
                if (Program.QuietResourceTest) loginSleepResult = asleep ? "suspended" : "declined";
                if (asleep && (generation != pageGeneration || !CanSleepLogin(DateTime.UtcNow)))
                {
                    core.Resume();
                    runtimeResumeCount++;
                    return false;
                }
                return asleep;
            }
            catch (NotImplementedException) { if (Program.QuietResourceTest) loginSleepResult = "unavailable"; return false; }
            catch (InvalidOperationException e) { if (Program.QuietResourceTest) loginSleepResult = e.Message; return false; }
            catch (System.Runtime.InteropServices.COMException e) { if (Program.QuietResourceTest) loginSleepResult = e.Message; return false; }
        }

        private void SetRuntimeControllerVisible(bool visible)
        {
            if (runtimeController == null)
            {
                Type viewType = webView.GetType();
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance;
                var property = viewType.GetProperty("CoreWebView2Controller", flags);
                if (property != null) runtimeController = property.GetValue(webView, null);
                if (runtimeController == null)
                {
                    var field = viewType.GetField("_coreWebView2Controller", flags);
                    if (field != null) runtimeController = field.GetValue(webView);
                }
                runtimeControllerVisibility = runtimeController == null ? null : runtimeController.GetType().GetProperty("IsVisible");
            }
            if (runtimeControllerVisibility != null && runtimeControllerVisibility.CanWrite &&
                !Equals(runtimeControllerVisibility.GetValue(runtimeController, null), visible))
            {
                runtimeControllerVisibility.SetValue(runtimeController, visible, null);
                if (visible) webViewAliveReasserts++;
            }
        }

        private void ReportRuntimePulseError(Exception error)
        {
            runtimeController = null;
            runtimeControllerVisibility = null;
            runtimePulseErrors++;
            if (runtimePulseErrors == 1 && !IsDisposed) AddLog("后台运行保护将自动重试：" + error.Message, YellowColor);
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
			bool idle = IsMinimizedOrHostMinimized() && !active;
			if (!idle) minimizedIdleSinceUtc = DateTime.MinValue;
			else if (minimizedIdleSinceUtc == DateTime.MinValue) minimizedIdleSinceUtc = stamp;
			if (memoryPolicyUnsupported) return;
			bool useLow = extremePerformanceMode || (idle && stamp - minimizedIdleSinceUtc >= TimeSpan.FromSeconds(20));
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
				backgroundSinceUtc = DateTime.MinValue;
				if (logUiDirty && currentBottomTab == "log") FlushDeferredLogUi();
				base.Show();
				DockWindow.Activate(base.Handle);
				bool prevTop = TopMost;
				TopMost = true;
				TopMost = prevTop;
				Activate();
				KeepPageRunning();
				QueueFullRepaint();
				return;
			}
			changingWindowState = true;
			try
			{
				backgroundMode = false;
				backgroundSinceUtc = DateTime.MinValue;
				WindowStyle.SetTaskbarVisible(base.Handle, true);
				base.WindowState = FormWindowState.Normal;
				if (backgroundRestoreBounds.Width > 40 && backgroundRestoreBounds.Height > 40)
				{
					base.Bounds = backgroundRestoreBounds;
				}
				base.Show();
				base.WindowState = backgroundRestoreState == FormWindowState.Maximized
					? FormWindowState.Maximized : FormWindowState.Normal;
				if (logUiDirty && currentBottomTab == "log") FlushDeferredLogUi();
				Activate();
				KeepPageRunning();
				QueueFullRepaint();
			}
			finally
			{
				changingWindowState = false;
			}
		}

		private void TriggerSoftBackgroundWake()
		{
			if (previewMode || IsDisposed || webView == null || webView.CoreWebView2 == null) return;
			lastRuntimePulse = DateTime.MinValue;
			lastLifecyclePulse = DateTime.MinValue;
			try { BackgroundPowerGuard.EnsureEcoQoSDisabledForTree(webView.CoreWebView2.BrowserProcessId, true); } catch { }
			KeepPageRunning();
		}

		private void DisposeBackgroundMode()
		{
			BackgroundPowerGuard.SetSystemSleepPrevention(false);
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
