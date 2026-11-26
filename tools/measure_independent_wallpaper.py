"""Measure one isolated 1080p single-window Wallpaper Engine run."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

import psutil

from measure_runtime import read_processes
from summarize_runtime import summarize


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def run(args):
    output = args.output.resolve()
    output.mkdir(parents=True)
    profile = output / "profile"
    profile.mkdir()
    if args.quality != 0 or args.fps != 30:
        (profile / "options.json").write_text(
            json.dumps({"wallpaperQuality": args.quality, "wallpaperFps": args.fps}), encoding="utf-8"
        )
    env = {key: value for key, value in os.environ.items() if not key.startswith("AANDM_")}
    env.update(AANDM_SMOKE="1", AANDM_STAGE3_MEASURE="1",
               AANDM_WALLPAPER_WIDTH="1920", AANDM_WALLPAPER_HEIGHT="1080",
               AANDM_WALLPAPER_LAYOUT=str(args.layout), AANDM_WALLPAPER_SECONDS=str(args.seconds),
               AANDM_WALLPAPER_SMOKE=str(args.project.resolve(strict=True)))
    if args.video_probe:
        env["AANDM_STAGE3_DIRECT_PROBE"] = "1"
    executable = args.exe.resolve(strict=True)
    wake = ctypes.windll.kernel32.SetThreadExecutionState
    wake(0x80000003)
    process = None
    try:
        process = subprocess.Popen([executable, "--data-root", profile, "--no-schtasks"], env=env)
        deadline = time.monotonic() + 80
        begin = profile / "wallpaper-metrics-start"
        report = profile / "wallpaper-smoke.json"
        while not begin.exists() and time.monotonic() < deadline and process.poll() is None:
            time.sleep(.15)
        if not begin.exists():
            details = read_json(report) if report.exists() else {}
            raise RuntimeError(f"Playback did not begin sampling: {details}")
        started = time.monotonic()
        until = started + args.seconds * 2 + 70
        end = profile / "wallpaper-metrics-end"
        trace = output / "processes.jsonl"
        with trace.open("w", encoding="utf-8") as samples:
            while not end.exists() and time.monotonic() < until and process.poll() is None:
                samples.write(json.dumps({"seconds": time.monotonic() - started,
                                          "processes": read_processes([process])}) + "\n")
                samples.flush()
                time.sleep(1)
        process.wait(timeout=45)
        clip = read_json(report)
        if clip["result"] != "passed":
            raise RuntimeError(f"Wallpaper smoke failed: {clip}")
        result = {"sha256": hashlib.sha256(executable.read_bytes()).hexdigest(),
                  "project": args.project.parent.name, "playback": clip, "resources": summarize(trace)}
        (output / "summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(result, ensure_ascii=False))
    finally:
        if process is not None and process.poll() is None:
            children = psutil.Process(process.pid).children(recursive=True)
            process.terminate()
            for child in children:
                try:
                    child.terminate()
                except psutil.NoSuchProcess:
                    pass
            process.wait(timeout=10)
        wake(0x80000000)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ("exe", "project", "output"):
        parser.add_argument("--" + field, type=Path, required=True)
    parser.add_argument("--video-probe", action="store_true")
    parser.add_argument("--quality", type=int, choices=(0, 1, 2), default=0)
    parser.add_argument("--fps", type=int, choices=range(5, 61), default=30)
    parser.add_argument("--seconds", type=int, default=45)
    parser.add_argument("--layout", type=int, choices=(1, 2, 3), default=1)
    args = parser.parse_args()
    if not 10 <= args.seconds <= 60:
        parser.error("Use between 10 and 60 measured seconds.")
    run(args)


if __name__ == "__main__":
    main()

