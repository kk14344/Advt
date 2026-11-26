"""Measure isolated app and WebView2 processes on an interactive Windows desktop."""

import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import msvcrt
import os
from pathlib import Path
import subprocess
import time

import psutil


user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
enum_proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumWindows.argtypes = [enum_proc, wintypes.LPARAM]
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.IsIconic.argtypes = [wintypes.HWND]
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.ShowWindowAsync.argtypes = [wintypes.HWND, ctypes.c_int]
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.GetDpiForWindow.argtypes = [wintypes.HWND]
user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int,
                                ctypes.c_int, ctypes.c_int, wintypes.UINT]
user32.GetGuiResources.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.WaitNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD]
kernel32.PeekNamedPipe.argtypes = [wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD,
                                  ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(wintypes.DWORD),
                                  ctypes.POINTER(wintypes.DWORD)]


def window_for(pid):
    windows = []

    @enum_proc
    def collect(window, _):
        owner = wintypes.DWORD()
        user32.GetWindowThreadProcessId(window, ctypes.byref(owner))
        if owner.value == pid and user32.IsWindowVisible(window):
            windows.append(window)
        return True

    user32.EnumWindows(collect, 0)
    return windows[0] if windows else None


def gdi_objects(pid):
    process = kernel32.OpenProcess(0x0400, False, pid)
    if not process:
        return None
    try:
        return user32.GetGuiResources(process, 0)
    finally:
        kernel32.CloseHandle(process)


def read_assistant_status(profile):
    context = str(profile.resolve()).rstrip("\\").upper() + "|main"
    context_id = hashlib.sha256(context.encode("utf-8")).hexdigest()[:24].upper()
    pipe = r"\\.\pipe\AandmAutoTasks-" + context_id
    if not kernel32.WaitNamedPipeW(pipe, 150):
        return {}
    try:
        with open(pipe, "r+b", buffering=0) as stream:
            stream.write(b'{"Action":"status"}\n')
            handle = msvcrt.get_osfhandle(stream.fileno())
            payload = bytearray()
            deadline = time.monotonic() + 1.2
            while time.monotonic() < deadline:
                available = wintypes.DWORD()
                if not kernel32.PeekNamedPipe(handle, None, 0, None, ctypes.byref(available), None):
                    break
                if available.value:
                    payload.extend(stream.read(available.value))
                    if b"\n" in payload:
                        return json.loads(payload.split(b"\n", 1)[0])
                time.sleep(0.05)
    except (OSError, ValueError):
        pass
    return {}


def read_processes(launched):
    result = {}
    roles = {}
    for item in launched:
        try:
            parent = psutil.Process(item.pid)
            roles[parent.pid] = ("assistant", parent)
            for child in parent.children(recursive=True):
                role = "webview" if child.name().casefold() == "msedgewebview2.exe" else "other"
                roles.setdefault(child.pid, (role, child))
        except psutil.Error:
            continue
    for engine in psutil.process_iter(["name"]):
        if (engine.info["name"] or "").casefold() in {"wallpaper32.exe", "wallpaper64.exe", "webwallpaper32.exe", "webwallpaper64.exe"}:
            roles.setdefault(engine.pid, ("wallpaper_engine_system", engine))
    for pid, (role, process) in roles.items():
        try:
            with process.oneshot():
                cpu = process.cpu_times()
                memory = process.memory_info()
                result[str(pid)] = {
                    "role": role,
                    "cpu_seconds": cpu.user + cpu.system,
                    "private_bytes": memory.private,
                    "working_set_bytes": memory.rss,
                    "gdi": gdi_objects(pid) if role == "assistant" else None,
                }
        except psutil.Error:
            continue
    return result


