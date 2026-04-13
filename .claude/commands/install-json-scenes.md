Install JSON Scenes for Unity Claude Code integration into the Unity project root.

Run this skill from within the package directory. It copies skills and CLAUDE.md
into the Unity project root so Claude Code picks them up when opened from there.

Steps:

1. Locate the Unity project root — the nearest ancestor directory that contains an `Assets/` folder.
   Search upward from the current working directory. If not found within 4 levels, ask the user for the path.

2. Create `<project-root>/.claude/commands/` if it does not already exist.

3. Copy each skill file from the package into the project:
   - `.claude/commands/scene-overview.md` → `<project-root>/.claude/commands/scene-overview.md`
   - `.claude/commands/new-entity.md`     → `<project-root>/.claude/commands/new-entity.md`
   - `.claude/commands/install-json-scenes.md` → `<project-root>/.claude/commands/install-json-scenes.md`
   Overwrite any existing copies so the project always has the current version.

4. Handle `<project-root>/CLAUDE.md`:
   - If it does not exist: copy `CLAUDE.md` from the package directly.
   - If it already exists and already contains a `# JSON Scenes for Unity` heading: skip (already installed).
   - If it already exists but lacks that heading: append a blank line followed by the full contents of the package `CLAUDE.md`.

5. Report exactly what was created or updated, and remind the user to restart Claude Code
   from the Unity project root for the changes to take effect.
