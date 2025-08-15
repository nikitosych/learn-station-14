#!/usr/bin/env python3

import argparse
import requests
import os
import subprocess
from typing import Iterable

PUBLISH_TOKEN = os.environ["PUBLISH_TOKEN"]
VERSION = os.environ["GITHUB_SHA"]

RELEASE_DIR = "release"

#
# CONFIGURATION PARAMETERS
# Forks should change these to publish to their own infrastructure.
#
ROBUST_CDN_URL = "https://central.ss14.pl/"
FORK_ID = "polonium"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fork-id", default=FORK_ID)

    args = parser.parse_args()
    fork_id = args.fork_id

    session = requests.Session()
    session.headers = {
        "Authorization": f"Bearer {PUBLISH_TOKEN}",
    }

    print(f"Starting publish on Robust.Cdn for version {VERSION}")
    print(f"Token length: {len(PUBLISH_TOKEN)}")

    data = {
        "version": VERSION,
        "engineVersion": get_engine_version(),
    }
    headers = {
        "Content-Type": "application/json"
    }
    post_with_logging(session, f"{ROBUST_CDN_URL}fork/{fork_id}/publish/start", json=data, headers=headers)

    print("Publish successfully started, adding files...")

    for file in get_files_to_publish():
        print(f"Publishing {file}")
        with open(file, "rb") as f:
            headers = {
                "Content-Type": "application/octet-stream",
                "Robust-Cdn-Publish-File": os.path.basename(file),
                "Robust-Cdn-Publish-Version": VERSION
            }
            post_with_logging(session, f"{ROBUST_CDN_URL}fork/{fork_id}/publish/file", data=f, headers=headers)

    print("Successfully pushed files, finishing publish...")

    data = {
        "version": VERSION
    }
    headers = {
        "Content-Type": "application/json"
    }
    
    post_with_logging(session, f"{ROBUST_CDN_URL}fork/{fork_id}/publish/finish", json=data, headers=headers)

    print("SUCCESS!")

def post_with_logging(session: requests.Session, url: str, **kwargs) -> requests.Response:
    resp = session.post(url, **kwargs)
    print(f"\nHTTP {resp.status_code} for {url}")
    print("Response headers:")
    for k, v in resp.headers.items():
        print(f"  {k}: {v}")
    try:
        print("Response JSON:")
        print(resp.json())
    except ValueError:
        print("Response text:")
        print(resp.text)

    if not resp.ok:
        resp.raise_for_status()

    return resp

def get_files_to_publish() -> Iterable[str]:
    for file in os.listdir(RELEASE_DIR):
        yield os.path.join(RELEASE_DIR, file)


def get_engine_version() -> str:
    proc = subprocess.run(["git", "describe","--tags", "--abbrev=0"], stdout=subprocess.PIPE, cwd="RobustToolbox", check=True, encoding="UTF-8")
    tag = proc.stdout.strip()
    assert tag.startswith("v")
    print(f"Engine version: {tag[1:]}")
    return tag[1:] # Cut off v prefix.


if __name__ == '__main__':
    main()
