"""Builds the test package of the SP2 Server Mod.

    python tools/make-dist.py

Compiles the Release DLL, puts it with the README and the test plan (dist-src/, with {version}
filled in from SP2ServerModPlugin.cs) in the folder layout of the game, and zips it:

    dist/SP2ServerMod-<version>.zip
        BepInEx/plugins/SP2ServerMod/SP2ServerMod.dll
        README.txt
        TEST-PLAN.txt

The archive is unzipped into the game folder. Nothing is uploaded or published by this script.
"""
import hashlib
import os
import re
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(ROOT, "SP2-Server-Mod", "SP2-Server-Mod.csproj")
PLUGIN = os.path.join(ROOT, "SP2-Server-Mod", "SP2ServerModPlugin.cs")
DLL = os.path.join(ROOT, "SP2-Server-Mod", "bin", "Release", "netstandard2.1", "SP2ServerMod.dll")
SOURCES = os.path.join(ROOT, "dist-src")
OUT = os.path.join(ROOT, "dist")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main():
    with open(PLUGIN, encoding="utf-8") as f:
        match = re.search(r'public const string Version = "([^"]+)"', f.read())
    if not match:
        sys.exit("could not read the version from SP2ServerModPlugin.cs")
    version = match.group(1)

    print("building Release " + version + " ...")
    result = subprocess.run(["dotnet", "build", PROJECT, "-c", "Release", "-nologo", "-v", "q"],
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, universal_newlines=True)
    if result.returncode != 0:
        print(result.stdout)
        sys.exit("the build failed")
    if not os.path.exists(DLL):
        sys.exit("no DLL at " + DLL)

    os.makedirs(OUT, exist_ok=True)
    zip_path = os.path.join(OUT, "SP2ServerMod-" + version + ".zip")
    if os.path.exists(zip_path):
        os.remove(zip_path)

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(DLL, "BepInEx/plugins/SP2ServerMod/SP2ServerMod.dll")
        for name in ("README.txt", "TEST-PLAN.txt"):
            with open(os.path.join(SOURCES, name), encoding="utf-8") as f:
                text = f.read().replace("{version}", version)
            # Windows line endings: the files are opened in Notepad.
            z.writestr(name, text.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8"))

    print("DLL      SHA-256 " + sha256(DLL))
    print("package  " + zip_path + " (" + str(os.path.getsize(zip_path)) + " bytes)")
    print("package  SHA-256 " + sha256(zip_path))
    with zipfile.ZipFile(zip_path) as z:
        for info in z.infolist():
            print("  " + info.filename + " (" + str(info.file_size) + ")")


if __name__ == "__main__":
    main()
