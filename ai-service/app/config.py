"""הגדרות השירות - ממשתני סביבה (AI_PROVIDER, LLM_CLIENT ...)."""
from pathlib import Path

from pydantic_settings import BaseSettings, SettingsConfigDict

BASE_DIR = Path(__file__).resolve().parent.parent


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="", env_file=".env", extra="ignore")

    # mock = מפענח עברית מבוסס-כללים | llm = צנרת מודל שפה (עם LLM_CLIENT)
    ai_provider: str = "mock"
    # recorded = Mock שמחזיר תשובות מוקלטות | openai / anthropic / azure = נקודות הרחבה
    llm_client: str = "recorded"
    llm_recordings_path: Path = BASE_DIR / "fixtures" / "llm_recordings.json"
    log_level: str = "INFO"


settings = Settings()
