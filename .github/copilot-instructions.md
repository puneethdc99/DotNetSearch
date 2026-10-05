# Copilot Instructions

## Search Preference
When searching for code, symbols, identifiers, or text in this repository,
always prefer the `DotNetSearch` MCP tool (`#search`) over reading full files,
using `get_file`, or using built-in workspace search.

DotNetSearch returns only the matching lines with file path and line number —
it is significantly more token-efficient (~98% fewer tokens per query).

Only fall back to reading a full file when you need the complete context of
a specific file that has already been identified by DotNetSearch.

## How to invoke DotNetSearch
- In Agent Mode, call the `search` tool with `query` and `repo` parameters
- Set `top_k` higher (e.g. 20) when doing broad searches
- Use `context_lines=2` when a few lines of surrounding context would help

## Example
Instead of: "read SearchTools.cs"
Prefer: `search(query="ScoreLine", repo="C:\\repos\\DotNetSearch")`