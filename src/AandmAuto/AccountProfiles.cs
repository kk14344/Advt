using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
    public sealed class AccountProfile { public string Id {get;set;} public string Name {get;set;} }
    public sealed class ProfileCredential {public string Username {get;set;} public string Password {get;set;} public bool AutoLogin {get;set;} }
    public sealed class AccountProfiles
    {
        private readonly string root;
        private readonly JavaScriptSerializer json=new JavaScriptSerializer();
        public AccountProfiles(string baseRoot){root=Path.GetFullPath(baseRoot);Directory.CreateDirectory(root);}
        public string Root(string id){return AppPaths.ProfileRoot(root,id);}
        private string Catalog {get{return Path.Combine(root,"account-profiles.json");}}
        public List<AccountProfile> Read() {
            var list=File.Exists(Catalog)?json.Deserialize<List<AccountProfile>>(File.ReadAllText(Catalog,Encoding.UTF8)):new List<AccountProfile>();
            list=list??new List<AccountProfile>();var result=new List<AccountProfile>{new AccountProfile{Id="main",Name="主账号"}};
            var ids=new HashSet<string>{"main"};foreach(var entry in list)if(entry!=null){string id=AppPaths.ValidateProfileId(entry.Id);if(ids.Add(id))result.Add(new AccountProfile{Id=id,Name=entry.Name??("账号 "+id.Substring(0,6))});}
            return result;
        }
        public ProfileCredential ReadCredential(string id) {
            string path=Path.Combine(Root(id),"account.dat");var value=new ProfileCredential{Username="",Password=""};
            if(!File.Exists(path))return value;
            var data=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));object item;
            if(data.TryGetValue("username",out item))value.Username=Convert.ToString(item);
            if(data.TryGetValue("password",out item)&&item!=null)value.Password=Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Convert.ToString(item)),null,DataProtectionScope.CurrentUser));
            if(data.TryGetValue("autoLogin",out item)&&item is bool)value.AutoLogin=(bool)item;
            return value;
        }
        private void Write(string path,object data) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,json.Serialize(data),Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        private Dictionary<string,object> ReadAppearanceOptions(string path) {
            if(!File.Exists(path))return new Dictionary<string,object>();
            var result=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));
            if(result==null)throw new InvalidDataException("账号设置文件格式有误。");
            return result;
        }
        internal int ReadTheme(string id) {
            object value;
            int theme;
            var options=ReadAppearanceOptions(Path.Combine(Root(id),"options.json"));
            return options.TryGetValue("uiTheme",out value)&&int.TryParse(Convert.ToString(value),out theme)
                &&theme>=UiTheme.ThemeClassic&&theme<=UiTheme.ThemeLiquidGlass?theme:UiTheme.ThemeClassic;
        }
        internal void SaveTheme(string id,int theme) {
            if(theme<UiTheme.ThemeClassic||theme>UiTheme.ThemeLiquidGlass)throw new ArgumentOutOfRangeException("theme");
            string path=Path.Combine(Root(id),"options.json");
            var options=ReadAppearanceOptions(path);
            options["uiTheme"]=theme;
            Write(path,options);
        }
        private void CopyOptionsForNewAccount(string templateRoot, string id)
        {
            string source = Path.Combine(templateRoot, "options.json");
            if (!File.Exists(source)) return;
            string target = Path.Combine(Root(id), "options.json");
            Dictionary<string, object> options = null;
            try { options = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(source, Encoding.UTF8)); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            if (options != null && options.Remove("battleReplaySpeed")) Write(target, options);
            else File.Copy(source, target, false);
        }

        public AccountProfile Save(string id,string username,string password,bool autoLogin,string templateRoot) {
            username=(username??"").Trim();if(username.Length==0||string.IsNullOrEmpty(password))throw new InvalidOperationException("请填写账号和密码");
            using(var guard=new Mutex(false,AppPaths.SignalName("Catalog",root,"main"))) {
                bool held=false;try{
                    try{held=guard.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("账号列表正在保存，请稍后重试");
                    var entries=Read();bool adding=string.IsNullOrEmpty(id);id=adding?Guid.NewGuid().ToString("N"):AppPaths.ValidateProfileId(id);
                    AccountProfile selected=null;foreach(var p in entries){if(p.Id==id)selected=p;else if(string.Equals(ReadCredential(p.Id).Username,username,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("该账号已在列表中，请打开原有窗口");}
                    if(!adding&&selected==null)throw new InvalidOperationException("账号不存在");
                    if(selected==null){selected=new AccountProfile{Id=id};entries.Add(selected);}selected.Name=username;
                    if(!adding&&!string.Equals(ReadCredential(id).Username,username,StringComparison.OrdinalIgnoreCase)) {
                        Directory.CreateDirectory(Root(id));File.WriteAllText(Path.Combine(Root(id),"browser-reset-required"),"account changed");
                    }
                    Write(Path.Combine(Root(id),"account.dat"),new Dictionary<string,object>{{"username",username},{"password",Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password),null,DataProtectionScope.CurrentUser))},{"autoLogin",autoLogin}});
                    if (adding && !string.IsNullOrEmpty(templateRoot)) CopyOptionsForNewAccount(templateRoot, id);
                    Write(Catalog,entries);return selected;
                }finally{if(held)guard.ReleaseMutex();}
            }
        }
        public string Label(string id){try{string user=ReadCredential(id).Username;if(!string.IsNullOrEmpty(user))return user;}catch{}foreach(var p in Read())if(p.Id==id)return p.Name;return "账号 "+id.Substring(0,6);}
        public bool IsRunning(string id){try{using(Mutex.OpenExisting(AppPaths.InstanceName(root,id)))return true;}catch(WaitHandleCannotBeOpenedException){return false;}}
        public bool Activate(string id){try{using(var signal=EventWaitHandle.OpenExisting(AppPaths.SignalName("Activate",root,id))){signal.Set();return true;}}catch(WaitHandleCannotBeOpenedException){return false;}}
        public ProcessStartInfo LaunchInfo(string exe,string id) {
            id=AppPaths.ValidateProfileId(id);var info=new ProcessStartInfo(exe,"--profile "+id+" --data-root "+AppPaths.QuoteArgument(root)){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(exe)};
            info.EnvironmentVariables["AANDM_DATA_DIR"]=root;return info;
        }
        public void Open(string id){if(IsRunning(id)&&Activate(id))return;using(var process=Process.Start(LaunchInfo(Application.ExecutablePath,id))){} }
    }
}
