#!/usr/bin/env python3
"""Import a GitHub Discussion form submission into community-scripts/.

This script is intentionally dependency-free (standard library only). It is
invoked by .github/workflows/import-community-script.yml after a maintainer
comments /import on a Script submissions discussion.

It never executes submitted Lua code. It only writes the .lua file, updates
index.json, pushes a branch and opens a PR for review.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import unicodedata
from pathlib import Path


ALLOWED_ASSOCIATIONS = {"OWNER", "MEMBER", "COLLABORATOR"}
MAX_SCRIPT_BYTES = 256 * 1024
FORM_LABELS = {
    "name": ("脚本名称",),
    "author": ("作者署名",),
    "save_to": ("下载后保存到",),
    "version": ("版本",),
    "description": ("一句话简介",),
    "note": ("备注",),
    "script": ("Lua 代码",),
}


class ImportError(Exception):
    """A submission error that should be reported back to the discussion."""


def env(name: str, default: str = "") -> str:
    return os.environ.get(name, default).strip()


def normalize_newlines(text: str) -> str:
    return (text or "").lstrip("\ufeff").replace("\r\n", "\n").replace("\r", "\n")


def parse_sections(body: str) -> dict[str, str]:
    """Split a GitHub issue/discussion form body into heading -> content."""
    sections: dict[str, str] = {}
    current: str | None = None
    lines: list[str] = []

    for line in normalize_newlines(body).split("\n"):
        if line.startswith("### "):
            if current is not None:
                sections[current] = "\n".join(lines).strip()
            current = line[4:].strip()
            lines = []
        elif current is not None:
            lines.append(line)

    if current is not None:
        sections[current] = "\n".join(lines).strip()

    return sections


def get_section(sections: dict[str, str], labels: tuple[str, ...]) -> str:
    for heading, value in sections.items():
        if all(label in heading for label in labels):
            return value.strip()
    return ""


def extract_lua_code(section: str) -> str:
    """Accept a fenced Lua block or the plain textarea content."""
    text = normalize_newlines(section).strip()

    # GitHub may keep a markdown fence if the submitter pasted one.
    match = re.search(r"```[ \t]*(?:lua|Lua)?[ \t]*\n(.*?)```", text, re.S)
    if match:
        return match.group(1).strip("\n").rstrip()

    # Remove a leading language marker if the textarea itself contains one.
    text = re.sub(r"^[ \t]*```[ \t]*(?:lua|Lua)?[ \t]*\n?", "", text)
    text = re.sub(r"\n?```[ \t]*$", "", text)
    return text.strip("\n").rstrip()


def parse_form(body: str) -> dict[str, object]:
    sections = parse_sections(body)

    name = get_section(sections, FORM_LABELS["name"])
    author = get_section(sections, FORM_LABELS["author"])
    save_to_raw = get_section(sections, FORM_LABELS["save_to"])
    version_raw = get_section(sections, FORM_LABELS["version"])
    description = get_section(sections, FORM_LABELS["description"])
    note = get_section(sections, FORM_LABELS["note"])
    script = extract_lua_code(get_section(sections, FORM_LABELS["script"]))

    if not name:
        raise ImportError("缺少“脚本名称”")
    if not description:
        raise ImportError("缺少“一句话简介”")
    if not script:
        raise ImportError("缺少“Lua 代码”")

    try:
        version = int(version_raw or "1")
    except ValueError as exc:
        raise ImportError(f"版本必须是整数：{version_raw!r}") from exc
    if version < 1:
        raise ImportError("版本必须大于等于 1")

    save_to_text = save_to_raw.lower()
    if "send" in save_to_text or "发送" in save_to_raw:
        save_to = "send"
    elif "recv" in save_to_text or "接收" in save_to_raw:
        save_to = "recv"
    else:
        save_to = "run"

    if len(script.encode("utf-8")) > MAX_SCRIPT_BYTES:
        raise ImportError(f"脚本超过 {MAX_SCRIPT_BYTES // 1024} KB 限制")
    if "\x00" in script:
        raise ImportError("脚本包含 NUL 字节，可能是二进制内容")

    return {
        "name": name[:100].strip(),
        "author": (author or "anonymous")[:100].strip(),
        "description": " ".join(description.split())[:300],
        "version": version,
        "saveTo": save_to,
        "note": note,
        "script": script.rstrip() + "\n",
    }


def slugify(name: str, fallback: str) -> str:
    normalized = unicodedata.normalize("NFKD", name)
    ascii_name = normalized.encode("ascii", "ignore").decode("ascii")
    slug = re.sub(r"[^a-zA-Z0-9]+", "-", ascii_name).strip("-").lower()
    slug = slug[:60].strip("-")
    return slug or fallback


def choose_file_name(slug: str, existing_files: set[str]) -> str:
    candidate = f"{slug}.lua"
    if candidate not in existing_files:
        return candidate
    for index in range(2, 100):
        candidate = f"{slug}-{index}.lua"
        if candidate not in existing_files:
            return candidate
    raise ImportError("无法生成不重复的脚本文件名")


def load_index(index_path: Path) -> dict:
    if not index_path.exists():
        return {"schema": 1, "scripts": []}
    with index_path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    if not isinstance(data, dict) or not isinstance(data.get("scripts", []), list):
        raise ImportError("community-scripts/index.json 格式不正确")
    data.setdefault("schema", 1)
    data.setdefault("scripts", [])
    return data


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def run(cmd: list[str], *, capture: bool = False) -> str:
    try:
        result = subprocess.run(
            cmd,
            check=True,
            text=True,
            stdout=subprocess.PIPE if capture else None,
            stderr=subprocess.STDOUT if capture else None,
        )
    except subprocess.CalledProcessError as exc:
        output = (exc.stdout or "").strip() if capture else ""
        detail = f"\n{output}" if output else ""
        raise ImportError(f"命令失败：{' '.join(cmd)}{detail}") from exc
    return (result.stdout or "").strip()


def comment_discussion(discussion_id: str, body: str) -> None:
    if not discussion_id:
        return
    query = (
        "mutation($discussionId: ID!, $body: String!) {"
        " addDiscussionComment(input: {discussionId: $discussionId, body: $body}) {"
        " comment { url } } }"
    )
    run(
        [
            "gh",
            "api",
            "graphql",
            "-f",
            f"query={query}",
            "-f",
            f"discussionId={discussion_id}",
            "-f",
            f"body={body}",
        ]
    )


def build_pr_body(fields: dict[str, object], discussion_url: str, author: str) -> str:
    note = fields["note"] or "(none)"
    return "\n".join(
        [
            "Automated import from a Script submissions Discussion.",
            "",
            f"- Discussion: {discussion_url}",
            f"- Submitted by: @{author}",
            f"- Script name: {fields['name']}",
            f"- Author: {fields['author']}",
            f"- saveTo: `{fields['saveTo']}`",
            f"- Version: {fields['version']}",
            f"- Description: {fields['description']}",
            f"- Note: {note}",
            "",
            "The submitted Lua code was **not executed** by this automation.",
            "Please review the script and merge if acceptable.",
        ]
    )


def create_pull_request(
    repository: str,
    branch: str,
    base: str,
    title: str,
    body: str,
    default_branch: str,
) -> str:
    """Create a PR; fall back to the REST API and include a manual link on failure."""
    first_error = ""
    try:
        return run(
            [
                "gh",
                "pr",
                "create",
                "--repo",
                repository,
                "--base",
                base,
                "--head",
                branch,
                "--title",
                title,
                "--body",
                body,
            ],
            capture=True,
        )
    except Exception as exc:
        first_error = str(exc)

    try:
        return run(
            [
                "gh",
                "api",
                "--method",
                "POST",
                f"repos/{repository}/pulls",
                "-f",
                f"title={title}",
                "-f",
                f"head={branch}",
                "-f",
                f"base={base}",
                "-f",
                f"body={body}",
                "--jq",
                ".html_url",
            ],
            capture=True,
        )
    except Exception as exc:
        second_error = str(exc)

    lower = f"{first_error}\n{second_error}".lower()
    hint = ""
    if (
        "not permitted" in lower
        or "create or approve pull requests" in lower
        or "resource not accessible by integration" in lower
    ):
        hint = (
            "请在仓库 Settings → Actions → General → Workflow permissions 中勾选 "
            "“Allow GitHub Actions to create and approve pull requests”，然后重新评论 /import。"
        )
    raise ImportError(
        "创建 PR 失败；失败分支会自动清理，请修复权限后重新评论 /import。\n"
        f"gh pr create: {first_error}\n"
        f"gh api fallback: {second_error}\n"
        f"{hint}"
    )


def delete_remote_branch(branch: str) -> None:
    try:
        run(["git", "push", "origin", "--delete", branch])
        print(f"已清理失败分支：{branch}", file=sys.stderr)
    except Exception as exc:
        print(f"失败分支清理失败：{branch}: {exc}", file=sys.stderr)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true", help="parse only; do not write/PR")
    parser.add_argument("--body-file", help="local form body for testing")
    args = parser.parse_args()

    body = env("DISCUSSION_BODY")
    if args.body_file:
        body = Path(args.body_file).read_text(encoding="utf-8")

    fields = parse_form(body)

    if args.dry_run:
        print(json.dumps(fields, ensure_ascii=False, indent=2))
        return 0

    association = env("COMMENT_ASSOCIATION")
    if association not in ALLOWED_ASSOCIATIONS:
        raise ImportError(f"评论作者权限不足（{association or 'unknown'}）")

    category = env("DISCUSSION_CATEGORY")
    if category != "script-submissions":
        raise ImportError(f"不是 script-submissions 分类（{category or 'unknown'}）")

    number = env("DISCUSSION_NUMBER")
    repository = env("REPOSITORY") or env("GITHUB_REPOSITORY")
    default_branch = env("DEFAULT_BRANCH", "master")
    discussion_id = env("DISCUSSION_ID")
    discussion_url = env("DISCUSSION_URL")
    run_id = env("RUN_ID", "manual")
    comment_author = env("COMMENT_AUTHOR", "maintainer")

    if not number or not repository:
        raise ImportError("缺少 Discussion 编号或仓库信息")

    # github.event.discussion.html_url 在部分 webhook 里可能缺少 owner/repo；
    # 用 repository + number 重建，避免 PR 正文出现 https://github.com//discussions/3。
    expected_discussion_prefix = f"https://github.com/{repository}/discussions/"
    if not discussion_url.startswith(expected_discussion_prefix):
        discussion_url = f"https://github.com/{repository}/discussions/{number}"

    repo_root = Path(__file__).resolve().parents[2]
    community_dir = repo_root / "community-scripts"
    index_path = community_dir / "index.json"

    data = load_index(index_path)
    existing_names = {str(item.get("name", "")).casefold() for item in data["scripts"]}
    if fields["name"].casefold() in existing_names:
        raise ImportError(f"已存在同名脚本：{fields['name']}")

    existing_files = {
        path.name for path in community_dir.iterdir() if path.is_file()
    } if community_dir.exists() else set()
    file_name = choose_file_name(
        slugify(str(fields["name"]), f"script-{number}"), existing_files
    )
    file_url = (
        f"https://github.com/{repository}/blob/{default_branch}/"
        f"community-scripts/{file_name}"
    )

    entry = {
        "name": fields["name"],
        "author": fields["author"],
        "description": fields["description"],
        "version": fields["version"],
        "saveTo": fields["saveTo"],
        "note": fields["note"],
        "file": file_name,
        "url": file_url,
    }

    script_path = community_dir / file_name
    write_text(script_path, str(fields["script"]))
    data["scripts"].append(entry)
    write_text(
        index_path,
        json.dumps(data, ensure_ascii=False, indent=2) + "\n",
    )

    branch = f"community-scripts/import-{number}-{run_id}"
    run(["git", "config", "user.name", "github-actions[bot]"])
    run(
        [
            "git",
            "config",
            "user.email",
            "41898282+github-actions[bot]@users.noreply.github.com",
        ]
    )
    run(["git", "checkout", "-B", branch])
    run(["git", "add", str(script_path.relative_to(repo_root)), str(index_path.relative_to(repo_root))])
    run(["git", "commit", "-m", f"feat: import community script {fields['name']}"])
    run(["git", "push", "--set-upstream", "origin", branch])

    pr_body = build_pr_body(fields, discussion_url, comment_author)
    pr_url = ""
    try:
        pr_url = create_pull_request(
            repository,
            branch,
            default_branch,
            f"[Script] {fields['name']}",
            pr_body,
            default_branch,
        )
        try:
            comment_discussion(
                discussion_id,
                f"已生成 PR：{pr_url}\n\n请审核后合并。",
            )
        except Exception as comment_exc:
            print(f"PR 已创建，但无法回复 Discussion：{comment_exc}", file=sys.stderr)
        print(pr_url)
        return 0
    except Exception:
        # 只有“分支已推送但 PR 未创建”时才清理，避免误删已有 PR 的分支。
        if not pr_url:
            delete_remote_branch(branch)
        raise


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # report failures back to the discussion when possible
        message = f"自动导入失败：{exc}"
        print(message, file=sys.stderr)
        try:
            if not os.environ.get("DRY_RUN"):
                comment_discussion(env("DISCUSSION_ID"), message)
        except Exception as comment_exc:
            print(f"无法回复 Discussion：{comment_exc}", file=sys.stderr)
        sys.exit(1)
