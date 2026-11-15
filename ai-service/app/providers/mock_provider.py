"""
ספק Mock מבוסס-כללים לעברית.

לא מדובר בתשובות קבועות: המפענח לומד את אוצר המילים מה-SchemaContext (שמות ומילים נרדפות
של ישויות ושדות שמוגדרים במילון הנתונים), ולכן הוא עובד גם על טבלאות/שדות חדשים ועל ניסוחים שונים.
הוא מדמה את מה שמודל אמיתי יעשה ומחזיר תשובה באותו מבנה בדיוק (ControlSuggestion).

מה נתמך:
  • זיהוי הישות הראשית ("עובדים", "מחלקות"...)
  • שדות לפי מילים נרדפות, כולל אותיות שימוש (ו/ש/ה/ב/ל/מ/כ)
  • השוואות: נמוך/גבוה/מעל/מתחת/לפחות/עד/מוקדם/מאוחר/שווה/שונה/מכיל
  • ערכים: מספרים (כולל 15,000), תאריכים, "היום", השוואה לשדה אחר
  • שדות כן/לא ("פעילים", "שאינה פעילה")
  • חוסר ערך ("ללא דוא"ל", "חסרה ת"ז")
  • ערכים מתוך רשימה סגורה ("משרה חלקית", "הסתיים")
  • קשרים: נתיב ("למחלקה שאינה פעילה") ו"קיים/לא קיים" ("מחלקות ללא עובדים פעילים")
  • "או" (תנאי סטטוס נשארים ב"וגם" מעל קבוצת ה"או")
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field as dc_field
from typing import Optional

from ..contracts import (
    ConditionNode, ControlDefinition, ControlSuggestion, GroupNode, RelatedNode,
    SchemaContext, SchemaEntity, SchemaField, ValueSpec,
)
from ..proactive import suggest_controls
from .base import AIProvider

HEB = "א-ת"
PREFIX = "[והשבלמכ]{0,3}"

NEGATIONS = ("לא", "שאינו", "שאינה", "שאינם", "שאינן", "אינו", "אינה", "אינם", "אינן", "שלא")
WITHOUT = ("ללא", "בלי", "אין", "שאין", "שאין בה", "שאין בו", "שאין בהן", "שאין בהם")
EMPTY_BEFORE = ("ללא", "בלי", "חסר", "חסרה", "חסרים", "חסרות", "שחסר", "שחסרה", "שחסרים", "שחסרות", "ריק", "ריקה")
EMPTY_AFTER = ("חסר", "חסרה", "חסרים", "חסרות", "ריק", "ריקה", "ריקים", "לא הוזן", "לא הוזנה", "לא מולא", "לא מולאה")
NOT_EMPTY_AFTER = ("לא ריק", "לא ריקה", "מלא", "מלאה", "קיים", "קיימת", "הוזן", "הוזנה")

# (ביטוי, אופרטור) - נבדקים לפי מיקום; בתיקו - הביטוי הארוך מנצח
OPERATOR_PHRASES: list[tuple[str, str]] = [
    ("קטן או שווה", "lte"), ("נמוך או שווה", "lte"), ("לכל היותר", "lte"), ("עד", "lte"),
    ("גדול או שווה", "gte"), ("גבוה או שווה", "gte"), ("לפחות", "gte"),
    ("לא מכיל", "notContains"), ("אינו מכיל", "notContains"), ("אינה מכילה", "notContains"),
    ("לא מכילה", "notContains"), ("ללא התו", "notContains"), ("בלי התו", "notContains"),
    ("מכיל", "contains"), ("מכילה", "contains"), ("כולל", "contains"),
    ("מתחיל ב", "startsWith"), ("מתחילה ב", "startsWith"), ("מסתיים ב", "endsWith"), ("מסתיימת ב", "endsWith"),
    ("שונה", "neq"), ("לא שווה", "neq"), ("אינו שווה", "neq"),
    ("שווה", "eq"), ("בדיוק", "eq"),
    ("נמוך", "lt"), ("נמוכה", "lt"), ("נמוכים", "lt"), ("נמוכות", "lt"),
    ("קטן", "lt"), ("קטנה", "lt"), ("קטנים", "lt"), ("פחות", "lt"), ("מתחת", "lt"),
    ("מוקדם", "lt"), ("מוקדמת", "lt"), ("מוקדמים", "lt"), ("לפני", "lt"),
    ("גבוה", "gt"), ("גבוהה", "gt"), ("גבוהים", "gt"), ("גבוהות", "gt"),
    ("גדול", "gt"), ("גדולה", "gt"), ("גדולים", "gt"), ("יותר", "gt"), ("מעל", "gt"), ("עולה על", "gt"),
    ("מאוחר", "gt"), ("מאוחרת", "gt"), ("מאוחרים", "gt"), ("אחרי", "gt"),
]

LEADING_VERBS = ("מצא", "מצאו", "תמצא", "איתור", "אתר", "הצג", "הציגו", "חפש", "בדוק", "בדיקת", "רשימת", "תן לי")

NUMBER_RE = re.compile(r"(?<![\w\d])(-\d[\d,]*(?:\.\d+)?)|(\d[\d,]*(?:\.\d+)?)")
DATE_RE = re.compile(r"(\d{1,2})[./](\d{1,2})[./](\d{4})|(\d{4})-(\d{2})-(\d{2})")
QUOTED_RE = re.compile(r"[\"']([^\"']+)[\"']")


def word_regex(phrase: str) -> re.Pattern[str]:
    return re.compile(rf"(?<![{HEB}]){PREFIX}{re.escape(phrase)}(?![{HEB}])")


SUFFIX = "(?:ו|ם|ן|ה|ים|ות|תם|תן)?"


def mention_regex(phrase: str) -> re.Pattern[str]:
    """כמו word_regex, אך סובל סיומות נטייה נפוצות ("גיל"→"גילם", "התחיל"→"התחילו")."""
    suffix = SUFFIX if len(phrase) >= 3 and not phrase.endswith(("ה", "ם", "ן", "ו")) else ""
    # בצירופים: ה' הידיעה אופציונלית לפני כל מילה מלבד הראשונה ("תאריך הסיום" = "תאריך סיום")
    words = phrase.split()
    body = re.escape(words[0]) + "".join(rf"\s+ה?{re.escape(w)}" for w in words[1:])
    return re.compile(rf"(?<![{HEB}]){PREFIX}{body}{suffix}(?![{HEB}])")


def contains_word(text: str, phrase: str) -> bool:
    return word_regex(phrase).search(text) is not None


def ends_with_word(text: str, phrases: tuple[str, ...], allow_prefix: bool = True) -> bool:
    """האם אחת המילים מופיעה בסוף הקטע (עד 2 מילים לפני האזכור).
    allow_prefix=False מונע התאמות כמו "לא" בתוך "ללא"."""
    tail = " ".join(text.split()[-2:])
    if allow_prefix:
        return any(contains_word(tail, p) for p in phrases)
    return any(re.search(rf"(?<![{HEB}]){re.escape(p)}(?![{HEB}])", tail) for p in phrases)


@dataclass
class Context:
    """איך להתייחס לשדות של ישות שמוזכרת בטקסט, ביחס לישות הראשית."""
    entity: SchemaEntity
    path_prefix: str = ""                 # "department." לקשר ManyToOne
    related: Optional[str] = None         # מפתח קשר OneToMany (→ RelatedNode)
    related_label: Optional[str] = None


@dataclass
class Mention:
    start: int
    end: int
    kind: str                 # "entity" | "field"
    text: str
    entity: Optional[SchemaEntity] = None
    field: Optional[SchemaField] = None
    owner: Optional[SchemaEntity] = None
    plural: bool = False
    value: Optional[str] = None
    # שדות חלופיים באותו מיקום (למשל "פעילים" = עובד פעיל / מחלקה פעילה) - נבחר לפי הקשר
    alternatives: list[tuple[SchemaField, SchemaEntity]] = dc_field(default_factory=list)


@dataclass
class Draft:
    conditions: list[tuple[Context, ConditionNode, bool]] = dc_field(default_factory=list)   # (ctx, node, is_status)
    explanation: list[str] = dc_field(default_factory=list)
    warnings: list[str] = dc_field(default_factory=list)
    assumptions: list[str] = dc_field(default_factory=list)
    consumed: list[tuple[int, int]] = dc_field(default_factory=list)
    negated_related: set[str] = dc_field(default_factory=set)
    related_mentioned: dict[str, Context] = dc_field(default_factory=dict)


class MockProvider(AIProvider):
    name = "mock"
    model = "hebrew-rules-v1"

    # ------------------------------------------------------------------ public

    def interpret(self, text: str, schema: SchemaContext) -> ControlSuggestion:
        original = text.strip()
        t = normalize(original)
        mentions = self._find_mentions(t, schema)
        root = self._pick_root(mentions, schema)
        if root is None:
            return self._stamp(ControlSuggestion(
                name="לא זוהו נתונים מתאימים",
                description=original,
                confidence=0.0,
                warnings=["לא זוהתה בטקסט אף ישות עסקית (למשל: " + ", ".join(e.plural_name for e in schema.entities) + ")"],
                unmatched_terms=[original],
            ))

        contexts = self._contexts(root, schema)
        draft = Draft()
        self._parse(t, mentions, root, contexts, draft)

        definition = self._build_definition(t, root, draft)
        unmatched = [m.group(0) for m in NUMBER_RE.finditer(t) if not self._is_consumed(draft, m.start())]

        if definition is None:
            draft.warnings.append("לא זוהו תנאים בטקסט. נסו לציין שדה, השוואה וערך (למשל: \"שכר גבוה מ-10,000\").")

        confidence = 0.0 if definition is None else max(0.2, 0.92 - 0.2 * len(draft.warnings) - 0.15 * len(unmatched))
        return self._stamp(ControlSuggestion(
            name=make_name(original),
            description=original,
            category="הוגדר בשפה חופשית",
            severity="high" if any(w in t for w in ("קריטי", "חמור", "דחוף")) else "medium",
            definition=definition,
            confidence=round(confidence, 2),
            explanation="\n".join(draft.explanation) or None,
            assumptions=draft.assumptions,
            warnings=draft.warnings,
            unmatched_terms=unmatched,
        ))

    def suggest(self, schema: SchemaContext, max_items: int) -> list[ControlSuggestion]:
        return [self._stamp(s) for s in suggest_controls(schema, max_items)]

    # ------------------------------------------------------------------ mentions

    def _find_mentions(self, t: str, schema: SchemaContext) -> list[Mention]:
        found: list[Mention] = []
        for entity in schema.entities:
            for phrase in {entity.name, entity.plural_name, *entity.synonyms}:
                for m in mention_regex(phrase).finditer(t):
                    found.append(Mention(m.start(), m.end(), "entity", m.group(0), entity=entity,
                                         plural=phrase == entity.plural_name or phrase.endswith(("ים", "ות"))))
            for f in entity.fields:
                for phrase in {f.name, *f.synonyms}:
                    for m in mention_regex(phrase).finditer(t):
                        found.append(Mention(m.start(), m.end(), "field", m.group(0), field=f, owner=entity))
                for allowed in f.allowed_values or []:
                    for m in word_regex(allowed).finditer(t):
                        found.append(Mention(m.start(), m.end(), "value", m.group(0), field=f, owner=entity, value=allowed))

        # הכי ארוך מנצח; אין חפיפות
        found.sort(key=lambda m: (-(m.end - m.start), m.start))
        chosen: list[Mention] = []
        for m in found:
            if all(m.end <= c.start or m.start >= c.end for c in chosen):
                chosen.append(m)
        for c in chosen:
            if c.kind in ("field", "value"):
                c.alternatives = [(m.field, m.owner) for m in found  # type: ignore[misc]
                                  if m.kind == c.kind and m.start == c.start and m.end == c.end]
        return sorted(chosen, key=lambda m: m.start)

    @staticmethod
    def _pick_root(mentions: list[Mention], schema: SchemaContext) -> Optional[SchemaEntity]:
        for m in mentions:
            if m.kind == "entity":
                return m.entity
        owners = [m.owner for m in mentions if m.kind == "field" and m.owner]
        if owners:
            return max(set(owners), key=lambda o: owners.count(o))  # type: ignore[arg-type]
        return None

    @staticmethod
    def _contexts(root: SchemaEntity, schema: SchemaContext) -> dict[str, Context]:
        """כל ישות שאפשר להגיע אליה מהישות הראשית, ואיך (נתיב / תנאי קיום).
        ישות שאפשר להגיע אליה גם בקשר "רבים" נשמרת גם תחת המפתח '<key>*'."""
        contexts: dict[str, Context] = {root.key: Context(root)}
        for rel in root.relations:
            target = schema.entity(rel.target_entity)
            if target is None or target.key in contexts:
                continue
            if rel.cardinality == "ManyToOne":
                contexts[target.key] = Context(target, path_prefix=f"{rel.key}.")
        for rel in root.relations:
            target = schema.entity(rel.target_entity)
            if target is None:
                continue
            if rel.cardinality == "OneToMany" and target.key + "*" not in contexts:
                many = Context(target, related=rel.key, related_label=rel.name)
                contexts[target.key + "*"] = many
                contexts.setdefault(target.key, many)
                for rel2 in target.relations:
                    t2 = schema.entity(rel2.target_entity)
                    if rel2.cardinality == "ManyToOne" and t2 and t2.key not in contexts:
                        contexts[t2.key] = Context(t2, path_prefix=f"{rel2.key}.", related=rel.key, related_label=rel.name)
        return contexts

    # ------------------------------------------------------------------ parsing

    def _parse(self, t: str, mentions: list[Mention], root: SchemaEntity,
               contexts: dict[str, Context], draft: Draft) -> None:
        current = contexts[root.key]
        skip_until = -1

        for i, m in enumerate(mentions):
            if m.start < skip_until:
                continue
            next_start = next((n.start for n in mentions[i + 1:]
                               if n.kind != "entity" or n.entity.key not in (current.entity.key, root.key)),  # type: ignore[union-attr]
                              len(t))
            head = t[max(0, m.start - 25):m.start]

            if m.kind == "entity":
                key = m.entity.key  # type: ignore[union-attr]
                ctx = contexts.get(key)
                # "מחלקות ללא עובדים" / ציון ברבים → קשר אחד-לרבים, אם קיים
                if key + "*" in contexts and key != root.key and (m.plural or ends_with_word(head, WITHOUT)):
                    ctx = contexts[key + "*"]
                if ctx is None:
                    draft.warnings.append(f"לא נמצא קשר בין {root.plural_name} לבין {m.entity.plural_name}")  # type: ignore[union-attr]
                    continue
                current = ctx
                if ctx.related:
                    draft.related_mentioned[ctx.related] = ctx
                    if ends_with_word(head, WITHOUT):
                        draft.negated_related.add(ctx.related)
                continue

            ctx, f = self._field_in_context(m, current, contexts, root.key)
            if f is None:
                continue
            path = ctx.path_prefix + f.key

            if m.kind == "value":
                if ctx.related and ctx.related not in draft.related_mentioned:
                    draft.related_mentioned[ctx.related] = ctx
                negated = ends_with_word(head, NEGATIONS, allow_prefix=False)
                self._add(draft, ctx, ConditionNode(field=path, operator="neq" if negated else "eq", value=ValueSpec(value=m.value)),
                          True, f"'{m.text}' → {self._label(ctx, f)} {'≠' if negated else '='} \"{m.value}\"", m)
                continue
            tail = t[m.end:next_start]

            if f.data_type == "boolean":
                value = not ends_with_word(head, NEGATIONS, allow_prefix=False)
                self._add(draft, ctx, ConditionNode(field=path, operator="eq", value=ValueSpec(value=value)), True,
                          f"'{with_previous_word(t, m)}' → {self._label(ctx, f)} = {'כן' if value else 'לא'}", m)
                continue

            near_tail = tail[:20]
            says_not_empty = any(contains_word(near_tail, p) for p in ("לא ריק", "לא ריקה", "אינו ריק", "אינה ריקה"))
            if ends_with_word(head, EMPTY_BEFORE) or (
                    not says_not_empty and any(contains_word(near_tail, p) for p in EMPTY_AFTER)):
                self._add(draft, ctx, ConditionNode(field=path, operator="isEmpty"), False,
                          f"'{m.text}' חסר → {self._label(ctx, f)} ריק / חסר", m)
                continue

            op = find_operator(tail)
            last = next((n for c, n, st in reversed(draft.conditions) if not st), None)
            if op is None and last is not None and last.operator in ("isEmpty", "isNotEmpty") and (
                    ends_with_word(head, ("או", "וגם")) or m.text.startswith("ו")):
                self._add(draft, ctx, ConditionNode(field=path, operator=last.operator), False,
                          f"'{m.text}' → {self._label(ctx, f)} {'ריק / חסר' if last.operator == 'isEmpty' else 'מלא'}", m)
                continue
            if op is None:
                if any(contains_word(tail, p) for p in NOT_EMPTY_AFTER):
                    self._add(draft, ctx, ConditionNode(field=path, operator="isNotEmpty"), False,
                              f"'{m.text}' → {self._label(ctx, f)} מלא", m)
                elif f.allowed_values is None:
                    draft.warnings.append(f"זוהה השדה \"{f.name}\" אך לא זוהתה השוואה עבורו")
                continue

            op_key, op_start, op_end = op
            after = tail[op_end:]
            abs_after = m.end + op_end

            # השוואה לשדה אחר ("מוקדם מתאריך תחילת העסקה")
            nxt = mentions[i + 1] if i + 1 < len(mentions) else None
            if (nxt is not None and nxt.kind == "field" and re.fullmatch(rf"[\s\-]*(?:{PREFIX}[{HEB}]{{0,6}}\s+)?[\s\-]*", t[abs_after:nxt.start] or "")
                    and op_key in ("lt", "lte", "gt", "gte", "eq", "neq")):
                other_ctx, other = self._field_in_context(nxt, ctx, contexts, root.key)
                if other is not None and other.data_type == f.data_type and other_ctx.related == ctx.related:
                    other_path = other_ctx.path_prefix + other.key
                    self._add(draft, ctx, ConditionNode(field=path, operator=op_key, value=ValueSpec(kind="field", field=other_path)),
                              False, f"'{t[m.start:nxt.end]}' → {self._label(ctx, f)} {op_key} {self._label(other_ctx, other)}", m)
                    draft.consumed.append((nxt.start, nxt.end))
                    skip_until = nxt.end
                    continue

            value = parse_value(after, f, op_key)
            if value is None:
                draft.warnings.append(f"לא זוהה ערך להשוואה עבור \"{f.name}\"")
                continue
            spec, v_start, v_end = value
            draft.consumed.append((abs_after + v_start, abs_after + v_end))
            self._add(draft, ctx, ConditionNode(field=path, operator=op_key, value=spec), False,
                      f"'{t[m.start:abs_after + v_end].strip()}' → {self._label(ctx, f)} {op_key} {display_value(spec)}", m)

    # ------------------------------------------------------------------ assembly

    def _build_definition(self, t: str, root: SchemaEntity, draft: Draft) -> Optional[ControlDefinition]:
        or_mode = contains_word(t, "או")
        root_items: list[tuple[ConditionNode | RelatedNode, bool]] = []
        related_groups: dict[str, list[ConditionNode]] = {}

        for ctx, node, is_status in draft.conditions:
            if ctx.related:
                related_groups.setdefault(ctx.related, []).append(node)
            else:
                root_items.append((node, is_status))

        for rel_key, ctx in draft.related_mentioned.items():
            inner = related_groups.get(rel_key, [])
            quantifier = "none" if rel_key in draft.negated_related else "any"
            node = RelatedNode(relation=rel_key, quantifier=quantifier,
                               filter=GroupNode(logic="and", children=inner) if inner else None)
            root_items.append((node, False))
            draft.explanation.append(f"{'אין' if quantifier == 'none' else 'קיימת'} רשומה ב\"{ctx.related_label}\""
                                     + (" שעומדת בתנאים שלמעלה" if inner else ""))

        if not root_items:
            return None

        status = [n for n, s in root_items if s]
        others = [n for n, s in root_items if not s]
        if or_mode and len(others) >= 2:
            children: list = [*status, GroupNode(logic="or", children=others)] if status else others
            logic = "and" if status else "or"
            if status:
                draft.assumptions.append("תנאי סטטוס (כן/לא) הוחלו על כל הבקרה, ושאר התנאים חוברו ב\"או\"")
        else:
            children = [n for n, _ in root_items]
            logic = "and"
            if len(children) > 1:
                draft.assumptions.append("כל התנאים חייבים להתקיים יחד (\"וגם\")")

        if any(n.value and n.value.kind == "today" for _, n, _ in draft.conditions):
            draft.assumptions.append("\"היום\" מחושב בכל הרצה מחדש")

        return ControlDefinition(root_entity=root.key, filter=GroupNode(logic=logic, children=children))

    # ------------------------------------------------------------------ helpers

    @staticmethod
    def _field_in_context(m: Mention, current: Context, contexts: dict[str, Context],
                          root_key: Optional[str] = None) -> tuple[Context, Optional[SchemaField]]:
        """בוחר את השדה המתאים: קודם בישות שהוזכרה אחרונה, אחר כך בישות הראשית, ולבסוף כל ישות נגישה."""
        candidates = m.alternatives or ([(m.field, m.owner)] if m.field and m.owner else [])
        if not candidates:
            return current, None
        for f, owner in candidates:
            if owner.key == current.entity.key:
                return current, f
        if root_key:
            for f, owner in candidates:
                if owner.key == root_key:
                    return contexts[root_key], f
        for f, owner in candidates:
            if owner.key in contexts:
                return contexts[owner.key], f
        return current, None

    @staticmethod
    def _label(ctx: Context, f: SchemaField) -> str:
        return f.name if not ctx.path_prefix and not ctx.related else f"{f.name} ({ctx.entity.name})"

    @staticmethod
    def _add(draft: Draft, ctx: Context, node: ConditionNode, is_status: bool, explanation: str, m: Mention) -> None:
        draft.conditions.append((ctx, node, is_status))
        draft.explanation.append(explanation)
        draft.consumed.append((m.start, m.end))

    @staticmethod
    def _is_consumed(draft: Draft, pos: int) -> bool:
        return any(s <= pos < e for s, e in draft.consumed)


# ---------------------------------------------------------------------- free functions

def normalize(text: str) -> str:
    t = text.replace("־", "-").replace("–", "-").replace("—", "-")
    t = t.replace("״", '"').replace("”", '"').replace("“", '"').replace("׳", "'")
    t = re.sub(r"ש\"ח|₪|שקלים|שקל", " ", t)
    t = re.sub(r"\s+", " ", t)
    return t.strip().rstrip(".?!")


def find_operator(tail: str) -> Optional[tuple[str, int, int]]:
    best: Optional[tuple[str, int, int]] = None
    for phrase, op in OPERATOR_PHRASES:
        m = word_regex(phrase).search(tail)
        if not m:
            continue
        candidate = (op, m.start(), m.end())
        if best is None or m.start() < best[1] or (m.start() == best[1] and m.end() > best[2]):
            best = candidate
    return best


def parse_value(after: str, f: SchemaField, op: str) -> Optional[tuple[ValueSpec, int, int]]:
    after_window = after[:60]
    if f.data_type == "date":
        if (m := re.search(rf"(?<![{HEB}]){PREFIX}היום", after_window)):
            return ValueSpec(kind="today", offset_days=0), m.start(), m.end()
        if (m := DATE_RE.search(after_window)):
            if m.group(1):
                iso = f"{m.group(3)}-{int(m.group(2)):02d}-{int(m.group(1)):02d}"
            else:
                iso = f"{m.group(4)}-{m.group(5)}-{m.group(6)}"
            return ValueSpec(value=iso), m.start(), m.end()
        return None

    if f.data_type == "number":
        if (m := NUMBER_RE.search(after_window)):
            raw = (m.group(1) or m.group(2)).replace(",", "")
            number = float(raw)
            return ValueSpec(value=int(number) if number.is_integer() else number), m.start(), m.end()
        return None

    if f.data_type == "string":
        if (m := QUOTED_RE.search(after_window)):
            return ValueSpec(value=m.group(1)), m.start(), m.end()
        for allowed in f.allowed_values or []:
            if (m := word_regex(allowed).search(after_window)):
                return ValueSpec(value=allowed), m.start(), m.end()
        if op in ("contains", "notContains", "startsWith", "endsWith"):
            m = re.search(r"(?:התו|האות|המילה|הביטוי)?\s*(\S+)", after_window)
            if m:
                return ValueSpec(value=m.group(1)), m.start(1), m.end(1)
    return None


def with_previous_word(t: str, m: "Mention") -> str:
    """הקשר להסבר: המילה שלפני האזכור + האזכור ("מחלקות פעילות", "שאינה פעילה")."""
    before = t[:m.start].split()
    return f"{before[-1]} {m.text}" if before else m.text


def display_value(spec: ValueSpec) -> str:
    if spec.kind == "today":
        return "היום"
    if spec.kind == "field":
        return spec.field or "?"
    return str(spec.value)


def make_name(text: str) -> str:
    name = normalize(text)
    for verb in LEADING_VERBS:
        if name.startswith(verb + " "):
            name = name[len(verb) + 1:]
            break
    name = name.strip()
    if len(name) > 60:
        name = name[:57].rsplit(" ", 1)[0] + "..."
    return name or "בקרה חדשה"
