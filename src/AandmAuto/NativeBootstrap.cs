using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AandmAuto
{
	internal static class NativeBootstrap
	{
		[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr LoadLibrary(string fileName);

		[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool SetDllDirectory(string pathName);

		public static void Prepare()
		{
			try
			{
				string text = Path.Combine(AppPaths.DataRoot, "native");
				Directory.CreateDirectory(text);
				string text2 = Path.Combine(text, "WebView2Loader.dll");
				using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.WebView2Loader.dll"))
				{
					if (stream == null)
					{
						return;
					}
					using (FileStream destination = File.Create(text2))
					{
						stream.CopyTo(destination);
					}
				}
				SetDllDirectory(text);
				LoadLibrary(text2);
			}
			catch
			{
			}
		}
	}
}
