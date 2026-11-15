"""בחירת ספק ה-AI לפי הגדרות. זו הנקודה היחידה שמשתנה כשמחליפים מודל."""
from __future__ import annotations

from .config import Settings
from .providers import AIProvider, LLMProvider, MockProvider
from .providers.llm_provider import NotConfiguredClient, RecordedLLMClient


def create_provider(settings: Settings) -> AIProvider:
    provider = settings.ai_provider.lower()
    if provider == "mock":
        return MockProvider()
    if provider == "llm":
        client_name = settings.llm_client.lower()
        if client_name == "recorded":
            return LLMProvider(RecordedLLMClient(settings.llm_recordings_path))
        return LLMProvider(NotConfiguredClient(client_name))
    raise ValueError(f"Unknown AI_PROVIDER '{settings.ai_provider}'")
