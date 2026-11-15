"""
בניית ה-Prompt למודל שפה.

ה-Prompt בנוי משלושה חלקים, כולם נגזרים מהנתונים ולא מקוד קשיח:
 1. הוראות מערכת: התפקיד, חוקים (רק מפתחות מהסכמה, לעולם לא SQL), פורמט התשובה.
 2. תיאור מבנה הנתונים העסקי - נבנה מה-SchemaContext שמגיע מ-.NET.
 3. JSON Schema של התשובה (נגזר אוטומטית מ-Pydantic) + דוגמה.
"""
from __future__ import annotations

import json

from .contracts import ControlSuggestion, PromptPreview, SchemaContext

SYSTEM_TEMPLATE = """You translate business data-quality rules written in {language} into a structured control definition.

Rules:
- Output ONLY a single JSON object that matches the response JSON schema. No prose, no markdown.
- Use ONLY entity keys, field keys and relation keys that appear in the data model below. Never invent fields.
- Never output SQL. The definition is executed by a separate engine.
- The "filter" describes the EXCEPTION condition: records that satisfy it are reported as violations.
- Field paths may traverse ManyToOne relations with dots (e.g. "department.isActive").
- For OneToMany relations use a node {{"type":"related","relation":...,"quantifier":"any"|"none","filter":{{...}}}}.
- Compare two fields with value {{"kind":"field","field":"<path>"}}; relative dates with {{"kind":"today","offsetDays":N}}.
- If part of the request cannot be mapped, omit it, lower "confidence" and list the term in "unmatchedTerms".
- Write "name", "description", "explanation", "assumptions" and "warnings" in {language}.
"""

EXAMPLE = {
    "request": "מצא עובדים פעילים שגילם נמוך מ-18",
    "response": {
        "name": "עובדים פעילים מתחת לגיל 18",
        "description": "עובדים פעילים שגילם נמוך מ-18",
        "category": "שכר",
        "severity": "high",
        "confidence": 0.95,
        "definition": {
            "rootEntity": "employee",
            "filter": {"type": "group", "logic": "and", "children": [
                {"type": "condition", "field": "isActive", "operator": "eq", "value": {"kind": "constant", "value": True}},
                {"type": "condition", "field": "age", "operator": "lt", "value": {"kind": "constant", "value": 18}},
            ]},
        },
        "explanation": "'פעילים' → עובד פעיל = כן; 'גילם נמוך מ-18' → גיל < 18",
        "assumptions": [],
        "warnings": [],
        "unmatchedTerms": [],
    },
}


def describe_schema(schema: SchemaContext) -> str:
    """תיאור טקסטואלי קומפקטי של מבנה הנתונים - חסכוני בטוקנים וקריא למודל."""
    lines: list[str] = ["DATA MODEL"]
    for e in schema.entities:
        lines.append(f"\nEntity `{e.key}` — {e.name} / {e.plural_name}"
                     + (f" — {e.description}" if e.description else "")
                     + (f" (synonyms: {', '.join(e.synonyms)})" if e.synonyms else ""))
        for f in e.fields:
            extras = []
            if f.allowed_values:
                extras.append(f"values: {', '.join(f.allowed_values)}")
            if f.is_business_required:
                extras.append("required")
            if f.synonyms:
                extras.append(f"synonyms: {', '.join(f.synonyms)}")
            lines.append(f"  - field `{f.key}` ({f.data_type}) {f.name}"
                         + (f" — {f.description}" if f.description else "")
                         + (f" [{'; '.join(extras)}]" if extras else ""))
        for r in e.relations:
            lines.append(f"  - relation `{r.key}` → `{r.target_entity}` ({r.cardinality}) {r.name}")

    lines.append("\nOPERATORS")
    for o in schema.operators:
        lines.append(f"  - `{o.key}` {o.label} (values: {o.arity}; types: {', '.join(o.applies_to)}"
                     + ("; can compare to another field" if o.allows_field_comparison else "") + ")")
    return "\n".join(lines)


def response_json_schema() -> dict:
    return ControlSuggestion.model_json_schema(by_alias=True)


def build_interpret_prompt(text: str, schema: SchemaContext) -> PromptPreview:
    language = "Hebrew" if schema.locale.startswith("he") else "English"
    system = "\n".join([
        SYSTEM_TEMPLATE.format(language=language),
        describe_schema(schema),
        "\nRESPONSE JSON SCHEMA",
        json.dumps(response_json_schema(), ensure_ascii=False),
        "\nEXAMPLE",
        json.dumps(EXAMPLE, ensure_ascii=False),
    ])
    return PromptPreview(system=system, user=text)


def build_repair_prompt(previous_output: str, problems: list[str]) -> str:
    """הודעת המשך כאשר תשובת המודל לא עברה בדיקה - מבקשת תיקון ממוקד."""
    return ("Your previous answer was invalid:\n- " + "\n- ".join(problems)
            + "\nReturn a corrected JSON object only.\nPrevious answer:\n" + previous_output)


def build_suggest_prompt(schema: SchemaContext, max_items: int) -> PromptPreview:
    system = "\n".join([
        SYSTEM_TEMPLATE.format(language="Hebrew"),
        describe_schema(schema),
        "\nTASK: Analyse the data model and propose data-quality controls that are likely to be valuable "
        "(date ordering, required fields, inconsistent statuses across relations, out-of-range numbers, orphan records).",
        f'Return {{"suggestions": [ ...up to {max_items} objects matching the schema below... ]}}',
        json.dumps(response_json_schema(), ensure_ascii=False),
    ])
    return PromptPreview(system=system, user="הצע בקרות איכות נתונים עבור מבנה הנתונים")
