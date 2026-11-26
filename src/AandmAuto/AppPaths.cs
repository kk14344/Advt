using System;
using System.IO;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;

namespace AandmAuto
{
	internal static class AppPaths
	{
		private static string cachedRoot;
        public static string ProfileId {get;private set;}
        public static void ConfigureProfile(string id) {ProfileId=ValidateProfileId(id);}
        public static string ValidateProfileId(string id) {
            if(string.IsNullOrEmpty(id)||id=="main")return "main";
            Guid parsed;if(!Guid.TryParseExact(id,"N",out parsed))throw new ArgumentException("账号标识无效");
            return parsed.ToString("N");
        }
        public static string ProfileRoot(string root,string id) {
            id=ValidateProfileId(id);return id=="main"?Path.GetFullPath(root):Path.Combine(Path.GetFullPath(root),"Profiles",id);
        }
        public static string ContextKey(string root,string id) {
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant()+"|"+ValidateProfileId(id)))).Replace("-","").Substring(0,24);
        }
#if TEST_PVP_EDITION
		private const string DefaultAppFolderName = "AandmAutoTest";
#else
		private const string DefaultAppFolderName = "AandmAuto";
#endif

		public static string InstanceName(string root,string id) {
            string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),DefaultAppFolderName);
            return "Local\\" + DefaultAppFolderName + "DesktopSingleInstance"+(ValidateProfileId(id)=="main"&&string.Equals(Path.GetFullPath(root),Path.GetFullPath(standard),StringComparison.OrdinalIgnoreCase)?"":"-"+ContextKey(root,id));
        }
        public static string SignalName(string kind,string root,string id) {
            if(kind=="ScheduledRun"&&InstanceName(root,id)=="Local\\AandmAutoDesktopSingleInstance")return "Local\\AandmAutoScheduledRun";
            return "Local\\AandmAuto"+kind+"-"+ContextKey(root,id);
        }
        public static string QuoteArgument(string value) {
            var result=new StringBuilder("\"");int slashes=0;
            foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1);result.Append(c);slashes=0;continue;}result.Append('\\',slashes);slashes=0;result.Append(c);}
            result.Append('\\',slashes*2);return result.Append('"').ToString();
        }
        public static string DataRoot {get {string path=ProfileRoot(BaseRoot,ProfileId);Directory.CreateDirectory(path);return path;}}

		public static string BaseRoot
		{
			get
			{
				if (!string.IsNullOrEmpty(cachedRoot))
				{
					return cachedRoot;
				}
				string text = Environment.GetEnvironmentVariable("AANDM_DATA_DIR");
				if (string.IsNullOrEmpty(text))
				{
					text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DefaultAppFolderName);
				}
				if (CanWrite(text))
				{
					cachedRoot = text;
					return text;
				}
				try
				{
					string directoryName = Path.GetDirectoryName(Application.ExecutablePath);
					if (!string.IsNullOrEmpty(directoryName))
					{
						string text2 = Path.Combine(directoryName, "AandmAutoData");
						if (CanWrite(text2))
						{
							cachedRoot = text2;
							return text2;
						}
					}
				}
				catch
				{
				}
				string text3 = Path.Combine(Path.GetTempPath(), "AandmAuto");
				CanWrite(text3);
				cachedRoot = text3;
				return text3;
			}
		}

		private static bool CanWrite(string directory)
		{
			try
			{
				Directory.CreateDirectory(directory);
				string path = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
				File.WriteAllText(path, "ok");
				File.Delete(path);
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}
