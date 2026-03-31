/**
 * MCP Server for json-scenes-for-unity
 *
 * Provides three tools:
 *   create_entity   — generates a UUID, writes the entity JSON file, returns uuid + filePath
 *   force_reload    — writes a command file to Commands/ to trigger a reload inside Unity
 *   validate_scene  — writes a validate command, waits for Unity to write the result, returns it
 *
 * Communication with Unity is entirely file-based (no network connection).
 * See MCP.md for the full specification.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { randomUUID } from "crypto";
import { writeFileSync, readFileSync, existsSync, mkdirSync } from "fs";
import { join, resolve } from "path";

// ─── Schema helpers ────────────────────────────────────────────────────────────

const Vec3Schema = z
  .array(z.number())
  .length(3)
  .describe("[x, y, z] values");

const TransformSchema = z
  .object({
    pos: Vec3Schema.optional().describe("Local position [x, y, z] in metres. Defaults to [0, 0, 0]."),
    rot: Vec3Schema.optional().describe("Local Euler angles [x, y, z] in degrees. Defaults to [0, 0, 0]."),
    scl: Vec3Schema.optional().describe("Local scale [x, y, z]. Defaults to [1, 1, 1]."),
  })
  .optional();

// ─── Server setup ──────────────────────────────────────────────────────────────

const server = new McpServer({
  name: "json-scenes-for-unity",
  version: "1.0.0",
});

// ─── Tool: create_entity ──────────────────────────────────────────────────────

server.tool(
  "create_entity",
  "Creates a new entity JSON file with a generated UUID and returns the UUID and file path to the AI before any other files are written. Call this tool to get a UUID for a new entity — never fabricate UUID strings.",
  {
    scenePath: z.string().describe('Project-relative path to the scene directory, e.g. "Assets/SceneData/Level_01".'),
    name: z.string().describe("Display name for the entity (shown in the Unity hierarchy)."),
    prefabPath: z.string().describe('Project-relative prefab path, e.g. "Assets/Prefabs/Door.prefab", or a primitive identifier like "primitive/Cube".'),
    parentUuid: z.string().uuid().optional().describe("UUID of the parent entity, if any."),
    transform: TransformSchema,
    customData: z
      .array(z.record(z.unknown()))
      .optional()
      .describe('Initial component data array. Each entry must have a "type" field with the fully-qualified C# class name.'),
  },
  async ({ scenePath, name, prefabPath, parentUuid, transform, customData }) => {
    const uuid = randomUUID();

    const entitiesDir = join(scenePath, "Entities");
    ensureDir(entitiesDir);

    const entity = {
      uuid,
      name,
      prefabPath,
      ...(parentUuid ? { parentUuid } : {}),
      transform: {
        pos: transform?.pos ?? [0, 0, 0],
        rot: transform?.rot ?? [0, 0, 0],
        scl: transform?.scl ?? [1, 1, 1],
      },
      ...(customData && customData.length > 0 ? { customData } : {}),
    };

    const filePath = join(entitiesDir, `${uuid}.json`);
    writeFileSync(filePath, JSON.stringify(entity, null, 2), "utf8");

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify({ uuid, filePath: filePath.replace(/\\/g, "/") }, null, 2),
        },
      ],
    };
  }
);

// ─── Tool: force_reload ───────────────────────────────────────────────────────

server.tool(
  "force_reload",
  "Forces the Unity package to re-read one or all entity files from disk. Use when FileSystemWatcher may have missed a change (known issue on macOS), or to confirm Unity has processed a batch of writes.",
  {
    scenePath: z.string().describe('Project-relative path to the scene directory, e.g. "Assets/SceneData/Level_01".'),
    targetUuid: z
      .string()
      .uuid()
      .optional()
      .describe("UUID of a single entity to reload. Omit to reload the entire scene."),
  },
  async ({ scenePath, targetUuid }) => {
    const commandsDir = join(scenePath, "Commands");
    ensureDir(commandsDir);

    const command = {
      command: "force_reload",
      ...(targetUuid ? { targetUuid } : {}),
    };

    const commandPath = join(commandsDir, `force_reload_${Date.now()}.json`);
    writeFileSync(commandPath, JSON.stringify(command, null, 2), "utf8");

    return {
      content: [
        {
          type: "text",
          text: targetUuid
            ? `force_reload command written for entity ${targetUuid}.`
            : "force_reload command written for the entire scene.",
        },
      ],
    };
  }
);

// ─── Tool: validate_scene ─────────────────────────────────────────────────────

server.tool(
  "validate_scene",
  "Triggers the Unity-side scene validator. Checks that all entity files can be fully loaded: prefab paths resolve, parent UUIDs exist, and component types are found via reflection. Returns the validation result.",
  {
    scenePath: z.string().describe('Project-relative path to the scene directory, e.g. "Assets/SceneData/Level_01".'),
  },
  async ({ scenePath }) => {
    const commandsDir = join(scenePath, "Commands");
    ensureDir(commandsDir);

    const resultPath = join(commandsDir, "validate_result.json");

    // Remove any stale result
    if (existsSync(resultPath)) {
      try { writeFileSync(resultPath, "", "utf8"); } catch (_) {}
    }

    const command = { command: "validate_scene" };
    const commandPath = join(commandsDir, `validate_${Date.now()}.json`);
    writeFileSync(commandPath, JSON.stringify(command, null, 2), "utf8");

    // Poll for result (Unity writes validate_result.json after executing)
    const result = await pollForFile(resultPath, 15_000, 200);

    if (!result) {
      return {
        content: [
          {
            type: "text",
            text: "Timed out waiting for Unity to write validate_result.json. Make sure Unity Editor is open with the scene active.",
          },
        ],
        isError: true,
      };
    }

    return {
      content: [
        {
          type: "text",
          text: result,
        },
      ],
    };
  }
);

// ─── Helpers ──────────────────────────────────────────────────────────────────

function ensureDir(dir) {
  if (!existsSync(dir)) {
    mkdirSync(dir, { recursive: true });
  }
}

/**
 * Polls a file path until it contains non-empty content or times out.
 * Returns file contents string, or null on timeout.
 */
function pollForFile(filePath, timeoutMs, intervalMs) {
  return new Promise((resolve) => {
    const deadline = Date.now() + timeoutMs;

    const check = () => {
      if (existsSync(filePath)) {
        const content = readFileSync(filePath, "utf8").trim();
        if (content.length > 0) {
          resolve(content);
          return;
        }
      }

      if (Date.now() >= deadline) {
        resolve(null);
        return;
      }

      setTimeout(check, intervalMs);
    };

    check();
  });
}

// ─── Start ────────────────────────────────────────────────────────────────────

const transport = new StdioServerTransport();
await server.connect(transport);
