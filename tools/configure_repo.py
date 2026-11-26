"""
一键绑定/切换公开更新仓库（支持 GitHub / Gitee / GitCode）：
用法：
    python tools/configure_repo.py https://github.com/kk14344/Advt [--rebuild] [--no-check]
"""
from pathlib import Path
import json
import re
import subprocess
import sys
import urllib.request
import urllib.error

ROOT = Path(__file__).resolve().parents[1]
CONFIG_PATH = ROOT / 'src' / 'update-config.json'

DEFAULT_MIRRORS = [
    'https://ghfast.top/',
    'https://gh-proxy.com/',
    'https://mirror.ghproxy.com/',
]


def parse_repo(raw_url: str):
    url = raw_url.strip().rstrip('/')
    if url.endswith('.git'):
        url = url[:-4]
    m = re.match(r'^https?://(github\.com|gitee\.com|gitcode\.com)/([^/]+)/([^/]+)$', url, re.I)
    if not m:
        raise SystemExit(f'不支持的仓库地址格式：{raw_url}\n示例：https://github.com/owner/repo 或 https://gitee.com/owner/repo')
    host = m.group(1).lower()
    owner, repo = m.group(2), m.group(3)
    canonical = f'https://{host}/{owner}/{repo}'
    if host == 'github.com':
        return {
            'provider': 'github',
            'repoUrl': canonical,
            'releasePageUrl': f'{canonical}/releases',
            'manifestUrls': [
                f'https://raw.githubusercontent.com/{owner}/{repo}/main/updates.json',
                f'{canonical}/releases/latest/download/updates.json',
            ],
            'downloadMirrors': DEFAULT_MIRRORS,
        }
    elif host == 'gitee.com':
        return {
            'provider': 'gitee',
            'repoUrl': canonical,
            'releasePageUrl': f'{canonical}/releases',
            'manifestUrls': [
                f'{canonical}/raw/main/updates.json',
            ],
            'downloadMirrors': [],
        }
    else:
        return {
            'provider': 'gitcode',
            'repoUrl': canonical,
            'releasePageUrl': f'{canonical}/releases',
            'manifestUrls': [
                f'{canonical}/raw/main/updates.json',
            ],
            'downloadMirrors': [],
        }


def check_public_reachable(url: str):
    req = urllib.request.Request(url, method='HEAD', headers={'User-Agent': 'AandmAuto-Configure/1.0'})
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            if 200 <= resp.status < 400:
                print(f'[OK] 匿名可达性检查通过：{url} (HTTP {resp.status})')
                return True
    except urllib.error.HTTPError as e:
        raise SystemExit(f'[FAIL] 匿名访问 {url} 返回 HTTP {e.code}。\n请用浏览器无痕窗口确认仓库为 Public 且账号未被隐藏，再重试。')
    except Exception as e:
        raise SystemExit(f'[FAIL] 无法访问 {url}：{e}')
    return False


def set_git_remote(repo_url: str):
    remote_url = repo_url + '.git'
    existing = subprocess.run(['git', 'remote'], cwd=ROOT, capture_output=True, text=True).stdout.splitlines()
    if 'origin' in existing:
        subprocess.run(['git', 'remote', 'set-url', 'origin', remote_url], cwd=ROOT, check=True)
    else:
        subprocess.run(['git', 'remote', 'add', 'origin', remote_url], cwd=ROOT, check=True)
    print(f'[OK] 已设置 git remote origin = {remote_url}')


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    flags = {a for a in sys.argv[1:] if a.startswith('--')}
    if not args:
        raise SystemExit('用法：python tools/configure_repo.py <仓库URL> [--rebuild] [--no-check]')
    cfg = parse_repo(args[0])
    if '--no-check' not in flags:
        check_public_reachable(cfg['repoUrl'])
    CONFIG_PATH.write_text(json.dumps(cfg, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'[OK] 已更新 {CONFIG_PATH}')
    set_git_remote(cfg['repoUrl'])
    if '--rebuild' in flags:
        subprocess.run([sys.executable, str(ROOT / 'tools' / 'build.py'), '--all'], cwd=ROOT, check=True)


if __name__ == '__main__':
    main()
