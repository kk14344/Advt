from pathlib import Path
import json

root=Path(__file__).resolve().parents[1]
def read(name):return json.loads((root/name).read_text(encoding='utf-8-sig'))
binary=next((root/'dist').glob('*.exe'))
paths=['dist/background-live/background-test-results.json',
       'dist/background-live/Profiles/13300000000000000000000000000001/background-test-results.json']
reports=[read(name) for name in paths]
baseline=read('dist/background-baseline/background-test-results.json')
js=read('dist/background-js-test-results.json')
public=read('dist/public-startup-final/public-game-check.json')
launch=read('dist/background-processes.json')
assert launch['bothRunning'] and len(set(launch['processIds']))==2
assert baseline['result']=='baseline-stall-reproduced'
assert baseline['nativeRafPauseInjected'] and baseline['nativeFreezeInjected']
assert js['result']=='passed' and js['checks']==8
for name,report in zip(paths,reports):
    assert (root/name).stat().st_mtime>=binary.stat().st_mtime
    assert report['result']=='passed' and report['minimizedSeconds']>=90
    assert report['progressWhileMinimized']>=180 and report['goodProgressWindows']>=17
    for key in ['documentPreserved','requestsConserved','independentOfPollTimer','nativeRafPauseInjected','nativeFreezeInjected','noRealAccountUsed','noRealNetworkMutations']:
        assert report[key],key
    assert report['last']['guardErrors']==0
assert public['result']=='passed' and public['realEngineBackgroundRunning'] and public['realEngineBackgroundTicks']>40
summary={'result':'passed','concurrentWindows':2,'minimizedSecondsPerWindow':[r['minimizedSeconds'] for r in reports],
         'confirmedMockOperationsPerWindow':[r['progressWhileMinimized'] for r in reports],
         'baselineStallReproduced':True,'animationPauseAndBrowserFreezeInjected':True,
         'allProgressWindowsAdvanced':all(r['goodProgressWindows']==18 for r in reports),
         'noPageReloadOrDuplicatedRequests':True,'independentOfSnapshotPolling':True,
         'javascriptChecks':js['checks'],'realLoginPageBackgroundSeconds':public['backgroundSeconds'],
         'realLoginPageGameUpdates':public['realEngineBackgroundTicks'],
         'realAccountTaskTested':False,'extendedRealAccountSoakTested':False,
         'measurement':'Two offline task pipelines for 90 seconds each under injected rAF pause and browser freeze; real game login-page updates checked separately.'}
(root/'dist/background-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False))
