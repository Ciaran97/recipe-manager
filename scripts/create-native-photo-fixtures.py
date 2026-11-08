#!/usr/bin/env python3
"""Create two owned, synthetic PNG photos for the isolated iOS Photos picker fixture."""
import os
from pathlib import Path
import struct
import sys
import zlib


def chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))


def main() -> None:
    destination = Path(sys.argv[1])
    destination.mkdir(mode=0o700, parents=True, exist_ok=True)
    for name, color in [("recipe-red.png", (190, 40, 40)), ("recipe-blue.png", (40, 70, 190))]:
        width, height = 200, 120
        # An unambiguous two-color pattern, generated locally without personal photos or metadata.
        rows = b"".join(b"\0" + bytes(color if y < height // 2 else (240, 240, 240)) * width for y in range(height))
        png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        png += chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")
        path = destination / name
        with path.open("xb") as stream:
            os.chmod(path, 0o600)
            stream.write(png)


if __name__ == "__main__":
    main()
