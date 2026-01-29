# Copilot Instructions

## General Guidelines
- Use partial properties when creating or updating properties in the DivisiBill codebase.

## Code Style
- Follow specific formatting rules.
- Adhere to naming conventions.
- For properties that call OnPropertyChanged, prefer auto-properties with [NotifyPropertyChangedFor] and OnXChanged methods instead of manual backing fields.