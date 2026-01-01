#!/usr/bin/env python3
"""Generate training data using Foundry Local as a teacher model.

Outputs JSONL with schema:
  {"instruction": "...", "output": "..."}

Requires:
  pip install openai foundry-local-sdk

This script intentionally sanitizes + validates outputs to keep the dataset clean.
"""

from __future__ import annotations

import argparse
import json
import random
import re
import sys
import time
from dataclasses import dataclass
from pathlib import Path

import openai
from foundry_local import FoundryLocalManager

from prompting import normalize_model_output


SYSTEM_PROMPT = (
    "You generate GitHub search queries. "
    "Return ONLY a valid GitHub search query string. "
    "Do not include explanations, prefixes, code fences, or extra lines."
)


@dataclass(frozen=True)
class GenerationConfig:
    alias: str
    temperature: float
    top_p: float
    max_tokens: int


_ALLOWED_CHARS_RE = re.compile(r"^[\w\s\-\.:/\"'()\[\]@#+*,=<>!]+$")


def sanitize_instruction(text: str) -> str:
    t = (text or "").strip()
    t = re.sub(r"\s+", " ", t)
    return t


def sanitize_query(text: str) -> str:
    t = normalize_model_output(text)
    t = t.strip().strip('`"')
    t = t.replace("\r\n", "\n")
    t = t.split("\n", 1)[0].strip()
    t = re.sub(r"\s+", " ", t)

    # Remove stray trailing punctuation from model "chatty" completions.
    while t.endswith((".", ",", ";")):
        t = t[:-1].rstrip()

    return t


def validate_query(q: str) -> tuple[bool, str]:
    if not q:
        return False, "empty"
    if len(q) > 200:
        return False, "too_long"
    if "```" in q:
        return False, "code_fence"
    if "\n" in q or "\r" in q:
        return False, "multiline"
    if not any(ch.isalnum() for ch in q):
        return False, "no_alnum"
    if not _ALLOWED_CHARS_RE.match(q):
        return False, "bad_chars"
    return True, "ok"


_LANGS = [
    "C#", "Python", "JavaScript", "TypeScript", "Go", "Rust", "Java", "Kotlin", "Swift"
]

_TOPICS = [
    "authentication", "rate limiting", "GraphQL", "REST API", "unit testing", "dependency injection",
    "async/await", "logging", "caching", "CI/CD", "Docker", "Kubernetes"
]

_QUALIFIERS = [
    "stars:>100", "stars:>500", "forks:>50", "archived:false", "is:public", "size:<50000",
    "pushed:>2024-01-01", "license:mit", "license:apache-2.0"
]


def random_instruction(rng: random.Random) -> str:
    lang = rng.choice(_LANGS)
    topic = rng.choice(_TOPICS)
    q1 = rng.choice(_QUALIFIERS)
    q2 = rng.choice(_QUALIFIERS)

    templates = [
        f"Find GitHub repositories about {topic} written in {lang}. Prefer actively maintained repos and include useful qualifiers.",
        f"Search for {lang} repos related to {topic}. Add qualifiers to narrow results.",
        f"Find open-source repos for {topic} in {lang} with good popularity signals.",
        f"Find repos about {topic} in {lang}. Must match {q1} and {q2}.",
        f"Find {lang} repositories about {topic}. Exclude archived repos.",
    ]

    return rng.choice(templates)


def generate_query(client: openai.OpenAI, model_id: str, instruction: str, cfg: GenerationConfig) -> str:
    resp = client.chat.completions.create(
        model=model_id,
        messages=[
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": instruction},
        ],
        temperature=cfg.temperature,
        top_p=cfg.top_p,
        max_tokens=cfg.max_tokens,
    )
    return resp.choices[0].message.content or ""


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--alias", default="qwen2.5-0.5b", help="Foundry Local model alias or model id")
    ap.add_argument("--count", type=int, default=200, help="How many valid examples to write")
    ap.add_argument("--out_file", default="github_queries.generated.jsonl", help="Output JSONL file")
    ap.add_argument("--seed", type=int, default=1337)
    ap.add_argument("--temperature", type=float, default=0.2)
    ap.add_argument("--top_p", type=float, default=0.95)
    ap.add_argument("--max_tokens", type=int, default=80)
    ap.add_argument("--max_attempts", type=int, default=5000, help="Hard cap to avoid infinite loops")
    ap.add_argument("--log_rejects", action="store_true", help="Write rejects to a .rejects.jsonl file")
    args = ap.parse_args()

    out_path = Path(args.out_file)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    rejects_path = out_path.with_suffix(out_path.suffix + ".rejects.jsonl")

    rng = random.Random(args.seed)
    cfg = GenerationConfig(
        alias=args.alias,
        temperature=args.temperature,
        top_p=args.top_p,
        max_tokens=args.max_tokens,
    )

    manager = FoundryLocalManager(args.alias)
    client = openai.OpenAI(base_url=manager.endpoint, api_key=manager.api_key)

    model_id = manager.get_model_info(args.alias).id

    seen_outputs: set[str] = set()

    # When appending, preload existing outputs so we don't write duplicates across runs.
    if out_path.exists():
        try:
            with out_path.open("r", encoding="utf-8") as f_in:
                for line in f_in:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        obj = json.loads(line)
                    except Exception:
                        continue
                    q = obj.get("output")
                    if isinstance(q, str) and q:
                        seen_outputs.add(q)
        except Exception:
            # Best-effort: if the file is unreadable, still append new data.
            pass

    written = 0
    attempts = 0

    with out_path.open("a", encoding="utf-8") as f_out:
        f_rej = rejects_path.open("a", encoding="utf-8") if args.log_rejects else None
        try:
            while written < args.count and attempts < args.max_attempts:
                attempts += 1

                instruction = sanitize_instruction(random_instruction(rng))

                try:
                    raw = generate_query(client, model_id, instruction, cfg)
                except Exception as e:
                    # Transient failures happen while Foundry boots/models load.
                    if f_rej:
                        f_rej.write(json.dumps({"instruction": instruction, "raw": None, "reason": f"exception:{type(e).__name__}"}) + "\n")
                    time.sleep(0.2)
                    continue

                q = sanitize_query(raw)
                ok, reason = validate_query(q)
                if not ok:
                    if f_rej:
                        f_rej.write(json.dumps({"instruction": instruction, "raw": raw, "query": q, "reason": reason}) + "\n")
                    continue

                if q in seen_outputs:
                    if f_rej:
                        f_rej.write(json.dumps({"instruction": instruction, "raw": raw, "query": q, "reason": "duplicate"}) + "\n")
                    continue

                seen_outputs.add(q)
                f_out.write(json.dumps({"instruction": instruction, "output": q}, ensure_ascii=False) + "\n")
                written += 1

                if written % 25 == 0:
                    print(f"[{written}/{args.count}] wrote examples (attempts={attempts})")

        finally:
            if f_rej:
                f_rej.close()

    print(f"Wrote {written} examples to {out_path}")
    if args.log_rejects:
        print(f"Rejects written to {rejects_path}")

    if written < args.count:
        print(f"Warning: only produced {written}/{args.count} valid examples (attempts={attempts}).")
        return 2

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
