from app.config import Settings
from app.factory import create_provider
from app.prompting import build_interpret_prompt, describe_schema
from app.providers.llm_provider import LLMProvider, NotConfiguredClient, RecordedLLMClient, extract_json
from app.config import BASE_DIR

RECORDINGS = BASE_DIR / "fixtures" / "llm_recordings.json"


def test_extracts_json_from_markdown_fence():
    assert extract_json('```json\n{"a": 1}\n```') == {"a": 1}
    assert extract_json('Sure! here it is: {"a": 2} hope it helps') == {"a": 2}


def test_prompt_contains_business_schema_but_no_technical_names(schema):
    prompt = build_interpret_prompt("מצא עובדים", schema)
    assert "field `monthlySalary` (number) שכר חודשי" in prompt.system
    assert "relation `department` → `department` (ManyToOne)" in prompt.system
    assert "Never output SQL" in prompt.system
    # ה-AI לא מקבל שמות טבלאות/עמודות אמיתיים
    assert "vw_EmployeesEnriched" not in prompt.system
    assert "MonthlySalary" not in describe_schema(schema)


def test_recorded_llm_pipeline_parses_fenced_answer(schema):
    provider = LLMProvider(RecordedLLMClient(RECORDINGS))
    s = provider.interpret('מצא עובדים פעילים שגילם נמוך מ-18 ומשכורתם גבוהה מ-15,000 ש"ח', schema)
    assert s.definition is not None
    assert s.definition.root_entity == "employee"
    assert len(s.definition.filter.children) == 3
    assert s.provider == "llm" and s.model == "recorded-mock"


def test_hallucinated_field_triggers_repair_round(schema):
    client = RecordedLLMClient(RECORDINGS)
    provider = LLMProvider(client)
    s = provider.interpret("מצא עובדים עם בונוס גבוה", schema)

    assert len(client.calls) == 2                       # תשובה ראשונה נדחתה → בקשת תיקון
    assert "Unknown field 'bonus'" in client.calls[1][1][-1]["content"]
    assert s.definition is None and "בונוס" in s.unmatched_terms


def test_factory_switches_provider_by_configuration():
    assert create_provider(Settings(ai_provider="mock")).name == "mock"
    llm = create_provider(Settings(ai_provider="llm", llm_client="recorded"))
    assert isinstance(llm, LLMProvider)
    assert isinstance(create_provider(Settings(ai_provider="llm", llm_client="openai")).client, NotConfiguredClient)
