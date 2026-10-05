# DotNetSearch — MCP Search Server

**DotNetSearch** is a self-contained MCP (Model Context Protocol) server that gives GitHub Copilot and other AI agents five powerful tools for working with any local .NET codebase:

| Tool | Purpose |
|---|---|
| `search` | Find files and lines matching a keyword or natural-language query — returns only matching lines (~110 tokens vs ~2500 for a full file read) |
| `search_related` | Find code chunks structurally similar to a known location — ideal for discovering callers, implementations, or patterns |
| `decompile_type` | Decompile any .NET type from a referenced NuGet package assembly — returns the full C# class definition without needing the source code |
| `compress_code` | Reduce C# code indentation from 4-space to 1-space per indent level and remove blank lines — saves 10–20% tokens with zero semantic change |
| `read_file` | Read a file from disk and optionally return a specific line range — useful after `search` when you need full file context |

Instead of reading full files (~2500 tokens each), the agent gets back only the matching lines (~110 tokens) — saving up to 98% of context window per query. For files that must be read in full, use `compress_code` first to strip whitespace overhead and save an additional 10–20% of tokens.

---

## Step 1 — Download the executable

Go to the [latest release](https://github.com/puneethdc99/DotNetSearch/releases/latest) and download the file for your platform:

| Platform    | File to download          |
|-------------|---------------------------|
| Windows x64 | `DotNetSearch-win-x64.exe` |
| Linux x64   | `DotNetSearch-linux-x64`   |
| macOS ARM64 | `DotNetSearch-osx-arm64`   |

> No .NET installation required. The runtime is bundled inside the executable.

Save it somewhere permanent, e.g.:
- **Windows:** `C:\Tools\DotNetSearch.exe`
- **Linux/macOS:** `/usr/local/bin/DotNetSearch`

On Linux/macOS, make it executable:
```bash
chmod +x /usr/local/bin/DotNetSearch
```

---

## Step 2 — Add to your MCP config

Open (or create) your global MCP config file:

| Client | Config file location |
|--------|---------------------|
| Visual Studio | `%USERPROFILE%\.mcp.json` |
| VS Code + Copilot | `%USERPROFILE%\.mcp.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |
| Cursor | `%USERPROFILE%\.cursor\mcp.json` |

Add the following entry under `"servers"`:

**Windows**
```json
{
  "servers": {
    "DotNetSearch": {
      "type": "stdio",
      "command": "C:\\Tools\\DotNetSearch.exe"
    }
  }
}
```

**Linux / macOS**
```json
{
  "servers": {
    "DotNetSearch": {
      "type": "stdio",
      "command": "/usr/local/bin/DotNetSearch"
    }
  }
}
```

If you already have other servers in the file, just add the `"DotNetSearch"` block inside the existing `"servers"` object.

---

## Step 3 — Verify it is active

**Visual Studio / VS Code:**
1. Open GitHub Copilot Chat
2. Switch to **Agent Mode**
3. Click the **🔧 wrench / Select Tools** icon
4. Confirm the following tools appear in the list and are enabled:
   - **DotNetSearch → search**
   - **DotNetSearch → search_related**
   - **DotNetSearch → decompile_type**
   - **DotNetSearch → compress_code**
   - **DotNetSearch → read_file**

---

## Step 4 — Update your Copilot instructions

To make Copilot automatically prefer DotNetSearch over built-in file reading, add the instructions below to your Copilot instructions file. There are two places you can put this — repo-level (affects only that repo) or global (affects all repos).

---

### Option A — Repo-level instructions (recommended)

**File location:**
```
<your-repo-root>\.github\copilot-instructions.md
```

This file is read by GitHub Copilot for every conversation in that repository. It is the recommended place if you want DotNetSearch to be preferred only for a specific project.

**How to find or create it:**
- Navigate to your repository root folder
- Look for a `.github` subfolder — if it doesn't exist, create it
- Inside `.github`, look for `copilot-instructions.md` — if it doesn't exist, create the file

---

### Option B — Global instructions (applies to all repos)

**File location:**

| Client | Global instructions file |
|--------|--------------------------|
| Visual Studio | `%APPDATA%\Microsoft\VisualStudio\Copilot\copilot-instructions.md` |
| VS Code + Copilot | `%USERPROFILE%\.github\copilot-instructions.md` |

**How to find or create it:**
- Open the path above in File Explorer (paste into the address bar)
- If the folder doesn't exist, create it
- If `copilot-instructions.md` doesn't exist in the folder, create the file

> **Note:** If you add instructions to both files, Copilot merges them — both will be active at the same time.

---

### Instructions to add

Paste the following into whichever file you chose above:

```markdown
## Search Preference
When searching for code, symbols, or text in this repository, always prefer
the `DotNetSearch` MCP tool (`search` and `search_related`) over reading full
files or using built-in workspace search. DotNetSearch returns only matching
lines and is significantly more token-efficient (~98% fewer tokens per query).
Do not call DotNetSearch tools in parallel — run one search at a time.

## NuGet Type Inspection
When the user asks about a third-party or NuGet type and the source code is not
available in the workspace, use the `DotNetSearch` MCP tool `decompile_type` to
decompile the type from the project's referenced assemblies. Pass the short or
fully qualified type name and the absolute path to the .csproj file. The project
must have been built at least once before calling this tool.

## Code Compression
When reading source files to minimize context window usage, use the
`DotNetSearch` MCP tool `compress_code` — BUT ONLY for the following fully-safe
languages where indentation is cosmetic and braces/keywords define scope:

  C#, Java, JavaScript, TypeScript, C, C++, Kotlin, Swift, Rust, Scala,
  PHP, Ruby, CSS, SCSS, Less, JSON, XML

NEVER call `compress_code` on any other language. In particular, NEVER use it on
Python, YAML, CoffeeScript, Pug/Jade, HAML, or Makefile — these are
indentation-sensitive and compression WILL silently corrupt the file structure.

Before calling `compress_code`, check the file extension:
- Allowed:  .cs .java .js .ts .jsx .tsx .kt .swift .rs .scala .php .rb .css .scss .less .json .xml .c .cpp .h
- Blocked:  .py .yaml .yml .coffee .pug .jade .haml  (and Makefiles with no extension)

Pass the absolute file path. For JavaScript/TypeScript projects using 2-space
indentation, set `sourceIndentSize=2`.
```

---

## Step 5 — You're done

No special commands or workflow changes are needed. Just continue your normal coding tasks in Copilot Agent Mode — DotNetSearch will be used automatically whenever Copilot needs to search your codebase.

Over time you will notice a reduction in token usage per conversation. This is because DotNetSearch returns only the matching lines (typically ~110 tokens) instead of full file contents (~2500 tokens), saving up to 98% of context window per search operation.

---

## Tool reference

### `search`

Searches a local directory for files whose content matches a keyword or natural-language query. Uses BM25 ranking with CamelCase and snake_case token expansion.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `query` | string | required | Keyword, identifier, or natural-language phrase |
| `repo` | string | required | Absolute or relative path to the directory to search |
| `top_k` | int | `5` | Max results to return (1–100) |
| `context_lines` | int | `0` | Lines of context above/below each match (0–5) |

---

### `search_related`

Finds code chunks structurally similar to a specific location in a file. Use after `search` to explore related implementations, callers, or patterns.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `file_path` | string | required | Path to the file as returned by a prior `search` result |
| `line` | int | required | Line number (1-indexed) from a prior `search` result |
| `repo` | string | required | Absolute or relative path to the directory to search |
| `top_k` | int | `5` | Number of similar chunks to return (1–100) |

---

### `decompile_type`

Decompiles a .NET type (class, interface, enum, struct, or delegate) from the NuGet package assemblies referenced by a .NET project. Useful when you need to understand a third-party API without access to its source code.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `typeName` | string | required | Short name (`Session`) or fully qualified name (`Opc.Ua.Client.Session`) |
| `projectPath` | string | required | Absolute path to the `.csproj` that references the NuGet package |
| `maxResults` | int | `1` | Max matches to return when the name is ambiguous across assemblies |

> **Note:** The project must be built at least once so the `bin\` output folder is populated with NuGet assemblies.

---

### `compress_code`

Reduces code indentation from 4-space (or configurable) to 1-space per indent level and removes blank lines. Saves 10–20% tokens with zero semantic change — safe because brace-scoped and keyword-scoped languages treat indentation as cosmetic. Accepts either an absolute file path or a raw code string. Returns compressed code plus metrics: `originalLength`, `compressedLength`, `savingsPercent`, and line counts.

**✅ Safe for (brace-scoped / keyword-scoped):**
C#, Java, JavaScript, TypeScript, C, C++, Kotlin, Swift, Rust, Scala, PHP, Ruby, CSS, SCSS, Less, JSON, XML

**❌ Do NOT use on (indentation-sensitive — will corrupt code):**
Python, YAML, CoffeeScript, Pug/Jade, HAML, Makefile

**⚠️ Conditional:**
- **Go** — safe for LLM reading; uses tabs natively so set `sourceIndentSize=4` (tab width). Round-trip compilation would need `gofmt`.
- **Markdown** — avoid; 4-space indent = code block in Markdown spec.
- **JavaScript/TypeScript** — set `sourceIndentSize=2` for 2-space projects.
- **Multi-line string literals** (C# verbatim strings, Java `"""`, JS template literals) — internal whitespace is also reduced. Code is still readable by an LLM but not round-trip safe for strings that depend on exact internal indentation.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `filePath` | string | `null` | Absolute path to the source file to compress. Provide either this or `code`. |
| `code` | string | `null` | Raw source code string to compress. Used when `filePath` is not provided. |
| `sourceIndentSize` | int | `4` | Number of spaces that represent one indent level in the source. Set to `2` for JS/TS/Ruby/Scala projects. |
| `targetIndentSize` | int | `1` | Number of spaces to use per indent level in the output. |

---

**Language compatibility at a glance:**

| Category | Languages |
|---|---|
| ✅ **Fully safe** | C#, Java, JavaScript, TypeScript, C, C++, Kotlin, Swift, Rust, Scala, PHP, Ruby, CSS, SCSS, Less, JSON, XML |
| ⚠️ **Conditionally safe** | Go (LLM reading only — use `sourceIndentSize=4`), Markdown (avoid), Haskell/F# (use with caution) |
| ❌ **Will corrupt** | Python, YAML, CoffeeScript, Pug/Jade, HAML, Makefile |

**Typical token savings by language:**

| Language | Typical indent | Expected savings |
|---|---|---|
| C#, Java, Kotlin, Swift | 4-space + blank lines | ~18–22% |
| JavaScript, TypeScript | 2-space or 4-space | ~10–18% |
| C, C++, Rust, PHP | 4-space | ~18–22% |
| Go | tabs | ~15–19% |
| CSS, SCSS, Less | 2–4-space | ~10–18% |
| JSON, XML | any | ~10–15% |
| **Python, YAML** | **any** | **❌ DO NOT USE** |

**Example output:**
```json
{
  "compressedCode": "public class Foo {\n public void Bar() {\n  Console.WriteLine(\"hello\");\n }\n}",
  "metadata": {
    "originalLength": 27075,
    "compressedLength": 21813,
    "savingsPercent": 19.43,
    "originalLineCount": 616,
    "compressedLineCount": 529
  }
}
```

> **When to use:** Call `compress_code` before processing any large source file (C#, Java, JS, TS, etc.) to reduce the token cost of reading it. On typical 4-space-indented files this saves ~19% — equivalent to skipping the blank-line and indentation overhead that carries no information for an LLM. **Do not call on Python, YAML, or other indentation-sensitive files.**

**Typical workflow:**
```
Step 1 → search(query="MyApp.csproj", repo="C:\repos\MyApp")
         → gets the exact .csproj path

Step 2 → decompile_type(typeName="Session", projectPath="C:\repos\MyApp\MyApp.csproj")
         → returns the full decompiled C# class definition
```

### `read_file`

Reads a text file from disk and returns its contents. Use it after `search` when you already know the target file and need the full context. Supports an absolute or relative path plus optional `start_line` and `end_line` range arguments.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the file to read |
| `start_line` | int | `1` | 1-based line number to start reading from |
| `end_line` | int | `0` | 1-based line number to stop reading at (0 = read to end) |

---

## Supported file types

`.cs` `.ts` `.js` `.jsx` `.tsx` `.json` `.md` `.txt` `.xml` `.yaml` `.yml` `.html` `.css` `.py` `.java` `.cpp` `.c` `.h` `.go` `.rs` `.sh` `.ps1` `.psm1` `.psd1` `.toml` `.ini` `.env` `.config` `.csproj` `.props` `.targets` `.razor` `.vue` `.svelte`

---

## License

MIT — see [LICENSE](LICENSE) for details.