def aggregate(samples):
    roles = {}
    span = samples[-1]["seconds"] - samples[0]["seconds"]
    for left, right in zip(samples, samples[1:]):
        for pid, item in right["processes"].items():
            role = roles.setdefault(item["role"], {"cpu": 0, "peak_private_bytes": 0, "peak_working_set_bytes": 0})
            before = left["processes"].get(pid)
            if before is not None:
                role["cpu"] += max(0, item["cpu_seconds"] - before["cpu_seconds"])
        for name, role in roles.items():
            total = sum(item["private_bytes"] for item in right["processes"].values() if item["role"] == name)
            role["peak_private_bytes"] = max(role["peak_private_bytes"], total)
            working = sum(item["working_set_bytes"] for item in right["processes"].values() if item["role"] == name)
            role["peak_working_set_bytes"] = max(role["peak_working_set_bytes"], working)
    return {name: {"single_core_percent": round(100 * role["cpu"] / span, 3),
                   "peak_private_mib": round(role["peak_private_bytes"] / 1048576, 3),
                   "peak_working_set_mib": round(role["peak_working_set_bytes"] / 1048576, 3)}
            for name, role in roles.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mode", choices=["static", "minimized", "four"], required=True)
    parser.add_argument("--seconds", type=int, default=60)
    parser.add_argument("--warmup", type=int, default=10)
    parser.add_argument("--interval", type=float, default=1.0)
    parser.add_argument("--wallpaper", type=Path)
    parser.add_argument("--fixture", choices=["offline", "public-login"], default="offline")
    parser.add_argument("--foreground-warmup", type=int, default=0)
    parser.add_argument("--quiet", action="store_true", help="Run supported builds without foreground activation.")
    args = parser.parse_args()
    if args.seconds < 2 or min(args.warmup, args.foreground_warmup) < 0 or not 0.25 <= args.interval <= args.seconds:
        parser.error("Use at least 2 measured seconds and an interval between 0.25 seconds and the run duration.")
    launch = args.exe.resolve(strict=True)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    env = {key: value for key, value in os.environ.items() if not key.startswith("AANDM_")}
    if args.quiet:
        env["AANDM_LOW_RESOURCE_TEST"] = "1"
    if args.fixture == "offline":
        env["AANDM_MULTI_SMOKE"] = "1"
    applications = []
    windows = []
    try:
        for index in range(4 if args.mode == "four" else 1):
            profile = output / f"profile-{index + 1}"
            profile.mkdir()
            # Suppress legacy lookup of the desktop account by older builds.
            (profile / "account.dat").write_text('{"autoLogin":false}', encoding="utf-8")
            config = {"uiTheme": 4, "uiLayout": 1, "customBgMode": 0,
                      "customBgOpacity": 65, "stageWallpaperBg": True}
            if args.wallpaper:
                config["customBgPath"] = str(args.wallpaper.resolve(strict=True))
            (profile / "options.json").write_text(json.dumps(config), encoding="utf-8")
            process = subprocess.Popen([launch, "--data-root", profile, "--no-schtasks"], env=env)
            applications.append(process)
        deadline = time.monotonic() + (140 if args.fixture == "public-login" else 35)
        windows = [None] * len(applications)
        while time.monotonic() < deadline:
            profiles = sorted(output.glob("profile-*"))
            states = [read_assistant_status(profile) for profile in profiles]
            windows = [state.get("window", 0) for state in states]
            if args.fixture == "offline":
                ready = [(profile / "multi-cookie-debug.json").is_file() for profile in profiles]
            else:
                ready = [state.get("ready") and state.get("message") in {"已暂停", "等待登录"}
                         for state in states]
            if len(windows) == len(applications) and all(windows) and all(ready):
                break
            if any(proc.poll() is not None for proc in applications):
                raise RuntimeError("An assistant exited before reaching the offline page.")
            time.sleep(0.15)
        else:
            raise TimeoutError("Test windows did not become ready.")

        for window, process in zip(windows, applications):
            owner = wintypes.DWORD()
            user32.GetWindowThreadProcessId(window, ctypes.byref(owner))
            if owner.value != process.pid:
                raise RuntimeError("The reported form window belongs to another process.")
        time.sleep(args.foreground_warmup)
        for index, window in enumerate(windows):
            if args.mode == "minimized":
                user32.ShowWindowAsync(window, 6)
            else:
                user32.SetWindowPos(window, None, 10 + index * 230, 15 + index * 18, 1320, 880, 0x14)
        dpi = [user32.GetDpiForWindow(window) for window in windows]
        time.sleep(args.warmup)
        if args.mode == "minimized" and not all(user32.IsIconic(window) for window in windows):
            raise RuntimeError("A sampled window did not enter the minimized state.")
        started = time.monotonic()
        samples = []
        with (output / "samples.jsonl").open("x", encoding="utf-8") as trace:
            while True:
                if any(proc.poll() is not None for proc in applications):
                    raise RuntimeError("An assistant exited during measurement.")
                if args.mode == "minimized" and not all(user32.IsIconic(window) for window in windows):
                    raise RuntimeError("A sampled window left the minimized state.")
                elapsed = time.monotonic() - started
                sample = {"seconds": round(elapsed, 3), "processes": read_processes(applications)}
                if args.quiet and args.fixture == "public-login":
                    sample["status"] = [read_assistant_status(profile) for profile in sorted(output.glob("profile-*"))]
                samples.append(sample)
                trace.write(json.dumps(sample) + "\n")
                trace.flush()
                if elapsed >= args.seconds:
                    break
                time.sleep(min(args.interval, max(0.01, args.seconds - elapsed)))
        report = {"mode": args.mode, "executable_sha256": hashlib.sha256(launch.read_bytes()).hexdigest(),
                  "dpi": dpi, "samples": samples, "metrics": aggregate(samples)}
        report["fixture"] = args.fixture
        report["physical_memory_mib"] = round(psutil.virtual_memory().total / 1048576, 1)
        (output / "runtime.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps(report["metrics"]))
    finally:
        for process in applications:
            window = window_for(process.pid)
            if window:
                user32.PostMessageW(window, 0x0010, 0, 0)
        for process in applications:
            try:
                process.wait(timeout=7)
            except subprocess.TimeoutExpired:
                process.terminate()
                process.wait(timeout=7)


if __name__ == "__main__":
    main()
