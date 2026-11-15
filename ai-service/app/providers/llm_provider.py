"""
ספק כללי למודלי שפה (LLM).

LLMProvider אחראי על כל מה שמשותף לכל מודל: בניית Prompt, פענוח JSON מהטקסט שהמודל מחזיר,
ולידציה מול החוזה והסכמה, וניסיון תיקון אחד. ההבדל בין ספקים מצטמצם ל-LLMClient:
פונקציה אחת שמקבלת הודעות ומחזירה טקסט.

כדי לחבר מודל אמיתי בעתיד מממשים LLMClient (OpenAI / Anthropic / Azure / מודל מקומי)
ומגדירים AI_PROVIDER=llm, LLM_CLIENT=<name>. אין צורך לשנות שום דבר אחר במערכת.
"""
from __future__ import annotations

import json
import logging
import re
from pathlib import Path
from typing import Protocol

from pydantic import ValidationError

from ..contracts import ControlSuggestion, SchemaContext, SuggestResponse
from ..prompting import build_interpret_prompt, build_repair_prompt, build_suggest_prompt
from ..schema_check import find_problems
from .base import AIProvider, ProviderError

log = logging.getLogger(__name__)


class LLMClient(Protocol):
    model: str

    def complete(self, system: str, messages: list[dict[str, str]]) -> str:
        """שולח הודעות ומחזיר את הטקסט הגולמי של המודל."""
        ...


_JSON_BLOCK = re.compile(r"\{.*\}", re.DOTALL)


def extract_json(raw: str) -> dict:
    """מודלים נוטים לעטוף JSON ב-```json או בטקסט - מחלצים את האובייקט הראשון."""
    cleaned = raw.strip()
    if cleaned.startswith("```"):
        cleaned = re.sub(r"^```(?:json)?\s*|\s*```$", "", cleaned)
    try:
        return json.loads(cleaned)
    except json.JSONDecodeError:
        match = _JSON_BLOCK.search(cleaned)
        if not match:
            raise ProviderError("Model output does not contain JSON")
        return json.loads(match.group(0))


class LLMProvider(AIProvider):
    name = "llm"

    def __init__(self, client: LLMClient, max_repairs: int = 1):
        self.client = client
        self.model = client.model
        self.max_repairs = max_repairs

    def interpret(self, text: str, schema: SchemaContext) -> ControlSuggestion:
        prompt = build_interpret_prompt(text, schema)
        messages = [{"role": "user", "content": prompt.user}]

        for attempt in range(self.max_repairs + 1):
            raw = self.client.complete(prompt.system, messages)
            problems: list[str]
            try:
                suggestion = ControlSuggestion.model_validate(extract_json(raw))
                problems = find_problems(suggestion.definition, schema) if suggestion.definition else []
                if not problems:
                    return self._stamp(suggestion)
            except (ValidationError, ProviderError, json.JSONDecodeError) as ex:
                problems = [str(ex)[:500]]

            log.warning("LLM answer rejected (attempt %s): %s", attempt + 1, problems)
            messages += [{"role": "assistant", "content": raw},
                         {"role": "user", "content": build_repair_prompt(raw, problems)}]

        return self._stamp(ControlSuggestion(
            name="לא ניתן לפענח את הבקשה",
            confidence=0,
            warnings=["המודל לא הצליח להפיק הגדרה תקינה. נסו לנסח מחדש או להגדיר ידנית."],
        ))

    def suggest(self, schema: SchemaContext, max_items: int) -> list[ControlSuggestion]:
        prompt = build_suggest_prompt(schema, max_items)
        raw = self.client.complete(prompt.system, [{"role": "user", "content": prompt.user}])
        try:
            response = SuggestResponse.model_validate(extract_json(raw))
        except (ValidationError, ProviderError, json.JSONDecodeError) as ex:
            raise ProviderError(f"Invalid suggestions payload: {ex}") from ex
        valid = [s for s in response.suggestions if s.definition and not find_problems(s.definition, schema)]
        return [self._stamp(s) for s in valid[:max_items]]


class RecordedLLMClient:
    """
    Mock של מודל שפה: מחזיר תשובות "מוקלטות" מקובץ JSON לפי טקסט הבקשה.
    מדגים את כל צנרת ה-LLM (Prompt → טקסט גולמי → פענוח → ולידציה → תיקון) בלי קריאה חיצונית.
    """

    model = "recorded-mock"

    def __init__(self, recordings_path: Path):
        self.recordings: dict[str, str] = json.loads(recordings_path.read_text(encoding="utf-8"))
        self.calls: list[tuple[str, list[dict[str, str]]]] = []

    def complete(self, system: str, messages: list[dict[str, str]]) -> str:
        self.calls.append((system, list(messages)))
        last = messages[-1]["content"]
        if last.startswith("Your previous answer was invalid"):
            return self.recordings.get("__repair__", "{}")
        return self.recordings.get(messages[0]["content"].strip(), self.recordings.get("__default__", "{}"))


class NotConfiguredClient:
    """שומר מקום לספק אמיתי (OpenAI / Anthropic וכו'). במבחן אין קריאה חיצונית."""

    def __init__(self, vendor: str):
        self.model = f"{vendor} (not configured)"
        self.vendor = vendor

    def complete(self, system: str, messages: list[dict[str, str]]) -> str:
        raise ProviderError(
            f"LLM client '{self.vendor}' is not configured in this build. "
            "Implement LLMClient.complete() with the vendor SDK and set the API key.")
