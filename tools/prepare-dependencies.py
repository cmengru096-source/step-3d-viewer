"""Download and verify the portable Windows runtime and STEP importer."""

import argparse
import base64
import gzip
import hashlib
from pathlib import Path
import shutil
import tarfile
import urllib.request

NODE_VERSION = "v24.20.0"
NODE_URL = f"https://nodejs.org/dist/{NODE_VERSION}/win-x64/node.exe"
NODE_HASH = "5c976096e04e5c2c1f091938926234cc9fbebfe9787ddd149351b3b0ecc707b5"
OCCT_URL = "https://registry.npmjs.org/occt-import-js/-/occt-import-js-0.0.23.tgz"
OCCT_HASH = base64.b64decode(
    "RFfYQXYFX5C1mB1Aywm0ShcUKzXOr/VzTnlzhBSDJOR6YCAPt1HYCzeXWg1vwwjn/cUxwqRNhhtf1dlewoZYCQ=="
).hex()


def file_hash(path, algorithm):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, algorithm).hexdigest()


def fetch(url, destination, algorithm, expected, local=None):
    if destination.is_file() and file_hash(destination, algorithm) == expected:
        return
    temporary = destination.with_suffix(destination.suffix + ".partial")
    try:
        if local:
            shutil.copyfile(local, temporary)
        else:
            print(f"Downloading {url}", flush=True)
            with urllib.request.urlopen(url, timeout=90) as response, temporary.open("wb") as target:
                shutil.copyfileobj(response, target)
        if file_hash(temporary, algorithm) != expected:
            raise ValueError(f"Checksum mismatch for {destination.name}")
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)


def prepare(cache, local_node):
    cache.mkdir(parents=True, exist_ok=True)
    node = cache / "node.exe"
    fetch(NODE_URL, node, "sha256", NODE_HASH, local_node)
    with node.open("rb") as source, (cache / "node.exe.gz").open("wb") as target:
        with gzip.GzipFile(fileobj=target, mode="wb", filename="", compresslevel=5, mtime=0) as output:
            shutil.copyfileobj(source, output)

    package = cache / "occt-import-js.tgz"
    fetch(OCCT_URL, package, "sha512", OCCT_HASH)
    destination = cache / "occt"
    destination.mkdir(exist_ok=True)
    with tarfile.open(package, "r:gz") as importer:
        for filename in ("occt-import-js.js", "occt-import-js.wasm"):
            with importer.extractfile(f"package/dist/{filename}") as source, (destination / filename).open("wb") as target:
                shutil.copyfileobj(source, target)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("cache", type=Path)
    parser.add_argument("--node", type=Path, help="Use a local copy only if it matches the pinned Node.js release")
    arguments = parser.parse_args()
    prepare(arguments.cache, arguments.node)
