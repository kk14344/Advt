using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AandmAuto
{
	internal sealed class SelfHealDecision
	{
		public bool Allowed;
		public int AttemptInWindow;
		public int MaxInWindow;
		public string Reason = "";
	}

	/// <summary>
	/// 挂机异常自愈控制器：
	/// 1. 滑动窗口配额保护（10 分钟内最多自动重载恢复 3 次，两次至少间隔 30 秒），防止无限重载死循环；
	/// 2. 仅记录崩溃/断线前正在运行的自动挖矿意图（180 秒有效），页面恢复就绪后自动续跑，绝不无故触发消费型任务；
	/// 3. 统计连续脚本轮询异常次数，持续失联达到阈值才升级为重载自愈。
	/// </summary>
	internal sealed class SelfHealWatchdog
	{
		internal const int MaxRecoveriesPerWindow = 3;
		internal const int WindowMinutes = 10;
		internal const int MinCooldownSeconds = 30;
		internal const int ResumeExpireSeconds = 180;
		internal const int PollErrorReloadThreshold = 10;

		private readonly List<DateTime> recoveryTimestamps = new List<DateTime>();
		private int consecutivePollErrors;
		private bool pendingResumeMining;
		private DateTime pendingResumeUntilUtc = DateTime.MinValue;
		private bool reloadScheduled;

		internal int ConsecutivePollErrors { get { return consecutivePollErrors; } }
		internal bool ReloadScheduled { get { return reloadScheduled; } set { reloadScheduled = value; } }

		internal void RecordPollSuccess()
		{
			consecutivePollErrors = 0;
		}

		internal int RecordPollError()
		{
			consecutivePollErrors++;
			return consecutivePollErrors;
		}

		internal void MarkPendingResumeMining(DateTime nowUtc)
		{
			pendingResumeMining = true;
			pendingResumeUntilUtc = nowUtc.AddSeconds(ResumeExpireSeconds);
		}

		internal void ClearPendingResume()
		{
			pendingResumeMining = false;
			pendingResumeUntilUtc = DateTime.MinValue;
		}

		internal bool ConsumePendingResumeMining(DateTime nowUtc)
		{
			if (!pendingResumeMining) return false;
			if (nowUtc > pendingResumeUntilUtc)
			{
				ClearPendingResume();
				return false;
			}
			ClearPendingResume();
			return true;
		}

		internal bool HasPendingResumeMining(DateTime nowUtc)
		{
			if (!pendingResumeMining) return false;
			if (nowUtc > pendingResumeUntilUtc)
			{
				ClearPendingResume();
				return false;
			}
			return true;
		}

		internal SelfHealDecision TryConsumeRecoveryQuota(DateTime nowUtc)
		{
			DateTime cutoff = nowUtc.AddMinutes(-WindowMinutes);
			for (int i = recoveryTimestamps.Count - 1; i >= 0; i--)
			{
				if (recoveryTimestamps[i] < cutoff) recoveryTimestamps.RemoveAt(i);
			}

			if (recoveryTimestamps.Count > 0)
			{
				DateTime last = recoveryTimestamps[recoveryTimestamps.Count - 1];
				double elapsed = (nowUtc - last).TotalSeconds;
				if (elapsed < MinCooldownSeconds)
				{
					return new SelfHealDecision
					{
						Allowed = false,
						AttemptInWindow = recoveryTimestamps.Count,
						MaxInWindow = MaxRecoveriesPerWindow,
						Reason = "自愈冷却中（距上次恢复不足 " + MinCooldownSeconds + " 秒）"
					};
				}
			}

			if (recoveryTimestamps.Count >= MaxRecoveriesPerWindow)
			{
				return new SelfHealDecision
				{
					Allowed = false,
					AttemptInWindow = recoveryTimestamps.Count,
					MaxInWindow = MaxRecoveriesPerWindow,
					Reason = WindowMinutes + " 分钟内已自动恢复 " + MaxRecoveriesPerWindow + " 次，已暂停继续重载以防死循环"
				};
			}

			recoveryTimestamps.Add(nowUtc);
			consecutivePollErrors = 0;
			return new SelfHealDecision
			{
				Allowed = true,
				AttemptInWindow = recoveryTimestamps.Count,
				MaxInWindow = MaxRecoveriesPerWindow,
				Reason = "ok"
			};
		}

		internal static void RunSmokeSelfTest(string outDir)
		{
			Directory.CreateDirectory(outDir);
			var wd = new SelfHealWatchdog();
			DateTime t0 = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);

			SelfHealDecision d1 = wd.TryConsumeRecoveryQuota(t0);
			SelfHealDecision dCooldown = wd.TryConsumeRecoveryQuota(t0.AddSeconds(10));
			SelfHealDecision d2 = wd.TryConsumeRecoveryQuota(t0.AddSeconds(40));
			SelfHealDecision d3 = wd.TryConsumeRecoveryQuota(t0.AddSeconds(80));
			SelfHealDecision d4 = wd.TryConsumeRecoveryQuota(t0.AddSeconds(120));
			SelfHealDecision dAfterWindow = wd.TryConsumeRecoveryQuota(t0.AddMinutes(11));

			wd.MarkPendingResumeMining(t0);
			bool resumeActive = wd.HasPendingResumeMining(t0.AddSeconds(30));
			bool resumeConsumed = wd.ConsumePendingResumeMining(t0.AddSeconds(31));
			bool resumeSecond = wd.ConsumePendingResumeMining(t0.AddSeconds(32));

			wd.MarkPendingResumeMining(t0);
			bool resumeExpired = !wd.ConsumePendingResumeMining(t0.AddSeconds(ResumeExpireSeconds + 5));

			int errs = 0;
			for (int i = 0; i < PollErrorReloadThreshold; i++) errs = wd.RecordPollError();
			bool pollThresholdReached = errs >= PollErrorReloadThreshold;
			wd.RecordPollSuccess();
			bool pollResetOk = wd.ConsecutivePollErrors == 0;

			bool passed = d1.Allowed && !dCooldown.Allowed && d2.Allowed && d3.Allowed && !d4.Allowed && dAfterWindow.Allowed
				&& resumeActive && resumeConsumed && !resumeSecond && resumeExpired
				&& pollThresholdReached && pollResetOk;

			var report = new Dictionary<string, object>
			{
				{ "channel", BuildEdition.UpdateChannel },
				{ "quotaOk", d1.Allowed && !dCooldown.Allowed && d2.Allowed && d3.Allowed && !d4.Allowed && dAfterWindow.Allowed },
				{ "resumeOk", resumeActive && resumeConsumed && !resumeSecond && resumeExpired },
				{ "pollEscalationOk", pollThresholdReached && pollResetOk },
				{ "passed", passed }
			};
			var js = new JavaScriptSerializer();
			File.WriteAllText(Path.Combine(outDir, "selfheal-smoke-" + BuildEdition.UpdateChannel + ".json"), js.Serialize(report), Encoding.UTF8);
		}
	}
}
