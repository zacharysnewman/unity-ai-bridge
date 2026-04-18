---
name: install-unity-ai-bridge
description: Install Unity AI Bridge Claude Code integration into the Unity project root.
---

Install Unity AI Bridge Claude Code integration into the Unity project root.

Run this skill from within the package directory. It copies skills, CLAUDE.md, and CLI tools
into the Unity project root so Claude Code and the terminal pick them up when opened from there.

Steps:

1. Locate the Unity project root — the nearest ancestor directory that contains an `Assets/` folder.
   Search upward from the current working directory. If not found within 4 levels, ask the user for the path.

2. Create `<project-root>/.claude/skills/` if it does not already exist.

3. Copy each skill directory from the package into the project:
   - `.claude/skills/scene-overview/`         → `<project-root>/.claude/skills/scene-overview/`
   - `.claude/skills/new-entity/`             → `<project-root>/.claude/skills/new-entity/`
   - `.claude/skills/install-unity-ai-bridge/` → `<project-root>/.claude/skills/install-unity-ai-bridge/`
   Copy the full directory contents. Overwrite any existing copies so the project always has the current version.

4. Handle `<project-root>/CLAUDE.md`:
   - Before inserting, strip any content between (and including) these markers from the package CLAUDE.md:
     ```
     <!-- PACKAGE-ONLY-BEGIN -->
     ```
     and
     ```
     <!-- PACKAGE-ONLY-END -->
     ```
     These blocks are for package-development notes only and must not appear in installed projects.
   - The Unity AI Bridge section is delimited by these exact sentinel lines:
     ```
     <!-- BEGIN: Unity AI Bridge -->
     ```
     and
     ```
     <!-- END: Unity AI Bridge -->
     ```
   - Read the package `CLAUDE.md` — wrap its full contents between these two sentinels to form the "managed block":
     ```
     <!-- BEGIN: Unity AI Bridge -->
     <package CLAUDE.md contents>
     <!-- END: Unity AI Bridge -->
     ```
   - If `<project-root>/CLAUDE.md` does not exist: write the managed block as the new file.
   - If it exists and already contains both sentinels:
     - Replace everything between (and including) the BEGIN and END sentinel lines with the current managed block.
     - All content outside the sentinels is preserved exactly.
   - If it exists but lacks the sentinels: append a blank line followed by the managed block.
   - Never modify any content outside the sentinel lines.

5. Install CLI tools:
   - Create `<project-root>/Tools/` if it does not already exist.
   - Copy each script from the package `Tools/` directory:
     - `Tools/get-selection`  → `<project-root>/Tools/get-selection`
     - `Tools/select-objects` → `<project-root>/Tools/select-objects`
     - `Tools/query-scene`    → `<project-root>/Tools/query-scene`
     - `Tools/query-logs`     → `<project-root>/Tools/query-logs`
   - Make both files executable (`chmod +x`).
   - Overwrite any existing copies so the project always has the current version.

6. Report exactly what was created or updated, and remind the user to restart Claude Code
   from the Unity project root for the changes to take effect.
