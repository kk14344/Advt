from pathlib import Path
import hashlib,json,shutil,zipfile
from datetime import datetime,timezone,timedelta
root=Path(__file__).resolve().parents[1]
out=Path(r'C:\Users\1\Desktop\冒险与挖矿助手-优化版1.35')
archive=root.parent/'output'/'冒险与挖矿助手-优化版1.35.zip'
def read(p):return json.loads((root/p).read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
if out.exists() or archive.exists():raise SystemExit('Release already exists; refusing to overwrite')
binary=next((root/'dist').glob('*.exe'))
reports={
 'workflow':'dist/task-test-results.json','browser':'dist/smoke-135-final/smoke-result.json',
 'backgroundMain':'dist/background-135-final/background-test-results.json',
 'backgroundChild':'dist/background-135-final/Profiles/13500000000000000000000000000001/background-test-results.json',
 'backgroundJavascript':'dist/background-js-test-results.json','profiles':'dist/profile-135-release/profile-test-results.json',
 'multiAccount':'dist/multi-135-release/multi-account-test-results.json','embedded':'dist/embedded-test-results.json',
 'startup':'dist/startup-performance.json','legacySchedule':'dist/schedule-test-results.json','schedule':'dist/schedule-135-test-results.json',
 'text':'dist/preview/ui-text-check.json','buttons':'dist/preview/button-layout-check.json',
 'render':'dist/preview/render-checks.json','font':'dist/preview/font-quality-checks.json',
 'research':'dist/preview/research-choice-checks.json','miningSettings':'dist/preview/mining-settings-checks.json',
 'miningDepth':'dist/selected-depth-test-results.json','routePerformance':'dist/mining-135-performance.json'}
data={key:read(path) for key,path in reports.items()}
for key,report in data.items():
 if key not in ('browser','text'):assert report['result']=='passed',key
for key in ('workflow','browser','multiAccount','embedded','schedule','text','render','backgroundMain','backgroundChild'):
 assert (root/reports[key]).stat().st_mtime>=binary.stat().st_mtime, f'Stale final binary report: {key}'
assert data['workflow']['workflowTests']==364 and data['workflow']['miningBandScenarios']==135
assert data['workflow']['escort134Scenarios']==34
assert data['embedded']['assemblyVersion']=='1.35.0.0'
assert data['schedule']['checks']==26 and not data['schedule']['registeredAnyWindowsTask']
assert data['text']['buttonFit'] and data['buttons']['ratio']=='3:1'
assert data['buttons']['fontPoints']==10.5 and data['buttons']['primaryFontPoints']==13.5
assert data['font']['sampleScale']==3 and not data['font']['fontChanged']
for key in ('browserReady','scriptReady','contractCaptureSaved','minimizedStillRunning','restoreImagesStable','fastDailyScheduling','collapseControlsWorking'):
 assert data['browser'][key],key
assert data['browser']['restoreChecks']==6
assert len(data['profiles']['checks'])==17 and not data['profiles']['registeredWindowsTask']
assert data['multiAccount']['accountCount']==3 and data['multiAccount']['minimizedSeconds']==65
assert len(data['multiAccount']['checks'])>=10 and data['multiAccount']['noRealAccountUsed']
for key in ('backgroundMain','backgroundChild'):
 assert data[key]['minimizedSeconds']==90 and data[key]['requestsConserved'] and data[key]['documentPreserved']
 assert data[key]['goodProgressWindows']==18 and data[key]['probeTimeouts']==0
old=Path(r'C:\Users\1\Desktop\冒险与挖矿助手-优化版1.34\冒险与挖矿自动助手-优化版1.34.exe')
old_hash=sha(old);assert old_hash=='9ab1044262b8a13e4b8d8368eb28a69dda91d689713cfa0577da1c587177bb76'
mobile=Path(r'C:\Users\1\Desktop\冒险与挖矿助手-手机版1.34\冒险与挖矿助手-手机版1.34.apk')
assert sha(mobile)=='b88763e0a4da31df2babb7d150395621470593272b066150ebbca01b1b8064eb'
assert sha(root/'src/fonts/general.ttf')=='43eedccb258a4299ce5b0027ed5f02a0167fc5035e52e0d7bbc61291fd5c31cc'
out.mkdir();exe=out/'冒险与挖矿自动助手-优化版1.35.exe';shutil.copy2(binary,exe)
for source,target in [('README.md','README-功能说明.md'),('修改说明.md','修改说明.md')]:shutil.copy2(root/source,out/target)
report_dir=out/'验证记录';report_dir.mkdir()
for key,path in reports.items():shutil.copy2(root/path,report_dir/(key+'.json'))
preview=out/'界面预览';preview.mkdir()
for p in (root/'dist/preview').glob('*.png'):shutil.copy2(p,preview/p.name)
files=[root/'README.md',root/'修改说明.md']
for folder in ('src','lib','original','tests','tools'):
 files.extend(p for p in (root/folder).rglob('*') if p.is_file() and '__pycache__' not in p.parts and p.suffix!='.pyc')
source_zip=out/'冒险与挖矿助手-源码-1.35.zip'
with zipfile.ZipFile(source_zip,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
 for p in sorted(files):z.write(p,p.relative_to(root).as_posix())
with zipfile.ZipFile(source_zip) as z:
 assert z.testzip() is None
 for name in z.namelist():assert z.read(name)==(root/name).read_bytes(),name
 for name in ('src/AandmAuto/MultiAccountForm.cs','src/AandmAuto/ScheduleBook.cs','src/AandmAuto/CompositedPanel.cs','tools/test_mining_135.cjs','tools/test_schedule_135.ps1'):assert name in z.namelist()
verification={'version':'1.35.0','createdAt':datetime.now(timezone(timedelta(hours=8))).isoformat(),
 'result':'offline-workflow-native-browser-and-multi-account-checks-passed','checks':data,
 'liveAccountTested':False,'mobileUpdated':False,'old134Unchanged':True,'old134ExeSHA256':old_hash,
 'mobile134Unchanged':True,'mobile134SHA256':sha(mobile),'exeSHA256':sha(exe),'sourceSHA256':sha(source_zip),'sourceFiles':len(files),
 'singleAccountLayoutPreserved':True,'multiAccountLayoutOptInOnly':True,'maxAccountsPerScreen':3,
 'syncScope':'assistant tasks only; game clicks and global settings remain independent',
 'limitations':['Real account tasks and resource consumption were not exercised.','Offline CPU route measurements do not measure live server latency.','Windows scheduled launch requires an existing interactive desktop login.','The multi-account native host was exercised with three offline WebView fixtures, without live game accounts.']}
(out/'verification.json').write_text(json.dumps(verification,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
assert sha(binary)==sha(exe) and sha(old)==old_hash
archive.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
 for p in sorted(out.rglob('*')):
  if p.is_file():z.write(p,(Path(out.name)/p.relative_to(out)).as_posix())
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 for name in z.namelist():assert z.read(name)==(out.parent/name).read_bytes(),name
print(json.dumps({'exe':str(exe),'exeBytes':exe.stat().st_size,'exeSHA256':sha(exe),'archive':str(archive),'archiveBytes':archive.stat().st_size,'sourceFiles':len(files)},ensure_ascii=False))
