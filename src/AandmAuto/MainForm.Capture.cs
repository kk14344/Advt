using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private readonly HashSet<string> contractCaptures = new HashSet<string>();
        private readonly Queue<Dictionary<string, object>> contractCaptureQueue = new Queue<Dictionary<string, object>>();
        private bool contractCaptureBusy;
        private readonly HashSet<string> savedContractCaptures = new HashSet<string>();

        private Task<string> AckContractCapture(Dictionary<string, object> item)
        {
            return webView.CoreWebView2.ExecuteScriptAsync("window.__MWA_CAPTURE_ACK&&window.__MWA_CAPTURE_ACK(" + json.Serialize(GetString(item,"generation")) + "," + GetInt(item,"id") + ")");
        }

        private async void ContractCaptureReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var data = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                if (data == null || GetString(data, "type") != "contractStone" || GetInt(data, "amount") <= 0) return;
                string key = GetString(data, "generation") + ":" + GetInt(data, "id");
                if (!contractCaptures.Add(key))
                {
                    if (savedContractCaptures.Contains(key)) await AckContractCapture(data);
                    return;
                }
                contractCaptureQueue.Enqueue(data);
                if (contractCaptureBusy) return;
                contractCaptureBusy = true;
                try
                {
                    while (contractCaptureQueue.Count > 0)
                    {
                        var item = contractCaptureQueue.Dequeue();
                        string itemKey = GetString(item, "generation") + ":" + GetInt(item, "id");
                        string target = null;
                        try
                        {
                            string folder = Environment.GetEnvironmentVariable("AANDM_CAPTURE_DIR");
                            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "冒险与挖矿-契约石截图");
                            Directory.CreateDirectory(folder);
                            string name = "契约石_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_x" + GetInt(item,"amount") + "_" + Guid.NewGuid().ToString("N").Substring(0,6) + ".png";
                            target = Path.Combine(folder, name);
                            using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                            }
                            savedContractCaptures.Add(itemKey);
                            await AckContractCapture(item);
                            AddLog("契约石×" + GetInt(item,"amount") + "，截图已保存：" + target, BlueColor);
                        }
                        catch
                        {
                            if (!savedContractCaptures.Contains(itemKey))
                            {
                                contractCaptures.Remove(itemKey);
                                try { if (target != null && File.Exists(target)) File.Delete(target); } catch { }
                            }
                            AddLog("契约石截图暂未保存，将自动重试。", RedColor);
                        }
                    }
                }
                finally { contractCaptureBusy = false; }
            }
            catch { }
        }
    }
}
