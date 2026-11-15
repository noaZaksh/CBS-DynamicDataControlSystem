"""ממשק אחיד לכל ספק AI. מנוע הבקרות (.NET) לא מכיר אף מימוש ספציפי."""
from __future__ import annotations

from abc import ABC, abstractmethod

from ..contracts import ControlSuggestion, SchemaContext


class AIProvider(ABC):
    name: str = "base"
    model: str | None = None

    @abstractmethod
    def interpret(self, text: str, schema: SchemaContext) -> ControlSuggestion:
        """תרגום תיאור חופשי להצעת בקרה."""

    @abstractmethod
    def suggest(self, schema: SchemaContext, max_items: int) -> list[ControlSuggestion]:
        """הצעת בקרות באופן יזום על סמך מבנה הנתונים."""

    def _stamp(self, suggestion: ControlSuggestion) -> ControlSuggestion:
        suggestion.provider = self.name
        suggestion.model = self.model
        return suggestion


class ProviderError(Exception):
    """הספק לא הצליח להפיק תשובה תקינה."""
