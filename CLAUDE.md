# CLAUDE.md — pyRevit Script Template

This file provides guidance to Claude Code when working on pyRevit scripts.
Copy this file into each new pyRevit project and fill in the placeholder sections.

---

## Project Overview

<!-- FILL IN: What does this tool do? Is it read-only or does it write to the model? -->

- **Tool name:** `<Tool Name>`
- **Purpose:** `<Brief description>`
- **Model writes:** Yes / No
- **Target:** Revit 2020–2026+ / pyRevit / IronPython 2

---

## Folder Structure

pyRevit uses a strict folder hierarchy. The script is always named `script.py`.

```
MyTools.extension/
  My Tab.tab/
    My Panel.panel/
      My Button.pushbutton/
        script.py
        icon.png        (optional — 16x16 or 32x32 PNG)
```

Install by copying the `.extension` folder into:
```
%APPDATA%\pyRevit-Master\extensions\
```
Or use a `.bat` installer that does this automatically.

---

## Installing & Testing

1. **Reload scripts:** pyRevit ribbon → **Reload Scripts** (picks up changes without restarting Revit)
2. **Debug logging:** Ctrl+Click the button to enable `script.get_logger()` output in the pyRevit output panel
3. **Distribute:** Use a `.bat` installer that copies the `.extension` folder to `%APPDATA%\pyRevit-Master\extensions\`

---

## Module Header Convention

Every script begins with:

```python
# -*- coding: utf-8 -*-
"""
<Tool Name>

<Description of what the tool does.>

Author : <Author Name>
Target : Revit 2020 - 2026+ / pyRevit
"""
```

---

## Import Conventions

**Style A — preferred for simple scripts:**
```python
from pyrevit import revit, DB, script, forms
```

**Style B — for scripts importing many Revit API classes by name:**
```python
import clr
clr.AddReference("RevitAPI")
clr.AddReference("RevitAPIUI")
from Autodesk.Revit.DB import (
    FilteredElementCollector, ViewSheet, Transaction, TransactionGroup,
    # ... add others as needed
)
from pyrevit import revit, script, forms
```

**Optional dependencies** — use lazy imports with a fallback:
```python
try:
    from openpyxl import Workbook
    HAS_OPENPYXL = True
except ImportError:
    HAS_OPENPYXL = False
    output.print_md("**openpyxl not found — falling back to CSV export.**")
```

---

## Module-Level Globals

Always declare these before any functions:

```python
doc    = revit.doc
output = script.get_output()
output.set_title("My Tool Name")
```

All functions receive data as arguments. `doc` is accessed as a trusted module-level global — do not pass it as a function parameter.

---

## Key API Patterns

### Element Collection

```python
# Standard idiom:
elements = (FilteredElementCollector(doc)
            .OfClass(ViewSheet)
            .WhereElementIsNotElementType()
            .ToElements())

# Scoped to a specific view:
elements = FilteredElementCollector(doc, view.Id).OfCategory(...).ToElements()
```

### Transactions

Always use `TransactionGroup` + an inner `Transaction`. Never use `with Transaction(...)`.

```python
tg = TransactionGroup(doc, "My Operation Group")
tg.Start()
try:
    t = Transaction(doc, "My Transaction")
    t.Start()
    # ... all model writes go here ...
    t.Commit()
    tg.Assimilate()           # fold inner transaction into group on success
except Exception as e:
    tg.RollBack()             # clean rollback on any failure
    forms.alert(str(e), title="Error")
```

### Parameter Reading

Always read defensively — parameter availability varies across project templates and Revit versions.

```python
# By name (flexible):
p = element.LookupParameter("Parameter Name")
if p and p.HasValue:
    value = p.AsString()          # or .AsDouble() / .AsInteger() / .AsValueString()

# By built-in enum (robust):
p = element.get_Parameter(BuiltInParameter.SHEET_WIDTH)
```

Wrap parameter reads in `try/except` when iterating across many element types.

### Unit Conversion

Revit's internal unit is **feet**. Define all layout/geometry constants in mm and convert at draw time.

```python
def mm_to_feet(mm):
    return mm / 304.8

# Usage:
XYZ(mm_to_feet(x_mm), mm_to_feet(y_mm), 0)
```

### Forms / User Dialogs

```python
# Blocking alert:
forms.alert("Something went wrong.", title="Error")

# Multi-choice picker — returns chosen string or None if cancelled:
choice = forms.CommandSwitchWindow.show(["Option A", "Option B"], message="Choose:")
if not choice:
    return   # user cancelled

# Save file dialog — returns path or None if cancelled:
save_path = forms.save_file(file_ext="xlsx", default_name="output", title="Save As")
if not save_path:
    return   # user cancelled
```

Always check the return value for `None` before proceeding.

### Output Panel

```python
output.print_md("## Section Heading")
output.print_md("| Sheet | Rev |\n|---|---|\n| A-001 | C |")   # Tables work
output.linkify(element.Id)    # Clickable link to element in Revit
```

Use `output.print_md()` for all progress and summary messages — never `print()`.

### Revision API

```python
Revision.GetAllRevisionIds(doc)            # All project revisions
sheet.GetAllRevisionIds()                  # Revisions issued on a specific sheet
sheet.GetRevisionNumberOnSheet(rid)        # Per-sheet number (differs in "Per Sheet" mode)

# Filter to sheets that appear in the drawing list:
p = sheet.get_Parameter(BuiltInParameter.SHEET_SCHEDULED)
if p and p.AsInteger() == 1:
    ...
```

### Alternate Click Behaviours

pyRevit injects these as script globals:

```python
if __shiftclick__:
    # Shift+Click behaviour
if __ctrlclick__:
    # Ctrl+Click behaviour
```

---

## Architecture Conventions

- **Separate data collection from model writes.** Functions that read Revit data make no model changes. All writes happen inside a single transaction block.
- **Idempotent re-runs.** Before creating a view or element, delete any existing one with the same name — so re-running the script is always safe.
- **Keep `forms` calls in `main()`.** Helper and data functions should not open dialogs. All user interaction stays in `main()`.
- **Graceful optional dependencies.** Use `try/except ImportError` and fall back to a simpler path (e.g. xlsx → csv) with a user notification.

---

## Entry Point

```python
def main():
    # All orchestration logic here
    pass

if __name__ == "__main__":
    main()
```

---

## Do / Don't

**Do:**
- Wrap all model writes in `TransactionGroup` + `Transaction` with `tg.Assimilate()` on success and `tg.RollBack()` on failure
- Use `output.print_md()` for all progress and summary output
- Use `forms.alert()` for user-facing errors and warnings
- Check `p and p.HasValue` before reading any parameter value
- Define geometry constants in mm and convert via `mm_to_feet()` at draw time
- Delete existing elements by name before recreating them, so re-runs are safe
- Add `# -*- coding: utf-8 -*-` and a module docstring to every script
- Handle `__shiftclick__` / `__ctrlclick__` for alternate button behaviours

**Don't:**
- Modify the Revit model outside a Transaction — Revit will throw an exception
- Use Python's `print()` — output will not appear in the pyRevit panel
- Use `with Transaction(...)` — pyRevit scripts use manual `Start()` / `Commit()` / `RollBack()`
- Hardcode Revit internal units — always convert from mm using `mm_to_feet()`
- Call `forms` dialog functions inside helper or data functions — keep them in `main()`
- Assume a parameter exists on every element — always use `try/except` and check `HasValue`
