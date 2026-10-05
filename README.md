# DotNetSearch — MCP Search & PDF Server

**DotNetSearch** is a self-contained MCP (Model Context Protocol) server that empowers GitHub Copilot, Claude, Cursor, and other AI agents with high-performance codebase search, NuGet package decompilation, whitespace token reduction, and **deep PDF document reading and analysis**:

### Key Tools Overview

| Category | Tool | Purpose |
|---|---|---|
| **Code Search** | `search` | Find files and lines matching a keyword or natural-language query — returns only matching lines (~110 tokens vs ~2500 for a full file read) |
| **Code Search** | `search_related` | Find code chunks structurally similar to a known location — ideal for discovering callers, implementations, or patterns |
| **Decompilation** | `decompile_type` | Decompile any .NET type from referenced NuGet package assemblies — returns the full C# class definition without needing the source code |
| **Optimization** | `compress_code` | Reduce C# code indentation from 4-space to 1-space per indent level and remove blank lines — saves 10–20% tokens with zero semantic change |
| **File Reading** | `read_file` | Read a file from disk and optionally return a specific line range — useful after `search` when you need full file context |
| **PDF Reading** | `read_pdf_text` | Extract clean, structured text page-by-page or across targeted page ranges from any PDF document |
| **PDF Reading** | `get_pdf_info` | Inspect PDF metadata, total page count, per-page dimensions, author, subject, creation date, and structure |
| **PDF Visuals** | `render_pdf_page_as_image` | Rasterize any PDF page into a high-resolution PNG image at custom DPI — ideal for inspecting charts, diagrams, tables, and scanned documents |
| **PDF Visuals** | `extract_pdf_images` | Extract embedded raster images from PDF pages and save to disk with pixel coordinates |
| **PDF Structure** | `get_pdf_bookmarks` | Extract the hierarchical table of contents, bookmarks, and document outline with target page links |
| **PDF Forms** | `get_pdf_form_fields` | Read interactive AcroForm form fields (text boxes, checkboxes, radio buttons, dropdowns) and their values |
| **PDF Assets** | `extract_pdf_attachments` | Extract embedded file attachments from PDF documents and save them to disk |
| **PDF Links** | `get_pdf_hyperlinks` | Extract all clickable web URLs, internal page jumps, and external document links with bounding boxes |

Instead of reading full files (~2500 tokens each), the agent gets back only the matching lines (~110 tokens) — saving up to 98% of context window per query. For files that must be read in full, use `compress_code` first to strip whitespace overhead and save an additional 10–20% of tokens. For PDF documentation, technical manuals, and specifications, dedicated tools extract targeted text, images, and tables without overwhelming the model context window.

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

### Windows configuration

Add this to the `servers` section:

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

### Linux / macOS configuration

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
3. Click the **wrench / Select Tools** icon
4. Confirm the DotNetSearch tools appear in the list and are enabled:
   - **Code & Navigation:** `search`, `search_related`, `decompile_type`, `compress_code`, `read_file`
   - **PDF Document Inspection:** `read_pdf_text`, `get_pdf_info`, `render_pdf_page_as_image`, `extract_pdf_images`, `get_pdf_bookmarks`, `get_pdf_form_fields`, `extract_pdf_attachments`, `get_pdf_hyperlinks`

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

## PDF Document Reading
When reading, analyzing, or searching PDF documents:
- Always prefer `read_pdf_text` and `get_pdf_info` over raw file operations.
- First call `get_pdf_info` to inspect total pages and document metadata.
- Call `read_pdf_text` with specific page ranges (`startPage`, `endPage`) to avoid token overflow.
- If the PDF contains visual charts, flowcharts, architectural diagrams, or scanned pages, use `render_pdf_page_as_image` to rasterize the page.
- For interactive PDF forms, use `get_pdf_form_fields` to extract user inputs.

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

No special commands or workflow changes are needed. Just continue your normal coding tasks in Copilot Agent Mode — DotNetSearch will be used automatically whenever Copilot needs to search your codebase or inspect PDF documents.

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

