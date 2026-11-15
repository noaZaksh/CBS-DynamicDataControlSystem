import json
from pathlib import Path

import pytest

from app.contracts import SchemaContext

FIXTURES = Path(__file__).resolve().parent.parent / "fixtures"


@pytest.fixture(scope="session")
def schema_json() -> dict:
    # הקובץ נוצר מהמטא-דאטה האמיתי (database/05_metadata_seed.sql) באותו פורמט ש-.NET שולח
    return json.loads((FIXTURES / "schema-context.json").read_text(encoding="utf-8"))


@pytest.fixture(scope="session")
def schema(schema_json) -> SchemaContext:
    return SchemaContext.model_validate(schema_json)
