"""
בדיקה סמנטית קלה של הגדרה מול הסכמה (שדות/קשרים/אופרטורים קיימים).

ב-.NET מתבצעת הוולידציה המחייבת; כאן היא משמשת כדי לתת למודל AI משוב
ולבקש ממנו תיקון ("repair loop") לפני שמחזירים תשובה.
"""
from __future__ import annotations

from .contracts import ConditionNode, ControlDefinition, GroupNode, RelatedNode, SchemaContext, SchemaEntity


def find_problems(definition: ControlDefinition, schema: SchemaContext) -> list[str]:
    root = schema.entity(definition.root_entity)
    if root is None:
        return [f"Unknown root entity '{definition.root_entity}'"]
    problems: list[str] = []
    _check_group(definition.filter, root, schema, problems)
    return problems


def _resolve(entity: SchemaEntity, path: str, schema: SchemaContext) -> tuple[SchemaEntity | None, str | None]:
    parts = path.split(".")
    current = entity
    for rel_key in parts[:-1]:
        rel = next((r for r in current.relations if r.key == rel_key), None)
        if rel is None or rel.cardinality != "ManyToOne":
            return None, f"'{rel_key}' is not a many-to-one relation of '{current.key}'"
        current = schema.entity(rel.target_entity)
        if current is None:
            return None, f"Unknown entity '{rel.target_entity}'"
    if not any(f.key == parts[-1] for f in current.fields):
        return None, f"Unknown field '{parts[-1]}' on '{current.key}'"
    return current, None


def _check_group(group: GroupNode, entity: SchemaEntity, schema: SchemaContext, problems: list[str]) -> None:
    if not group.children:
        problems.append("Empty condition group")
    for child in group.children:
        if isinstance(child, GroupNode):
            _check_group(child, entity, schema, problems)
        elif isinstance(child, ConditionNode):
            _, err = _resolve(entity, child.field, schema)
            if err:
                problems.append(err)
            if child.value is not None and child.value.kind == "field" and child.value.field:
                _, err = _resolve(entity, child.value.field, schema)
                if err:
                    problems.append(err)
        elif isinstance(child, RelatedNode):
            rel = next((r for r in entity.relations if r.key == child.relation), None)
            if rel is None:
                problems.append(f"Unknown relation '{child.relation}' on '{entity.key}'")
                continue
            target = schema.entity(rel.target_entity)
            if target is not None and child.filter is not None:
                _check_group(child.filter, target, schema, problems)
