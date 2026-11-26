import json
import shutil
from hashlib import sha256
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


root = Path(__file__).resolve().parents[1]
release = Path.home() / 'Desktop' / '冒险与挖矿助手-优化版1.43'
archive = root.parent / 'output' / (release.name + '.zip')
binary = root / 'dist' / '冒险与挖矿自动助手-优化版.exe'
reports = ['task-test-results.json', 'refine-test-results.json', 'followup-test-results.json',
           'embedded-test-results.json', 'background-js-test-results.json', 'public-input-check.json',
           'multi-account-test-results.json', 'harvest-test-results.json', 'checkin-test-results.json',
           'schedule-135-test-results.json']
results = {name: json.loads((root / 'dist' / name).read_text('utf-8-sig')) for name in reports}
assert all(check['result'] == 'passed' for check in results.values())
assert results['task-test-results.json']['workflowTests'] == 400
assert results['task-test-results.json']['refine142Scenarios'] == 8
assert results['task-test-results.json']['daily142Scenarios'] == 10
assert results['task-test-results.json']['miningBandScenarios'] == 137
assert results['refine-test-results.json']['taps'] >= 120
assert results['refine-test-results.json']['captureErrors'] == 0
assert results['refine-test-results.json']['minimizedTaps'] >= 120
assert results['refine-test-results.json']['scheduledTaskRegistration'] == 'verified'
assert results['followup-test-results.json']['windowsTriggers'] == 1
assert results['followup-test-results.json']['checks']['lateRefineReceiptIgnored']
assert results['followup-test-results.json']['scheduledTaskRegistration'] == 'verified'
assert results['embedded-test-results.json']['assemblyVersion'] == '1.43.0.0'
assert results['public-input-check.json']['simTouchReachedEngine']
assert results['public-input-check.json']['pointerFallbackReachedEngine']
multi = results['multi-account-test-results.json']
assert multi['accountCount'] == 4
assert {'four-native-windows-docked', 'four-independent-cookie-stores',
        'broadcast-and-busy-queue', 'four-minimized-heartbeats'} <= set(multi['checks'])
assert (root / 'dist' / 'embedded-test-results.json').stat().st_mtime >= binary.stat().st_mtime
if release.exists() or archive.exists():
    raise SystemExit('Release output already exists')

release.mkdir()
exe = release / '冒险与挖矿自动助手-优化版1.43.exe'
shutil.copy2(binary, exe)
for name in ['README.md', '修改说明.md']:
    shutil.copy2(root / name, release / name)
evidence = release / '验证记录'
evidence.mkdir()
for name in reports:
    shutil.copy2(root / 'dist' / name, evidence / name)

source = release / '冒险与挖矿助手-源码-1.43.zip'
with ZipFile(source, 'w', ZIP_DEFLATED, compresslevel=9) as zip_file:
    for folder in ['src', 'lib', 'original', 'tools', 'tests']:
        for path in sorted((root / folder).rglob('*')):
            if path.is_file() and '__pycache__' not in path.parts and path.suffix != '.pyc':
                zip_file.write(path, path.relative_to(root))
    for name in ['README.md', '修改说明.md']:
        zip_file.write(root / name, name)
with ZipFile(source) as zip_file:
    assert zip_file.testzip() is None

digest = sha256(exe.read_bytes()).hexdigest()
assert digest == sha256(binary.read_bytes()).hexdigest()
(release / 'verification.json').write_text(json.dumps({
    'version': '1.43.0.0', 'exeSHA256': digest, 'liveAccountTransactionsTested': False,
    'checks': results,
    'environmentLimits': ['Real-account gameplay and reward settlement were not exercised.'],
}, ensure_ascii=False, indent=2), encoding='utf-8')
archive.parent.mkdir(exist_ok=True)
with ZipFile(archive, 'w', ZIP_DEFLATED, compresslevel=9) as zip_file:
    for path in sorted(release.rglob('*')):
        if path.is_file():
            zip_file.write(path, Path(release.name) / path.relative_to(release))
with ZipFile(archive) as zip_file:
    assert zip_file.testzip() is None
    assert zip_file.read(release.name + '/' + exe.name) == exe.read_bytes()
print(json.dumps({'exe': str(exe), 'zip': str(archive), 'sha256': digest}, ensure_ascii=False))
