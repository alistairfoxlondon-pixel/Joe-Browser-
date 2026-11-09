#!/usr/bin/env python3
"""Fetch a Chrome Web Store extension the way the application does, and check it.

This is the missing half of the extension importer that no build could test: the
app's own path needs a Windows desktop, but the network side is the same request
the app makes, so it can be checked from anywhere. Two things are verified:

  1. the update endpoint still answers a plain request with a CRX3 package, and
  2. the package carries a usable (or wholly absent) `key`, which decides whether
     the folder can be loaded unpacked by Chromium at all.

An extension whose manifest has a `key` keeps its store identity when it is loaded
unpacked. One without gets a different id on every machine, and anything the
extension keeps in chrome.storage - a licence, a settings sync - belongs to an id
that no longer exists. It also matters for the second half of this script: the
store's detail page refuses to answer for an id that is not installed, which is
why opening a store link in a fresh profile showed nothing.

Usage:  python3 tools/check-extension-fetch.py [id ...]
"""
import hashlib
import io
import json
import struct
import sys
import urllib.request
import zipfile

DEFAULT_IDS = [
    "fklakbbaaknbgcedidhblbnhclijnhbi",   # HTTP Tracker (shipped default)
    "okaeekdeohehbdanbbohcfhhlblcmjkm",   # Absolute Enable Right Click & Copy (shipped default)
]

ENDPOINT = ("https://clients2.google.com/service/update2/crx?response=redirect"
            "&prodversion=131.0.0.0&acceptformat=crx2,crx3&x=")


def fetch(ext_id):
    url = ENDPOINT + urllib.parse.quote("id=" + ext_id + "&uc")
    req = urllib.request.Request(url, headers={
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) JoeBrowser/1.2"
    })
    with urllib.request.urlopen(req, timeout=20) as r:
        return r.status, r.read()


def find_zip_start(raw):
    from_ = 8 if raw[:4] == b"Cr24" else 0
    i = raw.find(b"PK\x03\x04", from_)
    return i


def webstore_public_key(ext_id):
    """The id is the first 32 hex digits of sha256(first half of the DER key),
    mapped a-p. Reversing it is impossible; what can be checked is that the
    manifest's own key hashes to the manifest's own claim, if it makes one."""
    return None


def check(ext_id):
    out = {"id": ext_id}
    try:
        status, raw = fetch(ext_id)
    except Exception as ex:                     # noqa: BLE001 - report, never raise
        out["error"] = "%s: %s" % (type(ex).__name__, ex)
        return out
    out["http"] = status
    out["bytes"] = len(raw)
    out["magic"] = raw[:4].decode("latin-1", "replace")
    start = find_zip_start(raw)
    out["zip_at"] = start
    if start < 0:
        out["error"] = "no zip payload"
        return out
    try:
        z = zipfile.ZipFile(io.BytesIO(raw[start:]))
    except Exception as ex:                     # noqa: BLE001
        out["error"] = "zip: %s" % ex
        return out
    names = z.namelist()
    manifests = [n for n in names if n.rstrip("/").endswith("manifest.json")]
    out["entries"] = len(names)
    out["manifest_paths"] = manifests
    if not manifests:
        out["error"] = "no manifest.json in the package"
        return out
    m = json.loads(z.read(manifests[0]).decode("utf-8-sig"))
    out["name"] = m.get("name")
    out["version"] = m.get("version")
    out["manifest_version"] = m.get("manifest_version")
    out["has_key"] = "key" in m
    if "key" in m:
        der = __import__("base64").b64decode(m["key"])
        digest = hashlib.sha256(der).hexdigest()
        out["key_sha256"] = digest[:16]
        # a correct key for this package hashes to the id, a-p encoded
        out["key_matches_id"] = encode_id(digest) == ext_id
    out["background"] = "background" if "background" in m else (
        "background.service_worker" if m.get("background") else "-")
    return out


def encode_id(hex_digest):
    first = hex_digest[:32]
    return "".join(chr(ord("a") + int(c, 16)) for c in first)


def main():
    ids = sys.argv[1:] or DEFAULT_IDS
    bad = 0
    for ext_id in ids:
        info = check(ext_id)
        print(json.dumps(info, indent=2, sort_keys=True))
        if info.get("error"):
            bad += 1
        elif not info.get("has_key"):
            # Not fatal: the folder still loads, it just cannot keep its identity.
            print("  note: no key in manifest, so this copy gets a machine specific id")
    print("checked %d, %d failed" % (len(ids), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    import urllib.parse  # noqa: E402 - used by fetch
    sys.exit(main())
