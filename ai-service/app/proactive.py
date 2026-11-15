"""
משימת רשות: הצעת בקרות איכות נתונים באופן יזום, מתוך ניתוח מבנה הנתונים בלבד.

הכללים גנריים - הם מסתכלים על *סוגי* שדות, קשרים ומאפיינים במילון הנתונים
(תאריכי התחלה/סיום, שדות חובה, דגלי "פעיל", אחוזים, סכומים) ולא על שמות קבועים.
מודל שפה אמיתי יקבל את אותו מידע (ראו prompting.build_suggest_prompt) ויוכל להציע גם בקרות מורכבות יותר.
"""
from __future__ import annotations

import re
from typing import Iterable, Optional

from .contracts import (
    ConditionNode, ControlDefinition, ControlSuggestion, GroupNode, RelatedNode,
    SchemaContext, SchemaEntity, SchemaField, ValueSpec,
)

START_HINTS = re.compile(r"start|begin|from|התחל|תחילת", re.IGNORECASE)
END_HINTS = re.compile(r"end|finish|until|סיום", re.IGNORECASE)
PERCENT_HINTS = re.compile(r"percent|pct|אחוז", re.IGNORECASE)
AMOUNT_HINTS = re.compile(r"salary|budget|amount|price|cost|שכר|תקציב|סכום|מחיר", re.IGNORECASE)
ACTIVE_HINTS = re.compile(r"active|פעיל", re.IGNORECASE)


def _text(f: SchemaField) -> str:
    return f"{f.key} {f.name}"


def active_flag(entity: SchemaEntity) -> Optional[SchemaField]:
    return next((f for f in entity.fields if f.data_type == "boolean" and ACTIVE_HINTS.search(_text(f))), None)


def _eq(path: str, value: object) -> ConditionNode:
    return ConditionNode(field=path, operator="eq", value=ValueSpec(value=value))  # type: ignore[arg-type]


def _suggestion(name: str, description: str, category: str, severity: str, entity: SchemaEntity,
                children: list, rationale: str, logic: str = "and", confidence: float = 0.75) -> ControlSuggestion:
    return ControlSuggestion(
        name=name, description=description, category=category, severity=severity,  # type: ignore[arg-type]
        definition=ControlDefinition(root_entity=entity.key, filter=GroupNode(logic=logic, children=children)),  # type: ignore[arg-type]
        confidence=confidence, explanation=rationale,
        assumptions=["הצעה אוטומטית על בסיס מבנה הנתונים - יש לאשר שהכלל העסקי נכון"],
    )


def _date_pairs(entity: SchemaEntity) -> Iterable[ControlSuggestion]:
    dates = [f for f in entity.fields if f.data_type == "date"]
    starts = [f for f in dates if START_HINTS.search(_text(f))]
    ends = [f for f in dates if END_HINTS.search(_text(f))]
    for s in starts:
        for e in ends:
            if s.key == e.key:
                continue
            yield _suggestion(
                f"{e.name} לפני {s.name} ({entity.name})",
                f"{entity.plural_name} שבהם {e.name} מוקדם מ{s.name}",
                "תאריכים", "high", entity,
                [ConditionNode(field=e.key, operator="lt", value=ValueSpec(kind="field", field=s.key))],
                f"נמצא זוג שדות תאריך שנראים כהתחלה/סיום: \"{s.name}\" ו\"{e.name}\" - סיום לפני התחלה הוא כמעט תמיד שגיאה.",
                confidence=0.85)


def _required(entity: SchemaEntity) -> Iterable[ControlSuggestion]:
    required = [f for f in entity.fields if f.is_business_required and f.data_type != "boolean"][:6]
    if not required:
        return
    empties = [ConditionNode(field=f.key, operator="isEmpty") for f in required]
    flag = active_flag(entity)
    children: list = [_eq(flag.key, True), GroupNode(logic="or", children=empties)] if flag else empties  # type: ignore[list-item]
    yield _suggestion(
        f"חסרים נתוני חובה ({entity.name})",
        f"{entity.plural_name}{' פעילים' if flag else ''} שחסר להם אחד משדות החובה: {', '.join(f.name for f in required)}",
        "שלמות נתונים", "medium", entity, children,
        "שדות אלה מסומנים במילון הנתונים כשדות חובה עסקיים.",
        logic="and" if flag else "or")


def _numbers(entity: SchemaEntity) -> Iterable[ControlSuggestion]:
    for f in entity.fields:
        if f.data_type != "number":
            continue
        if PERCENT_HINTS.search(_text(f)):
            yield _suggestion(
                f"{f.name} מחוץ לטווח 0-100 ({entity.name})", f"{f.name} שלילי או מעל 100",
                "טווח ערכים", "medium", entity,
                [ConditionNode(field=f.key, operator="gt", value=ValueSpec(value=100)),
                 ConditionNode(field=f.key, operator="lt", value=ValueSpec(value=0))],
                f"\"{f.name}\" נראה כמו אחוז - ערכים מחוץ ל-0-100 חשודים.", logic="or")
        elif AMOUNT_HINTS.search(_text(f)):
            yield _suggestion(
                f"{f.name} שלילי ({entity.name})", f"{entity.plural_name} שבהם {f.name} קטן מאפס",
                "טווח ערכים", "medium", entity,
                [ConditionNode(field=f.key, operator="lt", value=ValueSpec(value=0))],
                f"\"{f.name}\" נראה כמו סכום כספי - ערך שלילי חשוד.")


def _status_consistency(entity: SchemaEntity, schema: SchemaContext) -> Iterable[ControlSuggestion]:
    flag = active_flag(entity)
    if flag is None:
        return
    for rel in entity.relations:
        target = schema.entity(rel.target_entity)
        target_flag = active_flag(target) if target else None
        if target is None or target_flag is None:
            continue
        if rel.cardinality == "ManyToOne":
            yield _suggestion(
                f"{entity.plural_name} בסטטוס פעיל - {rel.name} אינו פעיל",
                f"{entity.plural_name} פעילים ש{rel.name} אינו פעיל",
                "עקביות סטטוס", "medium", entity,
                [_eq(flag.key, True), _eq(f"{rel.key}.{target_flag.key}", False)],
                f"שני הצדדים של הקשר \"{rel.name}\" מחזיקים דגל פעיל - רשומה פעילה שמצביעה על רשומה לא פעילה היא חוסר עקביות נפוץ.")
        else:
            yield _suggestion(
                f"{entity.plural_name} בסטטוס פעיל ללא {target.plural_name} פעילים",
                f"{entity.plural_name} פעילים שאין להם אף רשומה פעילה ב\"{rel.name}\"",
                "עקביות סטטוס", "low", entity,
                [_eq(flag.key, True),
                 RelatedNode(relation=rel.key, quantifier="none",
                             filter=GroupNode(children=[_eq(target_flag.key, True)]))],
                f"רשומה פעילה בלי אף רשומה פעילה תחתיה (\"{rel.name}\") עשויה להעיד על נתונים יתומים.",
                confidence=0.6)


def suggest_controls(schema: SchemaContext, max_items: int) -> list[ControlSuggestion]:
    suggestions: list[ControlSuggestion] = []
    for entity in schema.entities:
        suggestions += [*_date_pairs(entity), *_required(entity), *_numbers(entity), *_status_consistency(entity, schema)]
    suggestions.sort(key=lambda s: -s.confidence)
    return suggestions[:max_items]
