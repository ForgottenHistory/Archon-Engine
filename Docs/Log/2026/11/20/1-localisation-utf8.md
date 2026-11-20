# Localisation UTF-8 Fix
**Date**: 2026-11-20
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Fix garbled non-ASCII letters in localised names (`Thüringen` shown as `ThÃ¼ringen`)

**Success Criteria:**
- Localised strings keep their UTF-8 characters from `.yml` to screen

---

## Problems Encountered & Solutions

### Problem 1: Non-ASCII letters doubled into Latin-1 garbage
**Symptom:** `Thüringen` → `ThÃ¼ringen`, `Württemberg` → `WÃ¼rttemberg`. The `.yml` files were
correct UTF-8; ASCII text was fine, so English-only test data never showed it.

**Root Cause:** `FixedString*Bytes` stores UTF-8. Parsers walked the UTF-8 byte buffer with

```csharp
result.Append((char)data[i]);   // WRONG for any byte >= 0x80
```

Each byte became its own char (effectively Latin-1), and `Append(char)` re-encoded that char as
UTF-8, so `ü` (`C3 BC`) turned into `Ã` + `¼` (4 bytes).

**Solution:**
```csharp
result.AppendRawByte(data[i]);  // Unity.Collections FixedStringMethods
```

**Files Changed:**
- `Scripts/Core/Localization/YAMLParser.cs`: ExtractString, ExtractQuotedString
- `Scripts/Core/Localization/ColoredTextMarkup.cs`: segment text, BufferToFixedString
- `Scripts/Core/Localization/StringReplacementSystem.cs`: ExtractKey, ExtractConditionalText, BufferToFixedString

**Why This Works:** `FixedString` length and indexer are in bytes, so the bytes go in untouched.
Code copying `(byte)str[i]` back into byte buffers was already correct; only byte → char appends
were wrong.

**Pattern for Future:** When parsing byte buffers into `FixedString`, use `AppendRawByte` (or
`Append(byte*, int)` for a run). Use `Append(char)` only for real chars, never for a byte cast
to char.

---

## Quick Reference for Future Claude

**Gotchas for Next Session:**
- Strings are truncated at 511 bytes, which can cut a multi-byte character in half. Not hit in
  practice yet (names are short).
- `ColoredTextMarkup.ExtractColorName` still builds a string from `(char)` bytes; fine because color
  names are ASCII.
