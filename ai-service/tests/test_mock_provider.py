import pytest

from app.contracts import ConditionNode, GroupNode, RelatedNode
from app.providers import MockProvider
from app.schema_check import find_problems

provider = MockProvider()


def flat(group: GroupNode) -> list[tuple]:
    """מייצג עץ תנאים כרשימה שטוחה וקריאה לצורך השוואה."""
    out: list[tuple] = []
    for c in group.children:
        if isinstance(c, ConditionNode):
            v = c.value
            value = None if v is None else (("field", v.field) if v.kind == "field" else ("today",) if v.kind == "today" else v.value)
            out.append((c.field, c.operator, value))
        elif isinstance(c, RelatedNode):
            out.append(("related", c.relation, c.quantifier, tuple(flat(c.filter)) if c.filter else None))
        else:
            out.append((c.logic, tuple(flat(c))))
    return out


CASES = [
    ('מצא עובדים פעילים שגילם נמוך מ־18 ומשכורתם גבוהה מ־15,000 ש"ח.', "employee", "and",
     [("isActive", "eq", True), ("age", "lt", 18), ("monthlySalary", "gt", 15000)]),
    ("איתור עובדים שתאריך סיום העסקתם מוקדם מתאריך תחילת העסקתם", "employee", "and",
     [("endDate", "lt", ("field", "startDate"))]),
    ("איתור עובדים פעילים המשויכים למחלקה שאינה פעילה", "employee", "and",
     [("isActive", "eq", True), ("department.isActive", "eq", False)]),
    ('עובדים פעילים שחסרה להם תעודת זהות או דוא"ל', "employee", "and",
     [("isActive", "eq", True), ("or", (("idNumber", "isEmpty", None), ("email", "isEmpty", None)))]),
    ("מחלקות פעילות ללא עובדים פעילים", "department", "and",
     [("isActive", "eq", True), ("related", "employees", "none", (("isActive", "eq", True),))]),
    ("עובדים פעילים המשובצים לפרויקט שהסתיים", "employee", "and",
     [("isActive", "eq", True), ("related", "assignments", "any", (("project.status", "eq", "הסתיים"),))]),
    ("עובדים במשרה חלקית ששכרם מעל 20000", "employee", "and",
     [("employmentType", "eq", "משרה חלקית"), ("monthlySalary", "gt", 20000)]),
    ("עובדים שהתחילו לעבוד אחרי 01/01/2025", "employee", "and",
     [("startDate", "gt", "2025-01-01")]),
    ("שיבוצים שאחוז השיבוץ בהם גבוה מ-100", "assignment", "and",
     [("allocationPercent", "gt", 100)]),
    ("עובדים שאינם פעילים וללא תאריך סיום", "employee", "and",
     [("isActive", "eq", False), ("endDate", "isEmpty", None)]),
    ("עובדים פעילים שתאריך הסיום שלהם לפני היום", "employee", "and",
     [("isActive", "eq", True), ("endDate", "lt", ("today",))]),
]


@pytest.mark.parametrize("text,root,logic,expected", CASES, ids=[c[0][:30] for c in CASES])
def test_interprets_hebrew_requests(schema, text, root, logic, expected):
    s = provider.interpret(text, schema)

    assert s.definition is not None, s.warnings
    assert s.definition.root_entity == root
    assert s.definition.filter.logic == logic
    assert flat(s.definition.filter) == expected
    assert find_problems(s.definition, schema) == []
    assert s.confidence > 0.8
    assert s.provider == "mock"


def test_unknown_entity_returns_no_definition_with_warning(schema):
    s = provider.interpret("מצא את כל הלקוחות עם חוב", schema)
    assert s.definition is None
    assert s.confidence == 0
    assert s.warnings


def test_field_without_comparison_is_reported(schema):
    s = provider.interpret("עובדים ששכרם", schema)
    assert s.definition is None
    assert any("שכר" in w for w in s.warnings)


def test_name_strips_leading_verb(schema):
    s = provider.interpret("מצא עובדים שגילם מעל 70", schema)
    assert s.name == "עובדים שגילם מעל 70"


def test_vocabulary_comes_from_schema_not_code(schema_json):
    """הוספת שדה חדש למילון הנתונים מספיקה כדי שהמפענח יכיר אותו - ללא שינוי קוד."""
    from app.contracts import SchemaContext
    data = {**schema_json, "entities": [dict(e) for e in schema_json["entities"]]}
    employee = next(e for e in data["entities"] if e["key"] == "employee")
    employee["fields"] = employee["fields"] + [{
        "key": "bonus", "name": "בונוס", "dataType": "number", "isBusinessRequired": False, "synonyms": ["בונוס", "מענק"]}]
    s = provider.interpret("עובדים שקיבלו מענק גבוה מ-5000", SchemaContext.model_validate(data))
    assert s.definition is not None
    assert flat(s.definition.filter) == [("bonus", "gt", 5000)]


def test_proactive_suggestions_are_valid_and_generic(schema):
    suggestions = provider.suggest(schema, 50)
    assert len(suggestions) >= 8
    for s in suggestions:
        assert s.definition is not None
        assert find_problems(s.definition, schema) == [], s.name
    names = " | ".join(s.name for s in suggestions)
    assert "תאריך סיום העסקה לפני תאריך תחילת העסקה" in names
    assert "אחוז שיבוץ מחוץ לטווח" in names
    assert provider.suggest(schema, 3).__len__() == 3
