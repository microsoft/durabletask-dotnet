import subprocess
import re
import argparse

def get_git_log(tag):
    # try to find previous tag; fall back to the second-latest tag if needed
    try:
        prev_tag = subprocess.check_output(
            "git tag --sort=-creatordate", shell=True, text=True
        ).strip().split("\n")
        # take the tag immediately before the given one
        if tag in prev_tag:
            idx = prev_tag.index(tag)
            previous = prev_tag[idx + 1] if idx + 1 < len(prev_tag) else None
        else:
            previous = prev_tag[1] if len(prev_tag) > 1 else None
    except Exception:
        previous = None

    format_str = "%h||%s||%an"
    if previous:
        cmd = f'git log {previous}..{tag} --pretty=format:"{format_str}"'
    else:
        # fallback: just commits reachable from tag
        cmd = f'git log {tag} --pretty=format:"{format_str}" -n 50'

    print(f"[DEBUG] Running: {cmd}")
    output = subprocess.check_output(cmd, shell=True, text=True, encoding="utf-8", errors="ignore")
    return [line.strip() for line in output.split("\n") if line.strip()]

def extract_pr_info(message):
    pr_match = re.search(r"(?:#|PR\s*)(\d+)", message)
    pr_number = pr_match.group(1) if pr_match else None
    # Strip out PR refs and noise
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
