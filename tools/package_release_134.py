from pathlib import Path
import hashlib, json, shutil, zipfile
from datetime import datetime, timezone, timedelta

root = Path(__file__).resolve().parents[1]
out = Path(r'C:\Users\1\Desktop\冒险与挖矿助手-优化版1.34')
archive = root.parent / 'output' / '冒险与挖矿助手-优化版1.34.zip'
if out.exists() or archive.exists():
    raise SystemExit('Release already exists; refusing to overwrite')

def read(path):
    return json.loads((root / path).read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

binary = next((root / 'dist').glob('*.exe'))
reports = {
    'workflow': 'dist/task-test-results.json',
    'browser': 'dist/smoke-134-release/smoke-result.json',
    'backgroundMain': 'dist/background-134-release/background-test-results.json',
    'backgroundChild': 'dist/background-134-release/Profiles/13400000000000000000000000000001/background-test-results.json',
    'backgroundJavascript': 'dist/background-js-test-results.json',
    'profiles': 'dist/profile-134-release/profile-test-results.json',
    'embedded': 'dist/embedded-test-results.json',
    'startup': 'dist/startup-performance.json',
    'schedule': 'dist/schedule-test-results.json',
    'text': 'dist/preview/ui-text-check.json',
    'buttons': 'dist/preview/button-layout-check.json',
    'render': 'dist/preview/render-checks.json',
    'font': 'dist/preview/font-quality-checks.json',
    'research': 'dist/preview/research-choice-checks.json',
    'miningSettings': 'dist/preview/mining-settings-checks.json',
    'miningDepth': 'dist/selected-depth-test-results.json',
}
data = {key: read(path) for key, path in reports.items()}
for key, report in data.items():
    if key not in ('browser', 'text'):
        assert report['result'] == 'passed', key
for key in ('workflow', 'browser', 'backgroundMain', 'backgroundChild', 'embedded', 'profiles', 'text'):
    assert (root / reports[key]).stat().st_mtime >= binary.stat().st_mtime, f'Report predates binary: {key}'
assert data['workflow']['workflowTests'] == 364
assert data['workflow']['escort134Scenarios'] == 34
assert data['workflow']['miningBandScenarios'] == 128
assert data['workflow']['retry130Scenarios'] == 15
assert data['workflow']['push132Scenarios'] == 12
assert data['embedded']['assemblyVersion'] == '1.34.0.0'
assert data['text']['buttonFit'] and data['text']['font'] == '青鸟华光简粗圆'
assert data['buttons']['ratio'] == '3:1' and data['buttons']['ink'] == '#000000'
assert data['buttons']['primaryFontPoints'] == 13.5 and data['buttons']['fontPoints'] == 10.5
assert data['font']['sampleScale'] == 3 and not data['font']['fontChanged']
for key in ('browserReady', 'scriptReady', 'contractCaptureSaved', 'minimizedStillRunning', 'restoreImagesStable', 'fastDailyScheduling', 'collapseControlsWorking'):
    assert data['browser'][key], key
assert data['browser']['restoreChecks'] == 6
assert len(data['profiles']['checks']) == 17
assert data['schedule']['checks'] == 10 and not data['profiles']['registeredWindowsTask']
for key in ('backgroundMain', 'backgroundChild'):
    b = data[key]
    assert b['minimizedSeconds'] == 90 and b['documentPreserved'] and b['requestsConserved']
    assert b['goodProgressWindows'] == 18 and b['probeTimeouts'] == 0
    assert b['noRealAccountUsed'] and b['noRealNetworkMutations']
assert read('dist/processes-134-final.json')['bothBackgroundRunning']
old = Path(r'C:\Users\1\Desktop\冒险与挖矿助手-优化版1.33\冒险与挖矿自动助手-优化版1.33.exe')
old_hash = sha(old)
assert old_hash == '36a2778393a2f50a9b8937e4ac5f9e7e67d6cd99605fc9e45966dab0755499d6'
assert (root / 'src/fonts/general.ttf').read_bytes() == (root.parent / 'assistant-1.33/src/fonts/general.ttf').read_bytes()
out.mkdir()
exe = out / '冒险与挖矿自动助手-优化版1.34.exe'
shutil.copy2(binary, exe)
shutil.copy2(root / 'README.md', out / 'README-功能说明.md')
shutil.copy2(root / '修改说明.md', out / '修改说明.md')
report_dir = out / '验证记录'
report_dir.mkdir()
for key, path in reports.items():
    shutil.copy2(root / path, report_dir / (key + '.json'))
preview = out / '界面预览'
preview.mkdir()
for p in (root / 'dist/preview').glob('*.png'):
    shutil.copy2(p, preview / p.name)
source_zip = out / '冒险与挖矿助手-源码-1.34.zip'
files = [root / 'README.md', root / '修改说明.md']
for folder in ('src', 'lib', 'original', 'tests', 'tools'):
    files.extend(p for p in (root / folder).rglob('*') if p.is_file() and '__pycache__' not in p.parts and p.suffix != '.pyc')
with zipfile.ZipFile(source_zip, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for p in sorted(files):
        z.write(p, p.relative_to(root).as_posix())
with zipfile.ZipFile(source_zip) as z:
    assert z.testzip() is None
    for name in z.namelist():
        assert z.read(name) == (root / name).read_bytes(), name
    for name in ('src/daily_cart.lua', 'src/AandmAuto/CartOptionsForm.cs', 'tools/test_escort_134.cjs', 'tests/fixtures/escort-primary-134.json', 'src/background_guard.js'):
        assert name in z.namelist()
verification = {
    'version': '1.34.0', 'createdAt': datetime.now(timezone(timedelta(hours=8))).isoformat(),
    'result': 'offline-native-handler-and-browser-checks-passed', 'checks': data,
    'liveAccountTested': False, 'realCartResourceConsumptionTested': False,
    'cart': {'highestGrade': 4, 'transportStartsPerDailyRun': 1, 'claimBeforeQuotaCheck': True,
             'freeRefreshThenNativeSpecify': True, 'fallbackToCurrentGrade': True,
             'bulkTransportQuotaPurchase': True, 'interceptPriority': [4, 3],
             'compareBattlePower': False, 'attemptSameTripOnceIncludingLoss': True,
             'targetRefreshBudget': 'unlimited', 'buyInterceptionAttempts': False,
             'reconcileUncertainWrites': True, 'nativeHandlersExecutedInTests': True},
    'publicPrimarySource': read('tests/fixtures/escort-primary-134.json')['source'],
    'old133ExeSHA256': old_hash, 'old133Unchanged': True, 'mobileUpdated': False,
    'exeSHA256': sha(exe), 'sourceSHA256': sha(source_zip), 'sourceFiles': len(files),
    'limitations': ['Native game handlers executed against an offline server model; no real account or diamonds used.',
                    'Unlimited target refresh can keep daily running until an eligible target appears or diamonds run out.',
                    'Uncertain target refresh reads the current list before considering a new refresh; exact spend after a lost refresh ACK is left to the game balance.']
}
(out / 'verification.json').write_text(json.dumps(verification, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
assert sha(binary) == sha(exe) == verification['exeSHA256']
assert sha(old) == old_hash
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for p in sorted(out.rglob('*')):
        if p.is_file():
            z.write(p, (Path(out.name) / p.relative_to(out)).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for name in z.namelist():
        assert z.read(name) == (out.parent / name).read_bytes(), name
print(json.dumps({'exe': str(exe), 'exeBytes': exe.stat().st_size, 'exeSHA256': sha(exe),
                  'archive': str(archive), 'archiveBytes': archive.stat().st_size,
                  'sourceFiles': len(files), 'old133Unchanged': sha(old) == old_hash}, ensure_ascii=False))
