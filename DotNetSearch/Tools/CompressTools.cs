using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DotNetSearch.Models;

namespace DotNetSearch.Tools;

[McpServerToolType]
public static class CompressTools
{
    [McpServerTool]
    [Description(
        "Reduces code indentation from 4-space (or configurable) to 1-space per indent level and removes blank lines. " +
        "Saves 10-20% tokens with zero semantic change for brace-scoped languages. " +
        "SAFE for: C#, Java, JavaScript, TypeScript, C, C++, Kotlin, Swift, Rust, Scala, PHP, Ruby, CSS, SCSS, JSON, XML. " +
        "DO NOT USE on indentation-sensitive languages: Python, YAML, CoffeeScript, Pug/Jade, HAML, Makefile — compression WILL corrupt these. " +
        "Accepts either an absolute file path or a raw code string. " +
        "Returns compressed code plus metrics: originalLength, compressedLength, savingsPercent, line counts. " +
        "Use when reading source files to minimize context window usage.")]
    public static string CompressCode(
        [Description("Absolute path to the C# source file to compress. Provide either this or 'code'."
        )] string? filePath = null,
        [Description("Raw C# code string to compress. Used when filePath is not provided."
        )] string? code = null,
        [Description("Number of spaces that represent one indent level in the source. Default: 4. Set to 2 for 2-space projects."
        )] int sourceIndentSize = 4,
        [Description("Number of spaces to use per indent level in the output. Default: 1."
        )] int targetIndentSize = 1,
        [Description("Remove blank/empty lines from the output. Default: true."
        )] bool removeBlankLines = true,
        [Description("Trim trailing whitespace from each line. Default: true."
        )] bool trimTrailingWhitespace = true)
    {
        var options = new CompressionOptions
        {
            SourceIndentSize = sourceIndentSize,
            TargetIndentSize = targetIndentSize,
            RemoveBlankLines = removeBlankLines,
            TrimTrailingWhitespace = trimTrailingWhitespace
        };

        CompressionResult result;

        if (!string.IsNullOrEmpty(filePath))
        {
            result = IndentationReducer.CompressFile(filePath, options);
        }
        else if (!string.IsNullOrEmpty(code))
        {
            result = IndentationReducer.Compress(code, options);
        }
        else
        {
            throw new ArgumentException("Either 'filePath' or 'code' parameter must be provided.");
        }

        var response = new
        {
            compressedCode = result.CompressedCode,
            metadata = new
            {
                originalLength = result.OriginalLength,
                compressedLength = result.CompressedLength,
                originalLineCount = result.OriginalLineCount,
                compressedLineCount = result.CompressedLineCount,
                savingsPercent = result.SavingsPercent
            }
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false });
    }
}
