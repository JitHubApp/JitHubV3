"""Shared prompt formatting for training + inference.

Keep training and inference prompts identical.
"""

from __future__ import annotations

import re
from dataclasses import dataclass


DEFAULT_SYSTEM_PROMPT = (
    "You generate GitHub search queries. "
    "Return ONLY a valid GitHub search query string. "
    "Do not include explanations, prefixes, code fences, or extra lines."
)


@dataclass(frozen=True)
class PromptConfig:
    system_prompt: str = DEFAULT_SYSTEM_PROMPT


def format_prompt(instruction: str, cfg: PromptConfig | None = None) -> str:
    cfg = cfg or PromptConfig()
    instruction = (instruction or "").strip()
    return (
        f"System: {cfg.system_prompt}\n"
        f"Instruction: {instruction}\n"
        "Response:"
    )


def format_training_text(instruction: str, output: str, cfg: PromptConfig | None = None) -> str:
    output = (output or "").strip()
    return f"{format_prompt(instruction, cfg)} {output}".strip()


_response_prefix_re = re.compile(r"^(response\s*:\s*)", re.IGNORECASE)
_query_prefix_re = re.compile(r"^(query\s*:\s*)", re.IGNORECASE)


def normalize_model_output(text: str) -> str:
    """Extract a single-line query from model output."""
    if text is None:
        return ""

    t = text.strip().strip("`\"")
    t = t.replace("\r\n", "\n")
    t = t.split("\n", 1)[0].strip()

    t = _response_prefix_re.sub("", t).strip()
    t = _query_prefix_re.sub("", t).strip()

    # Collapse whitespace to keep queries single-line and stable.
    t = re.sub(r"\s+", " ", t).strip()
    return t
