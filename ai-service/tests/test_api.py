from fastapi.testclient import TestClient

from app.main import app

client = TestClient(app)


def test_health():
    r = client.get("/health")
    assert r.status_code == 200 and r.json()["provider"] == "mock"


def test_interpret_returns_camel_case_contract(schema_json):
    r = client.post("/v1/interpret", json={"text": "מחלקות פעילות ללא עובדים פעילים", "schema": schema_json})
    assert r.status_code == 200
    body = r.json()
    assert body["definition"]["rootEntity"] == "department"
    assert body["definition"]["filter"]["children"][1]["type"] == "related"
    assert "unmatchedTerms" in body and "confidence" in body


def test_interpret_validates_input(schema_json):
    assert client.post("/v1/interpret", json={"text": "", "schema": schema_json}).status_code == 422
    assert client.post("/v1/interpret", json={"text": "x" * 1001, "schema": schema_json}).status_code == 422
    assert client.post("/v1/interpret", json={"text": "עובדים"}).status_code == 422


def test_suggest_controls(schema_json):
    r = client.post("/v1/suggest-controls", json={"schema": schema_json, "max": 5})
    assert r.status_code == 200
    assert len(r.json()["suggestions"]) == 5


def test_prompt_preview_and_response_schema(schema_json):
    r = client.post("/v1/prompt-preview", json={"text": "עובדים פעילים", "schema": schema_json})
    assert r.status_code == 200 and "DATA MODEL" in r.json()["system"]
    schema = client.get("/v1/response-schema").json()
    assert "definition" in schema["properties"]
