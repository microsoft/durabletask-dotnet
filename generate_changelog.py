import subprocess
import re
import argparse
import sys

def run_cmd(cmd):
    """Run shell command and return stdout as string."""
    try:
        return subprocess.check_output(cmd, shell=True, text=True, encoding="utf-8", errors="ignore").strip()
    except subprocess.CalledProcessError as e:
        return ""

def get_latest_tag():
    tags = run_cmd("git tag --sort=-creatordate").split("\n")
    return tags[0] if tags and tags[0] else None

def branch_exists(branch):
    result = run_cmd(f"git rev-parse --verify {branch}")
    return bool(result)

def tag_exists(tag):
    result = run_cmd(f"git rev-parse --verify refs/tags/{tag}")
    return bool(result)

def get_git_log(tag):
    """Generate git log range depending on tag availability."""
    tags = [t for t in run_cmd("git tag --sort=-creatordate").split("\n") if t]
    latest_tag = tags[0] if tags else None

    if tag_exists(tag):
        # Tag exists: find the one before it
        if tag in tags:
            idx = tags.index(tag)
            previous = tags[idx + 1] if idx + 1 < len(tags) else None
        else:
            previous = tags[1] if len(tags) > 1 else None
        cmd = f'git log {previous}..{tag} --pretty=format:"%h||%s||%an"' if previous else f'git log {tag} --pretty=format:"%h||%s||%an" -n 50'
    else:
        # Tag does not exist -> generate diff between latest tag and main
        print(f"[WARN] Tag '{tag}' not found, generating changelog between latest tag '{latest_tag}' and 'main'")
        if not latest_tag:
            cmd = f'git log main --pretty=format:"%h||%s||%an" -n 50'
        else:
            cmd = f'git log {latest_tag}..main --pretty=format:"%h||%s||%an"'

    print(f"[DEBUG] Running: {cmd}")
    output = run_cmd(cmd)
    return [line.strip() for line in output.split("\n") if line.strip()]

def extract_pr_info(message):
    pr_match = re.search(r"(?:#|PR\s*)(\d+)", message)
    pr_number = pr_match.group(1) if pr_match else None
    clean = re.sub(r"\(?#\d+\)?|Merge pull request.*|from.*|PR\s*\d+", "", message)
    clean = clean.strip().capitalize()
    return clean, pr_number

def generate_changelog(tag):
    lines = get_git_log(tag)
    if not lines:
        return f"## {tag}\n*(No commits found in range)*"

    entries = []
    for line in lines:
        parts = line.split("||")
        if len(parts) < 3:
            continue
        _, message, author = parts
        title, pr_number = extract_pr_info(message)
        if not title:
            continue
        pr_link = f"https://github.com/microsoft/durabletask-dotnet/pull/{pr_number}" if pr_number else ""
        entries.append(f"- {title} by {author} ({f'[#'+pr_number+']('+pr_link+')' if pr_number else ''})")

    return f"## {tag}\n" + "\n".join(entries)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate changelog from git commits.")
    parser.add_argument("--tag", required=True, help="Git tag to generate changelog for.")
    args = parser.parse_args()

    print("# Changelog\n")
    print(generate_changelog(args.tag))
