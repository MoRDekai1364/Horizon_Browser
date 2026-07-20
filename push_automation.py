import os
import sys
import json
import time
import subprocess

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_DIR = os.path.join(SCRIPT_DIR, "github_push")
CONFIG_FILE = os.path.join(CONFIG_DIR, "config.json")

PROCESS_NAMES = ["Horizon.Stealth.exe", "Horizon_Browser.exe", "Horizon Browser.exe"]

BRANCH_FOLDER_MAP = {
    "development_version_alpha": "alpha",
    "development_version_beta": "beta",
    "Horizon_Browser_official_release": "official_release",
}


class ProgressBar:
    def __init__(self, steps):
        self.steps = steps
        self.total = len(steps)
        self.done = 0
        self.start = time.time()
        self._render(label="Starting...")

    def _render(self, label):
        pct = int((self.done / self.total) * 100)
        filled = int((self.done / self.total) * 20)
        bar = "#" * filled + "." * (20 - filled)

        elapsed = time.time() - self.start
        if self.done > 0:
            avg = elapsed / self.done
            remaining = avg * (self.total - self.done)
            eta = f"ETA {int(remaining)}s"
        else:
            eta = "ETA --s"

        sys.stdout.write(f"\r[{bar}] {pct:3d}%  {eta}  {label:<30}")
        sys.stdout.flush()

    def step(self, label):
        self._render(label)

    def advance(self):
        self.done += 1
        label = self.steps[self.done - 1] if self.done <= self.total else "Done"
        self._render(label)
        if self.done >= self.total:
            sys.stdout.write("\n")
            sys.stdout.flush()


