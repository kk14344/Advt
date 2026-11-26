"""Zero-dependency local Web UI server for the 冒险与挖矿 real-rule PvP simulator."""

from __future__ import annotations

import argparse
import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from engine import SimulatorEngine
from export_card import render_team_png_bytes

ENGINE = SimulatorEngine()
ENGINE_LOCK = threading.Lock()
PRESETS = json.loads((HERE / 'presets.json').read_text(encoding='utf8'))
CATALOG = ENGINE.get_catalog()


class SimHandler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args: Any) -> None:
        pass

    def _send_json(self, code: int, payload: Any) -> None:
        raw = json.dumps(payload, ensure_ascii=False).encode('utf8')
        self.send_response(code)
        self.send_header('Content-Type', 'application/json; charset=utf-8')
        self.send_header('Content-Length', str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def _read_json(self) -> dict[str, Any]:
        length = int(self.headers.get('Content-Length', '0'))
        if length <= 0:
            return {}
        return json.loads(self.rfile.read(length).decode('utf8'))

    def do_GET(self) -> None:
        if self.path in ('/', '/index.html'):
            html = (HERE / 'index.html').read_bytes()
            self.send_response(200)
            self.send_header('Content-Type', 'text/html; charset=utf-8')
            self.send_header('Content-Length', str(len(html)))
            self.end_headers()
            self.wfile.write(html)
            return
        if self.path == '/api/init':
            self._send_json(200, {'presets': PRESETS, 'catalog': CATALOG})
            return
        self._send_json(404, {'error': 'Not found'})

    def do_POST(self) -> None:
        try:
            body = self._read_json()
            mode = body.get('mode', 'max')
            if self.path == '/api/inspect':
                with ENGINE_LOCK:
                    res = ENGINE.inspect_team(body['team'], mode=mode)
                self._send_json(200, res)
            elif self.path == '/api/vs':
                runs_each = max(10, min(5000, int(body.get('runs_each', 300))))
                seed = int(body.get('seed', 20261125))
                with ENGINE_LOCK:
                    res = ENGINE.simulate_series(body['team_a'], body['team_b'], runs_each=runs_each, seed=seed, mode=mode)
                self._send_json(200, res)
            elif self.path == '/api/trace':
                seed = int(body.get('seed', 20261125))
                first = body.get('first') or None
                with ENGINE_LOCK:
                    res = ENGINE.simulate_trace(body['team_a'], body['team_b'], seed=seed, mode=mode, forced_first=first)
                self._send_json(200, res)
            elif self.path == '/api/export_png':
                subtitle = body.get('subtitle') or None
                with ENGINE_LOCK:
                    png = render_team_png_bytes(ENGINE, body['team'], mode=mode, subtitle=subtitle)
                self.send_response(200)
                self.send_header('Content-Type', 'image/png')
                self.send_header('Content-Length', str(len(png)))
                self.send_header('Content-Disposition', 'attachment; filename="lineup.png"')
                self.end_headers()
                self.wfile.write(png)
            elif self.path == '/api/bench':
                runs_each = max(10, min(1000, int(body.get('runs_each', 150))))
                seed = int(body.get('seed', 20261125))
                rows = []
                with ENGINE_LOCK:
                    for idx, (k, p) in enumerate(PRESETS.items()):
                        r = ENGINE.simulate_series(body['team'], p, runs_each=runs_each, seed=seed + idx * 313, mode=mode)
                        rows.append({'key': k, 'name': p['name'], 'winrate': r['winrate'], 'wins': r['wins'], 'losses': r['losses'], 'draws': r['draws']})
                mean_wr = sum(x['winrate'] for x in rows) / len(rows)
                min_row = min(rows, key=lambda x: x['winrate'])
                self._send_json(200, {'mean': mean_wr, 'min': min_row['winrate'], 'min_vs': min_row['name'], 'rows': rows})
            else:
                self._send_json(404, {'error': 'Not found'})
        except Exception as exc:
            self._send_json(400, {'error': str(exc)})


def main() -> None:
    parser = argparse.ArgumentParser(description='冒险与挖矿 PvP 模拟器本地 Web 服务')
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', type=int, default=18080)
    args = parser.parse_args()
    server = ThreadingHTTPServer((args.host, args.port), SimHandler)
    print(f'PvP Simulator UI running at http://{args.host}:{args.port}/  (Ctrl+C to stop)')
    server.serve_forever()


if __name__ == '__main__':
    main()
