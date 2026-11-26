"""Exercise the release EXE against a local origin with isolated WebView profiles."""
from collections import Counter
from hashlib import sha256
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from secrets import token_hex
import json
import os
import subprocess
from threading import Thread


ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / 'build' / ('asset-cache-fixture-' + token_hex(4))
EXE = ROOT / 'dist' / '冒险与挖矿自动助手-优化版.exe'
PUBLIC = bytes(range(256)) * 2048
PRIVATE = b'private fixture'
PUBLIC_DIGEST = sha256(PUBLIC).hexdigest()
PUBLIC_PATH = '/assets-bootstrap/part.' + PUBLIC_DIGEST + '.bin'
PRIVATE_PATH = '/assets-bootstrap/part.' + sha256(PRIVATE).hexdigest() + '.bin'
HITS = Counter()


class Fixture(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        key = self.path + ('-authorized' if self.headers.get('Authorization') else '')
        HITS[key] += 1
        cache_control = 'no-store'
        content_type = 'application/octet-stream'
        if self.path == '/':
            content_type = 'text/html; charset=utf-8'
            body = f"""<!doctype html><meta charset='utf-8'><title>离线缓存验收</title>
            <script>(async()=>{{try{{
                const get=(url,headers)=>fetch(url,{{cache:'reload',credentials:'omit',headers}}).then(r=>r.arrayBuffer());
                const t=performance.now();const part=await get('{PUBLIC_PATH}');const publicMs=performance.now()-t;
                const full=new Uint8Array(part);const hash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',part)), x=>x.toString(16).padStart(2,'0')).join('');
                const other=await get('{PRIVATE_PATH}');const live=await get('/api/fixture');
                const authorized=await get('{PUBLIC_PATH}',{{Authorization:'fixture'}});
                window.fixtureResult={{hash,length:full.length,other:other.byteLength,live:live.byteLength,authorized:authorized.byteLength,publicMs}};
            }}catch(e){{window.fixtureResult={{error:String(e)}}}}}})();</script>""".encode()
        elif self.path == PUBLIC_PATH:
            body = PUBLIC
            if not self.headers.get('Authorization'):
                cache_control = 'public, max-age=31536000, immutable'
        elif self.path == PRIVATE_PATH:
            body = PRIVATE
        else:
            body = b'fresh response'
        self.send_response(200)
        self.send_header('Content-Type', content_type)
        self.send_header('Cache-Control', cache_control)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def run_profile(port, profile):
    env = dict(os.environ, AANDM_DATA_DIR=str(DATA), AANDM_SMOKE='1', AANDM_BOOTSTRAP_FIXTURE_PORT=str(port))
    for name in ('AANDM_BACKGROUND_SMOKE', 'AANDM_MULTI_SMOKE', 'AANDM_PUBLIC_SMOKE'):
        env.pop(name, None)
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    subprocess.run([str(EXE), '--profile', profile], env=env, startupinfo=startup, check=True, timeout=28)
    profile_root = DATA if profile == 'main' else DATA / 'Profiles' / profile
    report = json.loads((profile_root / 'asset-cache-fixture.json').read_text('utf-8-sig'))
    assert report['error'] is None and report['data']['hash'] == PUBLIC_DIGEST, report
    assert report['data']['authorized'] == len(PUBLIC) and report['data']['other'] == len(PRIVATE), report
    return report


if __name__ == '__main__':
    with ThreadingHTTPServer(('127.0.0.1', 0), Fixture) as server:
        worker = Thread(target=server.serve_forever, daemon=True)
        worker.start()
        port = server.server_address[1]
        try:
            cold = run_profile(port, 'main')
            assert cold['cache']['saved'] == 1 and HITS[PUBLIC_PATH] == 1, cold
            files = list((DATA / 'fixture-cache').rglob('*.bin'))
            assert len(files) == 1 and files[0].read_bytes() == PUBLIC
            warm = run_profile(port, '13600000000000000000000000000001')
            assert warm['cache']['hits'] == 1 and HITS[PUBLIC_PATH] == 1, warm
            files[0].write_bytes(b'broken offline test cache')
            repaired = run_profile(port, '13600000000000000000000000000002')
            assert repaired['cache']['saved'] == 1 and HITS[PUBLIC_PATH] == 2 and files[0].read_bytes() == PUBLIC, repaired
            assert HITS[PRIVATE_PATH] == HITS['/api/fixture'] == HITS[PUBLIC_PATH + '-authorized'] == 3, HITS
        finally:
            server.shutdown()
    report = {
        'result': 'passed', 'cold': cold['cache'], 'warmDifferentProfile': warm['cache'],
        'corruptionRepair': repaired['cache'], 'realBrowserProfiles': 3,
        'networkRequests': {'publicPart': HITS[PUBLIC_PATH], 'dynamic': HITS['/api/fixture'],
                            'noStore': HITS[PRIVATE_PATH], 'authorized': HITS[PUBLIC_PATH + '-authorized']},
        'coldPublicMs': cold['data']['publicMs'], 'warmPublicMs': warm['data']['publicMs'],
        'noRealAccountUsed': True,
    }
    (ROOT / 'dist' / 'bootstrap-cache-test.json').write_text(json.dumps(report, indent=2), 'utf-8')
    print(json.dumps(report))
