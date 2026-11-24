"""Package the exact LGPL dependency source trees for a binary release."""

import argparse
from pathlib import Path
import shutil
import tempfile
import urllib.request
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

REPOSITORIES = [
    ("kovacsv/occt-import-js", "c2148e54b456b571238d35cac037d304053d64b2"),
    ("Open-Cascade-SAS/OCCT", "d2abb6d844231cb8f29be6894440874a4700e4a5"),
]
DESCRIPTION = """Corresponding sources for the STEP 3D Viewer release

Both component archives are the unmodified upstream Git snapshots at the
exact revisions used by occt-import-js 0.0.23. To build the importer, extract
both archives, put the contents of the OCCT tree into the importer tree's
occt/ subdirectory, and follow the importer's build instructions. The
viewer repository supplies its own sources and build instructions.

occt-import-js 0.0.23
https://github.com/kovacsv/occt-import-js/tree/c2148e54b456b571238d35cac037d304053d64b2

Open CASCADE Technology submodule
https://github.com/Open-Cascade-SAS/OCCT/tree/d2abb6d844231cb8f29be6894440874a4700e4a5
"""


def main(output):
    root = Path(__file__).resolve().parents[1]
    with tempfile.TemporaryDirectory() as temporary:
        archives = []
        for repo, revision in REPOSITORIES:
            destination = Path(temporary) / f"{repo.split('/')[-1]}-{revision}.tar.gz"
            url = f"https://codeload.github.com/{repo}/tar.gz/{revision}"
            with urllib.request.urlopen(url, timeout=90) as response, destination.open("wb") as target:
                shutil.copyfileobj(response, target)
            print(f"Downloaded {destination.name}", flush=True)
            archives.append(destination)
        output.parent.mkdir(parents=True, exist_ok=True)
        with ZipFile(output, "w", compression=ZIP_DEFLATED) as bundle:
            bundle.writestr(ZipInfo("README.txt"), DESCRIPTION)
            bundle.write(root / "THIRD-PARTY-NOTICES.txt", "THIRD-PARTY-NOTICES.txt")
            for path in archives:
                bundle.write(path, path.name)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    main(parser.parse_args().output)
