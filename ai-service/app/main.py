"""
שירות AI להגדרת בקרות בשפה חופשית.

נקודות קצה:
  POST /v1/interpret         טקסט חופשי + הקשר סכמה → הצעת בקרה (ControlSuggestion)
  POST /v1/suggest-controls  הקשר סכמה → הצעות בקרה יזומות
  POST /v1/prompt-preview    הצגת ה-Prompt שהיה נשלח למודל שפה (לשקיפות/דיבאג)
  GET  /v1/response-schema   JSON Schema של התשובה המצופה מה-AI
  GET  /health
"""
from __future__ import annotations

import logging

from fastapi import Depends, FastAPI, HTTPException

from .config import settings
from .contracts import (
    ControlSuggestion, InterpretRequest, PromptPreview, SuggestRequest, SuggestResponse,
)
from .factory import create_provider
from .prompting import build_interpret_prompt, response_json_schema
from .providers import AIProvider, ProviderError

logging.basicConfig(level=settings.log_level)
log = logging.getLogger("ai-service")

app = FastAPI(title="Data Controls AI Service", version="1.0.0")
_provider = create_provider(settings)


def get_provider() -> AIProvider:
    return _provider


@app.get("/health")
def health(provider: AIProvider = Depends(get_provider)) -> dict:
    return {"status": "ok", "provider": provider.name, "model": provider.model}


@app.post("/v1/interpret", response_model=ControlSuggestion, response_model_by_alias=True, response_model_exclude_none=True)
def interpret(request: InterpretRequest, provider: AIProvider = Depends(get_provider)) -> ControlSuggestion:
    try:
        return provider.interpret(request.text, request.schema_)
    except ProviderError as ex:
        log.error("Provider failed: %s", ex)
        raise HTTPException(status_code=502, detail=str(ex)) from ex


@app.post("/v1/suggest-controls", response_model=SuggestResponse, response_model_by_alias=True, response_model_exclude_none=True)
def suggest(request: SuggestRequest, provider: AIProvider = Depends(get_provider)) -> SuggestResponse:
    try:
        return SuggestResponse(suggestions=provider.suggest(request.schema_, request.max))
    except ProviderError as ex:
        log.error("Provider failed: %s", ex)
        raise HTTPException(status_code=502, detail=str(ex)) from ex


@app.post("/v1/prompt-preview", response_model=PromptPreview)
def prompt_preview(request: InterpretRequest) -> PromptPreview:
    return build_interpret_prompt(request.text, request.schema_)


@app.get("/v1/response-schema")
def response_schema() -> dict:
    return response_json_schema()
