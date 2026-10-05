namespace DotNetSearch.Tools;

using DotNetSearch.Models;

/// <summary>
/// Reduces indentation in source code to minimize LLM token consumption.
/// Converts N-space indentation to 1-space per indent level.
/// Preserves all code content, comments, and string literals.
///
/// Safe for brace-scoped / keyword-scoped languages where indentation is cosmetic:
/// C#, Java, JavaScript, TypeScript, C, C++, Kotlin, Swift, Rust, Scala, PHP, Ruby, CSS, SCSS, JSON, XML.
///
/// NOT safe for indentation-sensitive languages:
/// Python, YAML, CoffeeScript, Pug/Jade, HAML, Makefile.
/// Compressing those will corrupt the code structure.
/// </summary>
public static class IndentationReducer
{
    /// <summary>
    /// Compresses indentation in the provided source code.
    /// Safe for brace-scoped languages (C#, Java, JS, TS, etc.).
    /// Do NOT use on Python, YAML, or other indentation-sensitive languages.
    /// </summary>
    /// <param name="code">Raw source code string.</param>
    /// <param name="options">Compression options. Uses defaults if null.</param>
    /// <returns>CompressionResult with compressed code and metrics.</returns>
    public static CompressionResult Compress(string code, CompressionOptions? options = null)
    {
        options ??= new CompressionOptions();

        if (string.IsNullOrEmpty(code))
        {
            return new CompressionResult
            {
                CompressedCode = string.Empty,
                OriginalLength = 0,
                CompressedLength = 0,
                OriginalLineCount = 0,
                CompressedLineCount = 0
            };
        }

        var lines = code.Split('\n');
        var result = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            // Handle blank lines
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!options.RemoveBlankLines)
                {
                    result.Add(string.Empty);
                }
                continue;
            }

            // Calculate indent level from leading whitespace
            int leadingSpaces = CountLeadingWhitespace(line, options.SourceIndentSize);
            int indentLevel = leadingSpaces / options.SourceIndentSize;
            int remainder = leadingSpaces % options.SourceIndentSize;

            // Get content without leading whitespace
            string content = line.TrimStart();

            // Trim trailing whitespace if configured
            if (options.TrimTrailingWhitespace)
            {
                content = content.TrimEnd();
            }

            // Skip if line is empty after trimming
            if (string.IsNullOrEmpty(content) && options.RemoveBlankLines)
            {
                continue;
            }

            // Build new line: reduced indent + original content
            string newIndent = new string(' ', (indentLevel * options.TargetIndentSize) + remainder);
            result.Add(newIndent + content);
        }

        var compressedCode = string.Join("\n", result);

        return new CompressionResult
        {
            CompressedCode = compressedCode,
            OriginalLength = code.Length,
            CompressedLength = compressedCode.Length,
            OriginalLineCount = lines.Length,
            CompressedLineCount = result.Count
        };
    }

    /// <summary>
    /// Convenience method: compress from file path.
    /// </summary>
    public static CompressionResult CompressFile(string filePath, CompressionOptions? options = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Source file not found: {filePath}");
        }

        var code = File.ReadAllText(filePath);
        return Compress(code, options);
    }

    /// <summary>
    /// Counts leading whitespace as equivalent space count.
    /// Tabs are expanded to the configured indent size.
    /// </summary>
    private static int CountLeadingWhitespace(string line, int tabSize)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ')
                count++;
            else if (c == '\t')
                count += tabSize;
            else
                break;
        }
        return count;
    }
}