def kill_running_instances():
    for name in PROCESS_NAMES:
        subprocess.run(
            ["taskkill", "/IM", name, "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )


def detect_branch(folder_path):
    base = os.path.basename(os.path.normpath(folder_path))
    return BRANCH_FOLDER_MAP.get(base)


def prompt(msg, default=None):
    suffix = f" [{default}]" if default else ""
    val = input(f"{msg}{suffix}: ").strip()
    return val if val else default


def run_git(args, cwd):
    result = subprocess.run(args, cwd=cwd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"ERROR running {' '.join(args)}")
        print(result.stderr.strip())
        sys.exit(1)
    if result.stdout.strip():
        print(result.stdout.strip())
    return result


def fetch_branches(token, repo_path):
    check = subprocess.run(
        ["git", "ls-remote", "--heads", f"https://{token}@github.com/{repo_path}.git"],
        capture_output=True,
        text=True,
    )
    if check.returncode != 0:
        print("Could not reach repository with the given token/repo path.")
        print(check.stderr.strip())
        return None

    branches = []
    for line in check.stdout.splitlines():
        line = line.strip()
        if not line:
            continue
        ref = line.split("\t")[-1]
        prefix = "refs/heads/"
        if ref.startswith(prefix):
            branches.append(ref[len(prefix):])
    return branches


def load_config():
    if os.path.exists(CONFIG_FILE):
        with open(CONFIG_FILE, "r") as f:
            return json.load(f)
    return None


def save_config(cfg):
    try:
        os.makedirs(CONFIG_DIR, exist_ok=True)
        with open(CONFIG_FILE, "w") as f:
            json.dump(cfg, f, indent=2)
    except PermissionError:
        print(f"ERROR: Cannot write to '{os.path.abspath(CONFIG_DIR)}'.")
        print("Check that no file (not folder) named 'github_push' already exists here,")
        print("and that this folder isn't blocked by antivirus or read-only permissions.")
        sys.exit(1)
    except OSError as ex:
        print(f"ERROR saving config: {ex}")
        sys.exit(1)


def first_run_setup():
    cwd = SCRIPT_DIR
    suggested = detect_branch(cwd)

    print("First run — no config found.")
    token = prompt("GitHub Personal Access Token")
    repo_path = prompt("Repository path (owner/repo, no domain)")

    branches = fetch_branches(token, repo_path)
    if branches is None:
        print("Aborting — fix token/repo and re-run.")
        sys.exit(1)
    if not branches:
        print("Repository has no branches, or none were returned.")
        sys.exit(1)

    print("Branches found:")
    for i, b in enumerate(branches, 1):
        marker = "  <- detected from folder name" if b == suggested else ""
        print(f"  [{i}] {b}{marker}")

    default_index = None
    if suggested in branches:
        default_index = branches.index(suggested) + 1

    choice = prompt(
        "Select branch number",
        str(default_index) if default_index else None,
    )
    try:
        branch = branches[int(choice) - 1]
    except (ValueError, IndexError, TypeError):
        print("Invalid selection.")
        sys.exit(1)

    cfg = {
        "token": token,
        "repo_path": repo_path,
        "directory": cwd,
        "branch": branch,
    }
    save_config(cfg)
    print("Config saved to", CONFIG_FILE)
    return cfg


def subsequent_run(cfg):
    print("Saved configuration:")
    print(f"  Directory : {cfg['directory']}")
    print(f"  Repo      : {cfg['repo_path']}")
    print(f"  Branch    : {cfg['branch']}")
    print(f"  Token     : {cfg['token'][:8]}... (hidden)")
    print()
    choice = input("Press [Enter] to accept, or [e] to edit: ").strip().lower()

    if choice == "e":
        cfg["token"] = prompt("GitHub Token", cfg["token"])
        cfg["repo_path"] = prompt("Repository path (owner/repo)", cfg["repo_path"])
        cfg["branch"] = prompt("Branch", cfg["branch"])
        cfg["directory"] = prompt("Local folder directory", cfg["directory"])

        current_mode = cfg.get("no_git_mode", "override")
        mode_choice = prompt(
            "No-git mode (1=override, 2=clone)",
            "1" if current_mode == "override" else "2",
        )
        cfg["no_git_mode"] = "override" if mode_choice == "1" else "clone"

        save_config(cfg)
        print("Config updated.")

    return cfg


def is_git_repo(directory):
    return os.path.isdir(os.path.join(directory, ".git"))


def ensure_repo_mode(cfg):
    directory = cfg["directory"]
    if is_git_repo(directory):
        return

    mode = cfg.get("no_git_mode")
    if mode not in ("override", "clone"):
        print("No git repo found in this folder.")
        print("  [1] Override — push this folder's content as the branch (force)")
        print("  [2] Clone — pull the branch down, replacing local files")
        choice = prompt("Select option", "1")
        mode = "override" if choice == "1" else "clone"
        cfg["no_git_mode"] = mode
        save_config(cfg)

    push_url = f"https://{cfg['token']}@github.com/{cfg['repo_path']}.git"
    branch = cfg["branch"]

    run_git(["git", "init"], directory)
    run_git(["git", "remote", "add", "origin", push_url], directory)

    if mode == "clone":
        run_git(["git", "fetch", "origin", branch], directory)
        run_git(["git", "reset", "--hard", f"origin/{branch}"], directory)
        print(f"Fetched branch '{branch}' — local files replaced with remote content.")
    else:
        print("Repo initialized. Local folder content will overwrite the remote branch on push.")


def do_push(cfg, bar):
    directory = cfg["directory"]
    branch = cfg["branch"]
    token = cfg["token"]
    repo_path = cfg["repo_path"]

    run_git(["git", "add", "."], directory)
    bar.advance()

    msg = input("Commit message [Automated update via push script]: ").strip()
    if not msg:
        msg = "Automated update via push script"

    commit = subprocess.run(
        ["git", "commit", "-m", msg], cwd=directory, capture_output=True, text=True
    )
    if commit.returncode != 0:
        if "nothing to commit" in (commit.stdout + commit.stderr).lower():
            print("\nNothing to commit — working tree clean.")
        else:
            print(commit.stderr.strip())
            sys.exit(1)
    bar.advance()

    push_url = f"https://{token}@github.com/{repo_path}.git"
    push_args = ["git", "push", push_url, f"HEAD:{branch}"]
    if cfg.get("no_git_mode") == "override":
        push_args.insert(2, "--force")
    run_git(push_args, directory)
    print(f"Pushed to branch '{branch}'.")


def main():
    cfg_existing = os.path.exists(CONFIG_FILE)

    steps = ["Closing running instances", "Loading configuration",
             "Preparing repository", "Staging changes", "Committing", "Pushing"]
    bar = ProgressBar(steps)

    kill_running_instances()
    bar.advance()

    if cfg_existing:
        cfg = load_config()
        cfg = subsequent_run(cfg)
    else:
        cfg = first_run_setup()
    bar.advance()

    ensure_repo_mode(cfg)
    bar.advance()

    do_push(cfg, bar)


if __name__ == "__main__":
    main()
