namespace DotNetSearch.Models;

/// <summary>
/// Configuration options for indentation reduction.
/// </summary>
public class CompressionOptions
{
    /// <summary>
    /// Number of spaces that represent one indent level in source code.
    /// Default: 4
    /// </summary>
    public int SourceIndentSize { get; set; } = 4;

    /// <summary>
    /// Number of spaces to use per indent level in output.
    /// Default: 1
    /// </summary>
    public int TargetIndentSize { get; set; } = 1;

    /// <summary>
    /// Whether to remove blank/empty lines.
    /// Default: true
    /// </summary>
    public bool RemoveBlankLines { get; set; } = true;

    /// <summary>
    /// Whether to trim trailing whitespace from lines.
    /// Default: true
    /// </summary>
    public bool TrimTrailingWhitespace { get; set; } = true;
}
