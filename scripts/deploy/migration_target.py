"""Fixed read-only precheck target; the CLI has no target override."""

from dataclasses import dataclass


@dataclass(frozen=True)
class ReviewTarget:
    server: str
    database: str
    login: str


PRODUCTION_TARGET = ReviewTarget("homelab-server", "Project1Db", "project1_migrate")
