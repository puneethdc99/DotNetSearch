namespace DotNetSearch.Models;

/// <summary>
/// Result of indentation compression operation.
/// </summary>
public class CompressionResult
{
    /// <summary>
    /// The compressed code string.
    /// </summary>
    public string CompressedCode { get; set; } = string.Empty;

    /// <summary>
    /// Original character count.
    /// </summary>
    public int OriginalLength { get; set; }

    /// <summary>
    /// Compressed character count.
    /// </summary>
    public int CompressedLength { get; set; }

    /// <summary>
    /// Number of lines in original.
    /// </summary>
    public int OriginalLineCount { get; set; }

    /// <summary>
    /// Number of lines in compressed output.
    /// </summary>
    public int CompressedLineCount { get; set; }

    /// <summary>
    /// Percentage of characters saved.
    /// </summary>
    public double SavingsPercent =>
        OriginalLength == 0 ? 0 : Math.Round((1 - (double)CompressedLength / OriginalLength) * 100, 2);
}