### `read_file`

Reads a text file from disk and returns its contents. Use it after `search` when you already know the target file and need the full context. Supports an absolute or relative path plus optional `start_line` and `end_line` range arguments.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the file to read |
| `start_line` | int | `1` | 1-based line number to start reading from |
| `end_line` | int | `0` | 1-based line number to stop reading at (0 = read to end) |

---

### `read_pdf_text`

Extracts clean, formatted text from a PDF file with page markers. Ideal for documentation, technical specifications, papers, and architecture guides. Supports whole-document extraction or targeted page ranges.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the PDF file |
| `startPage` | int | `1` | 1-based page number to begin extracting text from |
| `endPage` | int | `0` | 1-based page number to stop extracting text at (0 = read through the last page) |

---

### `get_pdf_info`

Retrieves comprehensive metadata and physical layout information about a PDF file without reading its full text content. Use this first to plan targeted extractions.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the PDF file |

**Returns:** Total page count, dimensions per page (`widthPt`, `heightPt`), document title, author, subject, keywords, creator, producer, creation date, and modification date.

---

### `render_pdf_page_as_image`

Rasterizes a specific PDF page into a high-resolution PNG image on disk. Essential for pages with complex graphical diagrams, architectural flowcharts, schematics, tables, or scanned documents where raw text extraction is insufficient.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the PDF file |
| `pageNumber` | int | `1` | 1-based index of the page to render |
| `dpi` | int | `150` | Render resolution in DPI (e.g. 72 = draft, 150 = standard, 300 = print/high-res) |

**Returns:** Path to the rendered `.png` image and image pixel dimensions.

---

### `extract_pdf_images`

Extracts all embedded raster images (e.g., photos, diagrams, embedded JPEGs/PNGs) from a PDF page and saves them directly to disk.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the PDF file |
| `pageNumber` | int | `0` | 1-based page number (0 = extract embedded images across all pages) |
| `outputDirectory` | string | `null` | Folder to save images into (defaults to `<pdf-folder>/<pdf-name>_images/`) |

---

### `get_pdf_bookmarks`

Extracts the hierarchical bookmark outline (Table of Contents tree) from a PDF. Helps agents understand the complete document structure before requesting specific chapters or pages.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the PDF file |

**Returns:** Recursive tree of bookmarks with titles and target destination page numbers.

---

### `get_pdf_form_fields`

Reads all interactive AcroForm form fields embedded in a PDF document.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the PDF file |

**Returns:** List of form fields with field names, field types (textbox, checkbox, combobox, radio button), and current values.

---

### `extract_pdf_attachments`

Extracts files embedded directly inside the PDF catalog (e.g., attached source code, sample data, schemas, or companion files) and writes them to disk.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the PDF file |
| `outputDirectory` | string | `null` | Directory to save extracted attachments into |

---

### `get_pdf_hyperlinks`

Extracts all hyperlinks and clickable annotations present on PDF pages.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | Absolute or relative path to the PDF file |
| `pageNumber` | int | `0` | 1-based page number (0 = extract links from all pages) |

**Returns:** URL targets, destination page jumps, and bounding coordinates for each hyperlink.

---

### `decompile_type`

Decompiles a .NET type (class, interface, enum, struct, or delegate) from NuGet package assemblies referenced by a .NET project. Useful when you need to understand third-party APIs without source code.

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

## Supported file types

`.pdf` `.cs` `.ts` `.js` `.jsx` `.tsx` `.json` `.md` `.txt` `.xml` `.yaml` `.yml` `.html` `.css` `.py` `.java` `.cpp` `.c` `.h` `.go` `.rs` `.sh` `.ps1` `.psm1` `.psd1` `.toml` `.ini` `.env` `.config` `.csproj` `.props` `.targets` `.razor` `.vue` `.svelte`

---

## License

MIT — see [LICENSE](LICENSE) for details.