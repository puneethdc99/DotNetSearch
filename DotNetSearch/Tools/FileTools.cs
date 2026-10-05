using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DotNetSearch.Tools;

[McpServerToolType]
public static class FileTools
{
    [McpServerTool]
    [Description(
        "Read a text file from disk and return its contents. " +
        "Useful when you need the full contents of a specific file after a search. " +
        "Supports both absolute and relative paths, and can optionally read a specific line range.")]
    public static string ReadFile(
        [Description("Absolute or relative path to the file to read")] string path,
        [Description("1-based line number to start reading from (default: 1)")] int start_line = 1,
        [Description("1-based line number to stop reading at (default: 0 means read to end)")] int end_line = 0)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return JsonError("path must not be empty.");

            string resolvedPath;
            try { resolvedPath = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { return JsonError($"Invalid path '{path}': {ex.Message}"); }

            if (!File.Exists(resolvedPath))
                return JsonError($"File not found: {resolvedPath}");

            string[] allLines = File.ReadAllLines(resolvedPath);
            int totalLines = allLines.Length;

            if (start_line < 1)
                return JsonError("start_line must be at least 1.");

            int start = Math.Min(start_line, totalLines);
            int end = end_line <= 0 || end_line > totalLines
                ? totalLines
                : Math.Min(end_line, totalLines);

            if (end < start)
                return JsonError("end_line must be greater than or equal to start_line.");

            string[] selectedLines = allLines[(start - 1)..end];
            string content = string.Join(Environment.NewLine, selectedLines);

            return SafeSerialize(new
            {
                path = resolvedPath,
                startLine = start,
                endLine = end,
                totalLines,
                lineCount = selectedLines.Length,
                content
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });

    private static string SafeSerialize(object payload) =>
        JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
}
