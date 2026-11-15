"""
החוזה בין שירות ה-AI לבין צד השרת (.NET).

* SchemaContext  - מה ש-.NET שולח: מבנה הנתונים במונחים עסקיים בלבד (ללא שמות טבלאות).
* ControlDefinition - מבנה הגדרת בקרה. זהה בדיוק לפורמט שבו משתמש העורך הידני ושנשמר ב-DB.
* ControlSuggestion - מבנה התשובה המצופה מכל מודל AI.

השדות ב-JSON הם camelCase (כמו ב-.NET); בקוד Python - snake_case.
"""
from __future__ import annotations

from typing import Annotated, Any, Literal, Optional, Union

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class ApiModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)

    def to_json_dict(self) -> dict[str, Any]:
        return self.model_dump(by_alias=True, exclude_none=True, mode="json")


class StrictModel(ApiModel):
    """מודלים של הגדרת בקרה: שדות לא מוכרים נדחים (מונע "המצאות" של מודל)."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="forbid")


# --------------------------------------------------------------------------- schema context

class SchemaField(ApiModel):
    key: str
    name: str
    description: Optional[str] = None
    data_type: Literal["string", "number", "date", "boolean"]
    is_business_required: bool = False
    allowed_values: Optional[list[str]] = None
    synonyms: list[str] = []


class SchemaRelation(ApiModel):
    key: str
    name: str
    target_entity: str
    cardinality: Literal["ManyToOne", "OneToMany"]


class SchemaEntity(ApiModel):
    key: str
    name: str
    plural_name: str
    description: Optional[str] = None
    synonyms: list[str] = []
    fields: list[SchemaField]
    relations: list[SchemaRelation] = []


class SchemaOperator(ApiModel):
    key: str
    label: str
    arity: Literal["none", "single", "range", "list"]
    applies_to: list[str]
    allows_field_comparison: bool


class SchemaContext(ApiModel):
    locale: str = "he-IL"
    entities: list[SchemaEntity]
    operators: list[SchemaOperator]
    value_kinds: list[str]
    definition_schema_version: int = 1

    def entity(self, key: str) -> Optional[SchemaEntity]:
        return next((e for e in self.entities if e.key.lower() == key.lower()), None)


# --------------------------------------------------------------------------- control definition

OperatorKey = Literal[
    "eq", "neq", "gt", "gte", "lt", "lte", "between", "in", "notIn",
    "contains", "notContains", "startsWith", "endsWith", "isEmpty", "isNotEmpty",
]


class ValueSpec(StrictModel):
    kind: Literal["constant", "list", "field", "today"] = "constant"
    value: Optional[Union[bool, int, float, str]] = None
    values: Optional[list[Union[int, float, str]]] = None
    field: Optional[str] = None
    offset_days: Optional[int] = None


class ConditionNode(StrictModel):
    type: Literal["condition"] = "condition"
    id: Optional[str] = None
    field: str
    operator: OperatorKey
    value: Optional[ValueSpec] = None
    value_to: Optional[ValueSpec] = None


class RelatedNode(StrictModel):
    type: Literal["related"] = "related"
    id: Optional[str] = None
    relation: str
    quantifier: Literal["any", "none"] = "any"
    filter: Optional["GroupNode"] = None


class GroupNode(StrictModel):
    type: Literal["group"] = "group"
    id: Optional[str] = None
    logic: Literal["and", "or"] = "and"
    negate: bool = False
    children: list[Annotated[Union["GroupNode", ConditionNode, RelatedNode], Field(discriminator="type")]] = []


RelatedNode.model_rebuild()
GroupNode.model_rebuild()


class ControlDefinition(StrictModel):
    schema_version: int = 1
    root_entity: str
    filter: GroupNode


# --------------------------------------------------------------------------- responses

class ControlSuggestion(ApiModel):
    """מבנה התשובה המצופה מה-AI. ההגדרה עצמה תאומת שוב ב-.NET לפני כל שימוש."""

    name: str = Field(description="שם קצר לבקרה בעברית")
    description: Optional[str] = Field(default=None, description="תיאור עסקי של הבקרה")
    category: Optional[str] = None
    severity: Literal["low", "medium", "high"] = "medium"
    definition: Optional[ControlDefinition] = Field(
        default=None, description="תנאי החריגה: רשומות שמקיימות אותו הן חריגות. null אם לא ניתן לפענח")
    confidence: float = Field(default=0.0, ge=0, le=1)
    explanation: Optional[str] = Field(default=None, description="איך הטקסט תורגם לתנאים")
    assumptions: list[str] = []
    warnings: list[str] = []
    unmatched_terms: list[str] = []
    provider: Optional[str] = None
    model: Optional[str] = None


class InterpretRequest(ApiModel):
    text: str = Field(min_length=1, max_length=1000)
    schema_: SchemaContext = Field(alias="schema")


class SuggestRequest(ApiModel):
    schema_: SchemaContext = Field(alias="schema")
    max: int = Field(default=10, ge=1, le=50)


class SuggestResponse(ApiModel):
    suggestions: list[ControlSuggestion]


class PromptPreview(ApiModel):
    system: str
    user: str
