"""Create a versioned release from an already tested clean Git build."""
import argparse
import json
import shutil
import tempfile
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

from release_identity import NAME, ROOT, git, provenance, sha256


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, action="append", required=True, help="助手验收 result.json，可多次传入")
    parser.add_argument("--output", type=Path, default=ROOT / "dist" / "releases")
    args = parser.parse_args()

    binary = ROOT / "dist" / NAME / (NAME + ".exe")
    manifest = json.loads(binary.with_suffix(".json").read_text(encoding="utf-8"))
    current = provenance()
    if current["dirty"] or any(manifest[key] != value for key, value in current.items()):
        raise SystemExit("需要在当前干净 Git 提交上生成的版本化构建。")
    if sha256(binary) != manifest["exeSha256"]:
        raise SystemExit("EXE 和构建清单不匹配。")
    reports = []
    for path in args.report:
        report = json.loads(path.read_text(encoding="utf-8-sig"))
        if report["sha256"].lower() != manifest["exeSha256"] or report["result"] == "failed":
            raise SystemExit(f"验收报告与 EXE 不匹配或报告含失败场景。 {path}")
        reports.append((path.resolve(), report))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    target = output / NAME
    if target.exists():
        raise FileExistsError(target)
    with tempfile.TemporaryDirectory(dir=output) as temporary:
        stage = Path(temporary) / NAME
        stage.mkdir()
        shutil.copy2(binary, stage / binary.name)
        source = stage / "源码" / (NAME + ".zip")
        source.parent.mkdir()
        source_archive = git("archive", "--format=zip", manifest["commit"])
        source.write_bytes(source_archive)
        with ZipFile(source, "a", ZIP_DEFLATED) as archive:
            archive.writestr("source-provenance.json", json.dumps({"commit": manifest["commit"]}))
            assert archive.testzip() is None
        report_names = []
        for index, (path, summary) in enumerate(reports, 1):
            label = str(index)
            report_dir = stage / "验收报告" / label
            report_dir.mkdir(parents=True)
            shutil.copy2(path, report_dir / "result.json")
            report_names.append(f"验收报告/{label}/result.json")
            for scenario in summary["scenarios"]:
                name = scenario["name"]
                if name != Path(name).name or name in {".", ".."}:
                    raise ValueError("验收场景名称无效")
                for filename in scenario.get("reports", []):
                    if filename != Path(filename).name or not filename.endswith(".json"):
                        raise ValueError("验收报告文件名无效")
                    evidence = path.parent / name / filename
                    if evidence.is_file():
                        folder = report_dir / name
                        folder.mkdir(exist_ok=True)
                        shutil.copy2(evidence, folder / filename)
        manifest.update({"sourceSha256": sha256(source), "acceptanceReports": report_names})
        (stage / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
        bundle = stage / (NAME + ".zip")
        with ZipFile(bundle, "w", ZIP_DEFLATED) as archive:
            for path in sorted(stage.rglob("*")):
                if path.is_file() and path != bundle:
                    archive.write(path, path.relative_to(stage))
        with ZipFile(bundle) as archive:
            assert archive.testzip() is None and archive.read(binary.name) == binary.read_bytes()
        stage.rename(target)
    print(target)


if __name__ == "__main__":
    main()

