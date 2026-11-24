"""Smoke test for the Windows helper. Deliberately uses synthetic pixels only."""
import argparse
from pathlib import Path
import struct
import subprocess


def run(exe, mode, request=b""):
    return subprocess.run(
        [str(exe), mode], input=request, capture_output=True, timeout=15, check=False
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("helper", type=Path)
    parser.add_argument("--require", choices=("d3d11", "vulkan"), action="append", default=[])
    args = parser.parse_args()
    exe = args.helper.resolve()
    probe = run(exe, "probe")
    assert probe.returncode == 0 and b"Direct3D 11:" in probe.stdout and b"Vulkan:" in probe.stdout
    print(probe.stdout.decode("utf-8", errors="replace").strip())

    # Two flat color fields. Their centers distinguish scaling from returning an
    # unscaled crop, and channel order is tested in the BGRA wire format.
    blue = bytes((200, 40, 12, 255))
    red = bytes((15, 60, 220, 255))
    row = blue * 24 + red * 24
    request = struct.pack("<4I", 48, 32, 12, 8) + row * 32
    device_absent = {
        "d3d11": b"Direct3D 11: unavailable" in probe.stdout,
        "vulkan": any(value in probe.stdout for value in (
            b"Vulkan: Windows loader or instance unavailable",
            b"Vulkan: no integrated/discrete GPU"
        )),
    }
    for mode in ("d3d11-warp-test", "d3d11", "vulkan"):
        bad = run(exe, mode, struct.pack("<4I", 99999, 1, 12, 8))
        assert bad.returncode == 2 and not bad.stdout, (mode, "oversized input")
        short = run(exe, mode, request[:-1])
        assert short.returncode == 2 and not short.stdout, (mode, "truncated input")
        result = run(exe, mode, request)
        if result.returncode == 3 and not result.stdout and device_absent.get(mode) and mode not in args.require:
            print(f"{mode}: SKIP (no compatible hardware device available)")
            continue
        assert result.returncode == 0, (mode, result.returncode, result.stderr)
        assert len(result.stdout) == 12 * 8 * 4, (mode, len(result.stdout))
        for column, expected in ((2, blue), (9, red)):
            offset = (4 * 12 + column) * 4
            for actual, target in zip(result.stdout[offset:offset + 3], expected[:3]):
                assert abs(actual - target) <= 2, (mode, column, actual, target)
        print(f"{mode}: scaling and readback verified (" +
              ("software WARP, test only)" if mode == "d3d11-warp-test" else "hardware)"))


if __name__ == "__main__":
    main()
