"""Summarize the process trace from the Windows runner or measure_runtime.py."""
import argparse
import json
from collections import defaultdict
from pathlib import Path


MIB = 1024 * 1024


def normalized(row):
    processes = row["processes"]
    if isinstance(processes, dict):
        return processes
    return {
        str(item["pid"]): {"role": item["role"], "cpu_seconds": item["cpuSeconds"],
                          "private_bytes": item["privateBytes"], "working_set_bytes": item["workingSetBytes"],
                          "gdi": item["gdi"] if item["role"] == "assistant" else None}
        for item in processes
    }


def summarize(path):
    raw = path.read_bytes()
    lines = raw.splitlines()
    if not raw.endswith(b"\n"):
        lines = lines[:-1]  # The sampler may still be appending this line.
    rows = [json.loads(line) for line in lines if line.strip()]
    if len(rows) < 2:
        raise ValueError("At least two complete samples are needed")
    span = rows[-1]["seconds"] - rows[0]["seconds"]
    if span <= 0:
        raise ValueError("Samples must cover a positive duration")
    by_role = defaultdict(list)
    cpu = defaultdict(float)
    previous = {}
    for row in rows:
        processes = normalized(row)
        grouped = defaultdict(list)
        for pid, item in processes.items():
            role = item["role"]
            grouped[role].append(item)
            if pid in previous and role == previous[pid]["role"]:
                cpu[role] += max(0, item["cpu_seconds"] - previous[pid]["cpu_seconds"])
        previous = processes
        for role, items in grouped.items():
            by_role[role].append((sum(item["private_bytes"] for item in items) / MIB,
                                  sum(item["working_set_bytes"] for item in items) / MIB,
                                  sum(item["gdi"] or 0 for item in items)))
    roles = {}
    for role, points in by_role.items():
        private, working, gdi = zip(*points)
        roles[role] = {
            "samplesSeen": len(points),
            "meanLogicalCorePercent": round(100 * cpu[role] / span, 3),
            "privateMiB": stats(private),
            "workingSetMiB": stats(working)
        }
        if role == "assistant":
            roles[role]["gdi"] = stats(gdi)
    return {"seconds": round(span, 3), "samples": len(rows), "roles": roles}


def stats(values):
    return {key: round(value, 3) for key, value in
            zip(("first", "last", "minimum", "peak"), (values[0], values[-1], min(values), max(values)))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trace", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    content = json.dumps(summarize(args.trace), indent=2) + "\n"
    if args.output:
        args.output.write_text(content, encoding="utf-8")
    else:
        print(content, end="")


if __name__ == "__main__":
    main()

