from datetime import datetime, timedelta, timezone
from hashlib import sha256
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile
import json
import shutil


root = Path(__file__).resolve().parents[1]
desktop = Path.home() / 'Desktop'
out = desktop / '冒险与挖矿助手-优化版1.36'
archive = root.parent / 'output' / (out.name + '.zip')
if out.exists() or archive.exists():
    raise SystemExit('Release output already exists')

binary = root / 'dist' / '冒险与挖矿自动助手-优化版.exe'
reports = {
    'workflow': 'dist/task-test-results.json',
    'browser': 'dist/smoke-136-verified/smoke-result.json',
    'startup': 'dist/startup-performance.json',
    'embedded': 'dist/embedded-test-results.json',
    'cache': 'dist/bootstrap-cache-test.json',
    'publicCold': 'dist/public-136-cold-result.json',
    'publicWarm': 'dist/public-136-cachecold/public-game-check.json',
    'backgroundMain': 'dist/background-136-final/background-test-results.json',
    'schedule': 'dist/schedule-135-test-results.json',
    'text': 'dist/preview/ui-text-check.json',
    'render': 'dist/preview/render-checks.json',
}
checks = {name: json.loads((root / path).read_text('utf-8-sig')) for name, path in reports.items()}
for name, result in checks.items():
    if name not in ('browser', 'text'):
        assert result['result'] == 'passed', name
assert checks['embedded']['assemblyVersion'] == '1.36.0.0'
assert (root / reports['embedded']).stat().st_mtime >= binary.stat().st_mtime
assert checks['workflow']['workflowTests'] == 369 and checks['workflow']['miningBandScenarios'] == 135
assert checks['workflow']['workshopScenarios'] == 22
assert checks['startup']['checks'] == 13 and checks['text']['buttonFit']
assert checks['browser']['workshopStatusStable']
assert checks['browser']['restoreImagesStable'] and checks['browser']['fastDailyScheduling'] and checks['browser']['scriptReady']
assert checks['cache']['warmDifferentProfile']['hits'] == 1 and checks['cache']['corruptionRepair']['saved'] == 1
assert checks['publicCold']['hostCache']['stored'] == 99
warm = checks['publicWarm']
assert warm['hostCache']['hits'] == 99 or warm['resourceTiming']['transferredBytes'] == 0
background = checks['backgroundMain']
assert background['minimizedSeconds'] == 90 and background['requestsConserved']
assert background['goodProgressWindows'] == 18 and background['probeTimeouts'] == 0


def digest(path):
    return sha256(path.read_bytes()).hexdigest()


old = desktop / '冒险与挖矿助手-优化版1.35' / '冒险与挖矿自动助手-优化版1.35.exe'
assert digest(old) == 'c746e28aab3d51db429db873ff813548fabeae961e76d3fd31eccfd52e539ad0'
out.mkdir()
exe = out / '冒险与挖矿自动助手-优化版1.36.exe'
shutil.copy2(binary, exe)
shutil.copy2(root / 'README.md', out / 'README-功能说明.md')
shutil.copy2(root / '修改说明.md', out / '修改说明.md')
evidence = out / '验证记录'
evidence.mkdir()
for name, path in reports.items():
    shutil.copy2(root / path, evidence / (name + '.json'))

source = out / '冒险与挖矿助手-源码-1.36.zip'
source_files = [root / 'README.md', root / '修改说明.md']
for folder in ('src', 'lib', 'original', 'tools', 'tests'):
    source_files.extend(path for path in (root / folder).rglob('*') if path.is_file() and
                        '__pycache__' not in path.parts and path.suffix != '.pyc')
with ZipFile(source, 'w', ZIP_DEFLATED, compresslevel=9) as zip_file:
    for path in sorted(source_files):
        zip_file.write(path, path.relative_to(root).as_posix())
with ZipFile(source) as zip_file:
    assert zip_file.testzip() is None
    assert all(zip_file.read(name) == (root / name).read_bytes() for name in zip_file.namelist())

verification = {
    'version': '1.36.0', 'createdAt': datetime.now(timezone(timedelta(hours=8))).isoformat(),
    'result': 'passed', 'liveAccountTransactionsTested': False, 'checks': checks,
    'exeSHA256': digest(exe), 'sourceSHA256': digest(source), 'previousExeSHA256': digest(old),
    'limitations': ['The public startup checks used the login screen with no account.',
                    'The cold and warm startup durations are single samples with server queue variance.',
                    'Workshop and rune mutations tested against the offline response fixtures.'],
}
(out / 'verification.json').write_text(json.dumps(verification, ensure_ascii=False, indent=2), 'utf-8')
assert digest(exe) == digest(binary)
archive.parent.mkdir(exist_ok=True)
with ZipFile(archive, 'w', ZIP_DEFLATED, compresslevel=9) as zip_file:
    for path in sorted(out.rglob('*')):
        if path.is_file():
            zip_file.write(path, (Path(out.name) / path.relative_to(out)).as_posix())
with ZipFile(archive) as zip_file:
    assert zip_file.testzip() is None
    assert zip_file.read(out.name + '/' + exe.name) == exe.read_bytes()
print(json.dumps({'exe': str(exe), 'sha256': digest(exe), 'releaseZip': str(archive)}, ensure_ascii=False))
