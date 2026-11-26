"""
端到端自动化验证脚本：启动本地 HTTP 模拟服务器，分别调用真实编译出的正式服与测试服 exe（--updater-smoke），
验证：SemVer 版本对比、镜像回退排序、通道严格隔离、SHA-256 防篡改拦截、合法包下载校验。
"""
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess
import tempfile
import threading
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parents[1]
PAYLOAD = b'AANDM-MOCK-EXE-BINARY-CONTENT-' * 2048
PAYLOAD_SHA = hashlib.sha256(PAYLOAD).hexdigest()


class MockHandler(BaseHTTPRequestHandler):
    manifest_bytes = b'{}'

    def do_GET(self):
        if self.path == '/updates.json':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('Content-Length', str(len(self.manifest_bytes)))
            self.end_headers()
            self.wfile.write(self.manifest_bytes)
        elif self.path in ('/official.exe', '/testpvp.exe'):
            self.send_response(200)
            self.send_header('Content-Type', 'application/octet-stream')
            self.send_header('Content-Length', str(len(PAYLOAD)))
            self.end_headers()
            self.wfile.write(PAYLOAD)
        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, format, *args):
        return


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--channel', choices=['official', 'testpvp', 'all'], default='all')
    args = parser.parse_args()

    server = HTTPServer(('127.0.0.1', 0), MockHandler)
    port = server.server_address[1]
    base = f'http://127.0.0.1:{port}'
    manifest = {
        'schemaVersion': 1,
        'updatedAt': '2026-11-26T00:00:00Z',
        'mirrors': ['https://ghfast.top/', 'https://gh-proxy.com/'],
        'channels': {
            'official': {
                'version': '99.0.2',
                'tag': 'official-v99.0.2',
                'fileName': '冒险与挖矿自动助手-优化版.exe',
                'sha256': PAYLOAD_SHA,
                'size': len(PAYLOAD),
                'publishedAt': '2026-11-26T00:00:00Z',
                'notes': '正式服测试更新包',
                'downloadUrl': f'{base}/official.exe',
                'releaseUrl': f'{base}/releases/official',
            },
            'testpvp': {
                'version': '99.0.5',
                'tag': 'testpvp-v99.0.5',
                'fileName': '冒险与挖矿自动助手-测试服PvP版.exe',
                'sha256': PAYLOAD_SHA,
                'size': len(PAYLOAD),
                'publishedAt': '2026-11-26T00:00:00Z',
                'notes': '测试服测试更新包',
                'downloadUrl': f'{base}/testpvp.exe',
                'releaseUrl': f'{base}/releases/testpvp',
            },
        },
    }
    MockHandler.manifest_bytes = json.dumps(manifest, ensure_ascii=False).encode('utf-8')
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()

    try:
        targets = ['official', 'testpvp'] if args.channel == 'all' else [args.channel]
        with tempfile.TemporaryDirectory(prefix='aandm-updater-test-') as tmp:
            for ch in targets:
                exe = ROOT / 'dist' / ('冒险与挖矿自动助手-测试服PvP版.exe' if ch == 'testpvp' else '冒险与挖矿自动助手-优化版.exe')
                if not exe.exists():
                    raise SystemExit(f'Missing exe: {exe}')
                env = os.environ.copy()
                env['AANDM_UPDATE_MANIFEST_URL'] = f'{base}/updates.json'
                subprocess.run([str(exe), '--updater-smoke', tmp], env=env, check=True)
                report_path = Path(tmp) / f'updater-smoke-{ch}.json'
                report = json.loads(report_path.read_text(encoding='utf-8-sig'))
                expected_ver = '99.0.5' if ch == 'testpvp' else '99.0.2'
                assert report['channel'] == ch, report
                assert report['semverOk'] is True, report
                assert report['mirrorOrderOk'] is True, report
                assert report['checkState'] == 'UpdateAvailable', report
                assert report['latestVersion'] == expected_ver, f'Channel isolation failed: {report}'
                assert report['tamperRejected'] is True, report
                assert report['downloadVerified'] is True, report
                assert report.get('remoteMirrorsCount', 0) == 2, report
                print(f'[PASS] {ch} updater smoke passed: detected v{report["latestVersion"]}, remote mirrors=2, tamper rejected, valid SHA-256 downloaded & verified')

                subprocess.run([str(exe), '--diag-smoke', tmp], env=env, check=True)
                diag_path = Path(tmp) / f'diag-smoke-{ch}.json'
                diag = json.loads(diag_path.read_text(encoding='utf-8-sig'))
                assert diag.get('passed') is True, diag
                assert diag.get('leakedSecret') is False, diag
                assert diag.get('hasForbiddenEntry') is False, diag
                print(f'[PASS] {ch} diagnostic export privacy smoke passed: {diag["includedCount"]} whitelisted files, 0 leaks')
    finally:
        server.shutdown()


if __name__ == '__main__':
    main()
