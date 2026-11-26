"""Publishes a test package as a GitHub release: creates the tag's release and attaches the zip.

    python tools/publish-release.py            (the version is read from SP2ServerModPlugin.cs)

Needs: the repository already on GitHub with the commit and the tag v<version> pushed, and the package
built by tools/make-dist.py. The GitHub login is the one git already has (git credential manager); the
token is used in memory only and is never printed or written anywhere. Nothing is sent without being asked
for: run it yourself, or ask for it.
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PLUGIN = os.path.join(ROOT, "SP2-Server-Mod", "SP2ServerModPlugin.cs")
API = "https://api.github.com"
UPLOADS = "https://uploads.github.com"


def git(*args):
    return subprocess.run(["git", "-C", ROOT] + list(args), stdout=subprocess.PIPE, universal_newlines=True, check=True).stdout.strip()


def token_from_git():
    env = dict(os.environ, GCM_INTERACTIVE="never", GIT_TERMINAL_PROMPT="0")
    result = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n", stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, universal_newlines=True, env=env, timeout=60)
    fields = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
    if not fields.get("password"):
        sys.exit("git has no GitHub login stored: sign in once with a git push")
    return fields["password"]


def call(token, method, url, body=None, content_type="application/json", raw=False):
    data = body if raw else (json.dumps(body).encode("utf-8") if body is not None else None)
    request = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "sp2-server-mod-release",
        "Content-Type": content_type,
    })
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            return json.loads(response.read().decode("utf-8") or "{}")
    except urllib.error.HTTPError as e:
        sys.exit("GitHub said " + str(e.code) + " to " + method + " " + url.split("?")[0] + ": " + e.read().decode("utf-8", "replace")[:400])


def main():
    version = re.search(r'public const string Version = "([^"]+)"', open(PLUGIN, encoding="utf-8").read()).group(1)
    tag = "v" + version
    package = os.path.join(ROOT, "dist", "SP2ServerMod-" + version + ".zip")
    if not os.path.exists(package):
        sys.exit("no package at " + package + ": run tools/make-dist.py first")

    remote = git("remote", "get-url", "origin")
    match = re.search(r"github\.com[:/]([^/]+)/([^/]+?)(?:\.git)?$", remote)
    if not match:
        sys.exit("origin is not a GitHub address: " + remote)
    owner, repo = match.group(1), match.group(2)

    if git("rev-parse", tag + "^{commit}") != git("rev-parse", "HEAD"):
        sys.exit("the tag " + tag + " is not on the current commit")
    digest = hashlib.sha256(open(package, "rb").read()).hexdigest()

    token = token_from_git()
    notes = (
        "Test build of the SP2 Server Mod " + version + ".\n\n"
        "Download `" + os.path.basename(package) + "` and unzip it into the SimplePlanes 2 folder (BepInEx 5.4.23.5 x64 is needed). "
        "`README.txt` inside explains how to make a match; `TEST-PLAN.txt` lists what to test and what to send back.\n\n"
        "New in this version: a team can have several crafts and every player with the mod picks one.\n\n"
        "SHA-256 of the zip: `" + digest + "`\n"
    )
    release = call(token, "POST", API + "/repos/" + owner + "/" + repo + "/releases", {
        "tag_name": tag, "target_commitish": "master", "name": "SP2 Server Mod " + version + " (test build)",
        "body": notes, "draft": False, "prerelease": True,
    })
    print("release " + release["html_url"])

    upload = UPLOADS + "/repos/" + owner + "/" + repo + "/releases/" + str(release["id"]) + "/assets?name=" + os.path.basename(package)
    asset = call(token, "POST", upload, open(package, "rb").read(), content_type="application/zip", raw=True)
    print("asset   " + asset["name"] + " (" + str(asset["size"]) + " bytes, state " + asset["state"] + ")")
    if asset["size"] != os.path.getsize(package):
        sys.exit("the uploaded size differs from the file")


if __name__ == "__main__":
    main()
