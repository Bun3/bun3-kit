"""Build the optional Windows x64 diagnostic SDK in a new, caller-owned directory."""
import argparse
import os
from pathlib import Path
import subprocess
import sys


def run(*args, cwd=None, env=None):
    subprocess.run(args, cwd=cwd, env=env, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="New checkout directory (must not exist)")
    parser.add_argument("--cmake", default="cmake")
    parser.add_argument("--generator", default="Visual Studio 17 2022",
                        choices=["Visual Studio 17 2022", "Visual Studio 18 2026"])
    args = parser.parse_args()
    root = args.directory.resolve()
    if root.exists():
        parser.error("The checkout directory must not already exist.")
    env = os.environ.copy()
    env["CMAKE_POLICY_VERSION_MINIMUM"] = "3.15"
    cmake = str(Path(args.cmake).resolve()) if Path(args.cmake).exists() else args.cmake
    if Path(cmake).is_absolute():
        env["PATH"] = str(Path(cmake).parent) + os.pathsep + env.get("PATH", "")
    run("git", "clone", "--branch", "v4.8.1", "--depth", "1",
        "https://github.com/ValveSoftware/steam-audio.git", str(root))
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    if revision != "0da18255cca520771f363ee01f100572b39a308e":
        raise RuntimeError("Unexpected upstream revision; refusing to patch.")
    run("git", "apply", str(Path(__file__).with_name("steam-audio-4.8.1-diagnostics.patch")), cwd=root)
    dependencies = root / "core/build/get_dependencies.py"
    dependencies.write_text(dependencies.read_text().replace("Visual Studio 17 2022", args.generator))
    for dependency in ["zlib", "pffft", "mysofa", "flatbuffers"]:
        run(sys.executable, "get_dependencies.py", "--platform", "windows", "--architecture", "x64",
            "--toolchain", "vs2022", "--dependency", dependency, cwd=dependencies.parent, env=env)
    build = root / "core/build-bun3"
    options = ["IPP", "MKL", "FFTS", "EMBREE", "RADEONRAYS", "TRUEAUDIONEXT"]
    targets = ["TESTS", "BENCHMARKS", "SAMPLES", "ITESTS", "DOCS"]
    run(cmake, "-S", str(root / "core"), "-B", str(build), "-G", args.generator, "-A", "x64",
        *["-DSTEAMAUDIO_ENABLE_" + name + "=OFF" for name in options],
        *["-DSTEAMAUDIO_BUILD_" + name + "=OFF" for name in targets], env=env)
    run(cmake, "--build", str(build), "--config", "Release", "--target", "phonon", "-j", "8", env=env)
    print(build / "src/core/Release/phonon.dll")


if __name__ == "__main__":
    main()
