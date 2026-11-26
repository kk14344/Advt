"""
生成/更新根目录 updates.json 清单（支持单通道增量更新，保留另一通道元数据）：
用法：
    python tools/release_manifest.py --channel official --version 1.0.0 --notes "初始发布"
    python tools/release_manifest.py --channel all --version 1.0.0 --notes "双端 1.0.0 初始发布"
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import subprocess
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parents[1]
CONFIG_PATH = ROOT / 'src' / 'update-config.json'
MANIFEST_PATH = ROOT / 'updates.json'

CHANNEL_FILES = {
    'official': ('冒险与挖矿自动助手-优化版.exe', 'AandmAuto-Official.exe'),
    'testpvp': ('冒险与挖矿自动助手-测试服PvP版.exe', 'AandmAuto-TestPvP.exe'),
}


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open('rb') as f:
        while True:
            chunk = f.read(65536)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def load_manifest() -> dict:
    if MANIFEST_PATH.exists():
        try:
            data = json.loads(MANIFEST_PATH.read_text(encoding='utf-8'))
            if isinstance(data, dict) and isinstance(data.get('channels'), dict):
                return data
        except Exception:
            pass
    return {'schemaVersion': 1, 'updatedAt': '', 'channels': {}}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--channel', choices=['official', 'testpvp', 'all'], default='all')
    parser.add_argument('--version', default='1.0.0')
    parser.add_argument('--notes', default='初始版本发布')
    parser.add_argument('--skip-build', action='store_true')
    parser.add_argument('--asset-ascii', action='store_true', help='Use ASCII release asset filenames for downloadUrl')
    args = parser.parse_args()

    cfg = json.loads(CONFIG_PATH.read_text(encoding='utf-8'))
    repo_url = cfg.get('repoUrl', '').rstrip('/')
    provider = cfg.get('provider', 'github')

    channels = ['official', 'testpvp'] if args.channel == 'all' else [args.channel]
    if not args.skip_build:
        for ch in channels:
            flag = ['--test-pvp'] if ch == 'testpvp' else []
            subprocess.run([sys.executable, str(ROOT / 'tools' / 'build.py'), *flag], cwd=ROOT, check=True)

    manifest = load_manifest()
    now_iso = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
    manifest['schemaVersion'] = 1
    manifest['updatedAt'] = now_iso

    for ch in channels:
        cn_name, ascii_name = CHANNEL_FILES[ch]
        exe_path = ROOT / 'dist' / cn_name
        if not exe_path.exists():
            raise SystemExit(f'缺少构建产物：{exe_path}')
        digest = sha256_file(exe_path)
        size = exe_path.stat().st_size
        tag = f'{ch}-v{args.version}'
        asset_name = ascii_name if args.asset_ascii else cn_name
        if repo_url:
            if provider == 'gitee':
                dl_url = f'{repo_url}/releases/download/{tag}/{asset_name}'
                rel_url = f'{repo_url}/releases/tag/{tag}'
            else:
                dl_url = f'{repo_url}/releases/download/{tag}/{asset_name}'
                rel_url = f'{repo_url}/releases/tag/{tag}'
        else:
            dl_url = ''
            rel_url = ''

        manifest['channels'][ch] = {
            'version': args.version,
            'tag': tag,
            'fileName': cn_name,
            'assetName': asset_name,
            'sha256': digest,
            'size': size,
            'publishedAt': now_iso,
            'notes': args.notes,
            'downloadUrl': dl_url,
            'releaseUrl': rel_url,
        }
        print(f'[{ch}] v{args.version}  size={size}  sha256={digest}')

    MANIFEST_PATH.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'[OK] 已写入 {MANIFEST_PATH}')


if __name__ == '__main__':
    main()
