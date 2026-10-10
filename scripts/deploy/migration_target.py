"""Trusted library targets; existing production CLIs have no target override."""

from dataclasses import dataclass
import re

from verify_ci_artifacts import VerificationError


@dataclass(frozen=True)
class ReviewTarget:
    server: str
    database: str
    login: str


PRODUCTION_TARGET = ReviewTarget("homelab-server", "Project1Db", "project1_migrate")


def ci_target(database: str, login: str, read_environment) -> ReviewTarget:
    """Only trusted CI test code calls this; never derived from approval JSON."""
    if (read_environment("GITHUB_ACTIONS") != "true"
            or read_environment("PROJECT1_CI_MIGRATION_TESTS") != "true"
            or not isinstance(database, str)
            or not re.fullmatch(r"Project1CiMigration_[0-9a-f]{32}_upgrade", database)
            or not isinstance(login, str)
            or not re.fullmatch(r"Project1CiExecutor_[0-9a-f]{32}", login)):
        raise VerificationError("CI review targets require explicit opt-in and owned identities.")
    return ReviewTarget("project1-ci-sqlserver", database, login)
