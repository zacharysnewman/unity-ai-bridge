---
name: install-unity-ai-bridge
description: Install Unity AI Bridge Claude Code integration into the Unity project root.
---

Complete the Unity AI Bridge Claude Code setup. Run this after using **Unity AI Bridge → Setup Claude Code Integration** from the Unity menu, which copies tools and skills into the project. This skill handles the remaining step: writing the documentation into CLAUDE.md.

Steps:

1. Locate the Unity project root — the nearest ancestor directory that contains an `Assets/` folder.
   Search upward from the current working directory up to 4 levels. If not found, ask the user for the path.

2. Verify setup prerequisites:
   - Check that `.claude/unity-ai-bridge.md` exists at the project root.
   - Check that `Tools/` contains the expected scripts: `query-scene`, `query-logs`, `get-selected-entities`, `select-entities`, `get-scene-path`, `get-camera`, `get-visible-entities`, `patch-entities`, `create-entities`, `delete-entities`.
   - If either check fails, tell the user to run **Unity AI Bridge → Setup Claude Code Integration** from the Unity menu first, then re-run this skill.

3. Handle `<project-root>/CLAUDE.md` using `.claude/unity-ai-bridge.md` as the source:
   - The Unity AI Bridge section is delimited by these exact sentinel lines:
     ```
     <!-- BEGIN: Unity AI Bridge -->
     ```
     and
     ```
     <!-- END: Unity AI Bridge -->
     ```
   - Wrap the full contents of `.claude/unity-ai-bridge.md` between the two sentinels to form the managed block:
     ```
     <!-- BEGIN: Unity AI Bridge -->
     <unity-ai-bridge.md contents>
     <!-- END: Unity AI Bridge -->
     ```
   - If `CLAUDE.md` does not exist: write the managed block as the new file.
   - If it exists and already contains both sentinels: replace everything between (and including) the BEGIN and END lines with the current managed block. All content outside the sentinels is preserved exactly.
   - If it exists but lacks the sentinels: append a blank line followed by the managed block.
   - Never modify any content outside the sentinel lines.

4. Report exactly what was created or updated, and remind the user to restart Claude Code from the Unity project root for the changes to take effect.
