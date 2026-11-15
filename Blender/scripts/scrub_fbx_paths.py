"""Remove this machine's profile path from FBX string metadata without changing byte offsets."""
import os
import re
import sys
from pathlib import Path


def scrub(path):
    path = Path(path)
    data = original = path.read_bytes()
    if not data.startswith(b"Kaydara FBX Binary"):
        raise ValueError("Not a binary FBX: " + str(path))
    profiles = {os.path.expanduser("~"), os.environ.get("USERPROFILE", "")}
    for profile in profiles:
        if not profile or len(profile) < 5:
            continue
        for text in {profile.replace("\\", "/"), profile.replace("/", "\\") }:
            pattern = re.compile(re.escape(text.encode("utf-8")), re.IGNORECASE)
            data = pattern.sub(lambda match: b"_" * len(match.group()), data)
    if data != original:
        assert len(data) == len(original)
        path.write_bytes(data)
        print("SCRUBBED_FBX_PROFILE", path.name)
    return data != original


if __name__ == "__main__":
    for file in sys.argv[1:]:
        scrub(file)
