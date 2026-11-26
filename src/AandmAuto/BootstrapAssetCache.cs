using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace AandmAuto
{
    internal sealed class BootstrapAssetCache
    {
        private const int PartLimit = 512 * 1024;
        private const long CacheLimit = 192L * 1024 * 1024;
        private static readonly Regex PartPath = new Regex(
            @"\A/assets-bootstrap/part\.([a-f0-9]{64})\.bin\z", RegexOptions.CultureInvariant);
        private readonly CoreWebView2 browser;
        private readonly Uri origin;
        private readonly string directory;

        internal int Hits { get; private set; }
        internal int Saved { get; private set; }
        internal int Misses { get; private set; }
        internal int EligibleResponses { get; private set; }
        internal int ReadErrors { get; private set; }
        internal int RejectedBodies { get; private set; }

        internal BootstrapAssetCache(CoreWebView2 browser, Uri origin, string root)
        {
            this.browser = browser;
            this.origin = origin;
            // Each server gets its own namespace. Public content can be shared across its profiles.
            directory = Path.Combine(root, Hash(System.Text.Encoding.UTF8.GetBytes(origin.GetLeftPart(UriPartial.Authority))));
            browser.AddWebResourceRequestedFilter(origin.GetLeftPart(UriPartial.Authority) + "/assets-bootstrap/part.*.bin",
                CoreWebView2WebResourceContext.Fetch);
            browser.WebResourceRequested += ServePart;
            browser.WebResourceResponseReceived += SavePart;
            Task.Run((Action)Trim);
        }

        private string IdentifyPart(CoreWebView2WebResourceRequest request)
        {
            Uri uri;
            if (request.Method != "GET" || !Uri.TryCreate(request.Uri, UriKind.Absolute, out uri) ||
                uri.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
            Match match = PartPath.Match(uri.AbsolutePath);
            if (!match.Success || request.Headers.Contains("Authorization") || request.Headers.Contains("Cookie") ||
                request.Headers.Contains("Range")) return null;
            return match.Groups[1].Value;
        }

        private async void ServePart(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            string digest = IdentifyPart(e.Request);
            if (digest == null) return;
            string path = Path.Combine(directory, digest + ".bin");
            if (!File.Exists(path)) { Misses++; return; }

            using (var deferral = e.GetDeferral())
            {
                try
                {
                    var read = Task.Run(() => ReadVerified(path, digest));
                    if (await Task.WhenAny(read, Task.Delay(200)) != read) { Misses++; return; }
                    byte[] bytes = await read;
                    if (bytes == null) { Misses++; return; }
                    e.Response = browser.Environment.CreateWebResourceResponse(new MemoryStream(bytes, false), 200, "OK",
                        "Content-Type: application/octet-stream\r\nCache-Control: public, max-age=31536000, immutable");
                    Hits++;
                }
                catch (Exception) { Misses++; }
            }
        }

        private async void SavePart(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                string digest = IdentifyPart(e.Request);
                if (digest == null || e.Response.StatusCode != 200) return;
                var headers = e.Response.Headers;
                if (headers.Contains("Set-Cookie") || !headers.Contains("Cache-Control")) return;
                var directives = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var values = headers.GetHeaders("Cache-Control"))
                {
                    while (values.HasCurrentHeader)
                    {
                        foreach (string value in values.Current.Value.Split(',')) directives.Add(value.Trim());
                        values.MoveNext();
                    }
                }
                if (!directives.Contains("public") || !directives.Contains("immutable") ||
                    directives.Contains("no-store") || directives.Any(part => part.StartsWith("private", StringComparison.OrdinalIgnoreCase))) return;
                EligibleResponses++;
                string path = Path.Combine(directory, digest + ".bin");
                if (File.Exists(path)) return;
                using (Stream stream = await e.Response.GetContentAsync())
                {
                    if (stream == null) { ReadErrors++; return; }
                    using (var buffer = new MemoryStream())
                    {
                        var block = new byte[65536];
                        int count;
                        // WebView supplies an apartment-bound COM stream after the response is ready.
                        // Only the detached byte array goes to a worker for hashing and disk writes.
                        while ((count = stream.Read(block, 0, block.Length)) > 0)
                        {
                            if (buffer.Length + count > PartLimit) { RejectedBodies++; return; }
                            buffer.Write(block, 0, count);
                        }
                        byte[] bytes = buffer.ToArray();
                        if (await Task.Run(() => StoreVerified(path, digest, bytes))) Saved++;
                        else if (!File.Exists(path)) RejectedBodies++;
                    }
                }
            }
            catch (Exception) { ReadErrors++; }
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static byte[] ReadVerified(string path, string digest)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) return null;
                if (file.Length > 0 && file.Length <= PartLimit)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.Length <= PartLimit && Hash(bytes) == digest) return bytes;
                }
                File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private bool StoreVerified(string path, string digest, byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > PartLimit || Hash(bytes) != digest || File.Exists(path)) return false;
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path);
                return true;
            }
            catch (IOException) { return false; } // Another account may have filled it first.
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private void Trim()
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                var files = new DirectoryInfo(directory).GetFiles("*.bin").OrderBy(file => file.LastWriteTimeUtc).ToArray();
                long size = files.Sum(file => file.Length);
                foreach (var file in files)
                {
                    if (size <= CacheLimit) break;
                    long bytes = file.Length;
                    try { file.Delete(); size -= bytes; }
                    catch (IOException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
