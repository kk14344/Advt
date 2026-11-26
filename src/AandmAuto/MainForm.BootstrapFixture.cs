using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private async Task RunBootstrapFixtureAsync()
        {
            pollTimer.Stop();
            dailyTaskTimer.Stop();
            string failure = null, result = null;
            BootstrapAssetCache cache = null;
            try
            {
                int port = int.Parse(Environment.GetEnvironmentVariable("AANDM_BOOTSTRAP_FIXTURE_PORT"));
                var origin = new Uri("http://127.0.0.1:" + port);
                cache = new BootstrapAssetCache(webView.CoreWebView2, origin, Path.Combine(AppPaths.BaseRoot, "fixture-cache"));
                webView.CoreWebView2.Navigate(origin.AbsoluteUri);
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < 20)
                {
                    await Task.Delay(100);
                    result = await ExecuteScriptAsync("window.fixtureResult||null");
                    if (result != "null") break;
                }
                await Task.Delay(300);
                if (result == "null") failure = "Fixture timed out";
            }
            catch (Exception error) { failure = error.ToString(); }
            File.WriteAllText(Path.Combine(DataRoot(), "asset-cache-fixture.json"), json.Serialize(new {
                error=failure, data=result==null?null:json.DeserializeObject(result),
                cache=cache==null?null:new {hits=cache.Hits,saved=cache.Saved,eligible=cache.EligibleResponses,
                    errors=cache.ReadErrors,rejected=cache.RejectedBodies,misses=cache.Misses}
            }));
            Close();
        }
    }
}
