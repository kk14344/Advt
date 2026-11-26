"""Replay paired held-out maps efficiently with Lupa's native Lua 5.3."""
import argparse
import itertools
import json
from pathlib import Path

from lupa.lua53 import LuaRuntime


ROOT = Path(__file__).resolve().parents[1]
METRICS = (
    "spent", "golems", "nightmares", "revealed", "penalty", "noVision",
    "tnt", "actions", "ticks", "stalled", "shallowPicks", "workshopReady",
)


def runner(path):
    setup, model = (ROOT / "tests/fixtures/mining-simulation.lua").read_text(
        encoding="utf-8").split("-- @LOAD_ASSISTANT@")
    lua = LuaRuntime()
    lua.execute(setup)
    lua.execute(path.read_text(encoding="utf-8"))
    lua.execute("bench_clock=function() return 0 end; bench_tick=function() end")
    lua.execute(model)
    return lua.globals().bench_run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--first", type=int, default=251)
    parser.add_argument("--count", type=int, default=64)
    parser.add_argument("--picks", type=int, default=210)
    args = parser.parse_args()
    if min(args.first, args.count, args.picks) < 1:
        parser.error("first, count and picks must be positive")
    engines = [runner(args.baseline), runner(args.candidate)]
    pairs = []
    for index in range(args.first, args.first + args.count):
        tnt = 12 if index % 2 else 0
        for spawn, vision, mode in itertools.product((1, 2), (1, 2, 3), (1, 2, 3)):
            params = (193 + index * 8669, vision, mode, spawn, args.picks, tnt)
            old, new = [dict(zip(METRICS, map(int, run(*params).split()), strict=True)) for run in engines]
            pairs.append(dict(seedIndex=index, spawning=spawn, vision=vision, mode=mode,
                              tntBudget=tnt, before=old, after=new))
    with args.output.open("w", encoding="utf-8") as out:
        json.dump(dict(firstSeedIndex=args.first, seedCount=args.count, picksPerMap=args.picks,
                       mapPairs=len(pairs), pairs=pairs), out, separators=(",", ":"))


if __name__ == "__main__":
    main()
