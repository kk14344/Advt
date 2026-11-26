from pathlib import Path
import json

root=Path(__file__).resolve().parents[1]
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
launch=read(root/'dist/multi-process-launch.json')
base=root/'dist/smoke-release-final'
child=base/'Profiles'/launch['childProfile']
paths=[base/'smoke-result.json',child/'smoke-result.json']
reports=[read(p) for p in paths]
binary=next((root/'dist').glob('*.exe'))
assert launch['bothRunning'] and len(set(launch['processIds']))==2
for path,report in zip(paths,reports):
    assert path.stat().st_mtime>=binary.stat().st_mtime
    for flag in ['browserReady','scriptReady','contractCaptureSaved','minimizedStillRunning','restoreImagesStable','profileCookieIsolated','fastDailyScheduling','collapseControlsWorking']:
        assert report[flag],(path,flag)
    assert report['restoreChecks']==6
assert reports[0]['dataRoot']!=reports[1]['dataRoot']
assert reports[0]['profileId']=='main' and reports[1]['profileId']==launch['childProfile']
assert not (child/'browser-reset-required').exists()
assert '已清理此窗口的旧登录缓存' in reports[1]['logs']
report={'result':'passed','windowCount':2,'concurrentProcessesObserved':True,
    'independentDataRoots':True,'independentCookieStores':True,'browserResetMarkerProcessed':True,
    'restoreChecksPerWindow':[r['restoreChecks'] for r in reports],
    'realAccountLoginTested':False,'profileIds':[r['profileId'] for r in reports]}
(root/'dist/multi-profile-test-results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
