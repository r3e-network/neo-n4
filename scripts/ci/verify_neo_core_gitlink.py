#!/usr/bin/env python3
"""Verify that the checked-out Neo core gitlink is on the canonical R3E branch."""

from __future__ import annotations

import subprocess
from pathlib import Path


CANONICAL_REPOSITORY = "https://github.com/r3e-network/neo.git"
CANONICAL_BRANCH = "r3e/neo-n4-core"
CANONICAL_REF = "refs/remotes/neo-n4-canonical/r3e-neo-n4-core"


def run_git(repository: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", "-C", str(repository), *arguments],
        check=False,
        capture_output=True,
        text=True,
    )


def require_git(repository: Path, *arguments: str) -> str:
    completed = run_git(repository, *arguments)
    if completed.returncode != 0:
        detail = completed.stderr.strip() or completed.stdout.strip()
        raise RuntimeError(f"git {' '.join(arguments)} failed: {detail}")
    return completed.stdout.strip()


def verify_gitlink(
    repository: Path,
    canonical_repository: str = CANONICAL_REPOSITORY,
    canonical_branch: str = CANONICAL_BRANCH,
) -> tuple[str, str]:
    repository = repository.resolve()
    head = require_git(repository, "rev-parse", "HEAD")
    is_shallow = require_git(repository, "rev-parse", "--is-shallow-repository") == "true"
    fetch_arguments = ["fetch", "--force", "--no-tags", "--filter=blob:none"]
    if is_shallow:
        fetch_arguments.append("--unshallow")
    fetch_arguments.extend(
        [
            canonical_repository,
            f"+refs/heads/{canonical_branch}:{CANONICAL_REF}",
        ]
    )
    require_git(repository, *fetch_arguments)
    canonical_head = require_git(repository, "rev-parse", CANONICAL_REF)
    ancestry = run_git(repository, "merge-base", "--is-ancestor", head, canonical_head)
    if ancestry.returncode != 0:
        raise RuntimeError(
            f"external/neo gitlink {head} is not published on canonical "
            f"{canonical_repository} branch {canonical_branch}"
        )
    return head, canonical_head


def main() -> int:
    head, canonical_head = verify_gitlink(Path("external/neo"))
    print(f"Neo core gitlink verified: {head} <= {canonical_head}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
