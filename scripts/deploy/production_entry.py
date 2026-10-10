"""Installed fixed entrypoint. Checks code trust before enabling sibling imports."""
import os
from pathlib import Path
import re
import stat
import sys

ROOT = Path("/usr/local/libexec/project1-migration")


def main(arguments=None):
    arguments = sys.argv[1:] if arguments is None else arguments
    try:
        if sys.platform != "linux" or os.geteuid() != 0:
            raise ValueError()
        for path in reversed((ROOT, *ROOT.parents)):
            info = path.lstat()
            if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
                raise ValueError()
        for path in ROOT.iterdir():
            info = path.lstat()
            if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
                    or stat.S_IMODE(info.st_mode) != 0o644 or path.suffix != ".py"):
                raise ValueError()
        sys.dont_write_bytecode = True
        sys.path.insert(0, str(ROOT))
        from production_migration import operate
        from migration_approval_ledger import canonical
        os.umask(0o077)
        if (len(arguments) == 2 and arguments[0] == "execute"
                and re.fullmatch(r"[0-9a-f]{32}", arguments[1])):
            result = operate("execute", arguments[1])
        elif (len(arguments) == 3 and arguments[:2] == ["execute", "--describe"]
                and re.fullmatch(r"[0-9a-f]{32}", arguments[2])):
            result = operate("describe", arguments[2])
        elif arguments and arguments[0] == "admin" and os.environ.get("SUDO_USER") != "project1_deploy":
            if len(arguments) == 2 and arguments[1] in {"initialize", "inspect"}:
                result = operate(arguments[1])
            elif len(arguments) == 3 and arguments[1] == "register" and re.fullmatch(r"[0-9a-f]{32}", arguments[2]):
                result = operate("register", arguments[2])
            else:
                raise ValueError()
        else:
            raise ValueError()
        print(canonical(result).decode("ascii"))
        return 0
    except BaseException:
        print("ERROR: Migration helper refused or failed; administrator must inspect ledger and database before retrying.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
