using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Spire.Pdf;
using Spire.Pdf.Annotations;
using Spire.Pdf.Bookmarks;
using Spire.Pdf.Fields;
using Spire.Pdf.Graphics;
using Spire.Pdf.Texts;
using Spire.Pdf.Utilities;
using Spire.Pdf.Widget;

namespace DotNetSearch.Tools;

[McpServerToolType]
public static class PdfTools
{
    [McpServerTool]
    [Description(
        "Read the metadata of a PDF file: page count, per-page size, and document info " +
        "(title, author, subject, keywords, creator, producer, creation/modification date). " +
        "Use this first to plan which pages to read text/images from.")]
    public static string GetPdfInfo(
        [Description("Absolute or relative path to the PDF file")] string path)
    {
        try
        {
            string? resolvedPath = ResolvePath(path, out string? error);
            if (resolvedPath is null)
                return JsonError(error!);

            using var doc = new PdfDocument();
            doc.LoadFromFile(resolvedPath);

            var pages = new List<object>();
            for (int i = 0; i < doc.Pages.Count; i++)
            {
                var size = doc.Pages[i].Size;
                pages.Add(new { page = i + 1, widthPt = size.Width, heightPt = size.Height });
            }

            var info = doc.DocumentInformation;

            return SafeSerialize(new
            {
                path = resolvedPath,
                pageCount = doc.Pages.Count,
                title = info?.Title,
                author = info?.Author,
                subject = info?.Subject,
                keywords = info?.Keywords,
                creator = info?.Creator,
                producer = info?.Producer,
                creationDate = info?.CreationDate,
                modificationDate = info?.ModificationDate,
                pages
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Extract the text content of a PDF file, optionally limited to a page range. " +
        "Returns text per page along with the overall extracted text. " +
        "Note: scanned/image-only PDFs will return little or no text — use ExtractPdfImages or " +
        "RenderPdfPageAsImage instead for those.")]
    public static string ReadPdfText(
        [Description("Absolute or relative path to the PDF file")] string path,
        [Description("1-based page number to start reading from (default: 1)")] int start_page = 1,
        [Description("1-based page number to stop reading at (default: 0 means read to last page)")] int end_page = 0)
    {
        try
        {
            string? resolvedPath = ResolvePath(path, out string? error);
            if (resolvedPath is null)
                return JsonError(error!);

            using var doc = new PdfDocument();
            doc.LoadFromFile(resolvedPath);

            int totalPages = doc.Pages.Count;
            if (!TryResolveRange(start_page, end_page, totalPages, out int start, out int end, out string? rangeError))
                return JsonError(rangeError!);

            var options = new PdfTextExtractOptions { IsExtractAllText = true };
            var pages = new List<object>();
            var allText = new StringBuilder();

            for (int i = start; i <= end; i++)
            {
                var extractor = new PdfTextExtractor(doc.Pages[i - 1]);
                string text = extractor.ExtractText(options) ?? string.Empty;
                pages.Add(new { page = i, text });
                allText.AppendLine($"--- Page {i} ---");
                allText.AppendLine(text);
            }

            return SafeSerialize(new
            {
                path = resolvedPath,
                totalPages,
                startPage = start,
                endPage = end,
                pages,
                text = allText.ToString()
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Extract embedded raster images from a PDF file (e.g. photos, diagrams) and save them as PNG files " +
        "to an output directory. Optionally limited to a page range. Returns the list of saved file paths " +
        "with their source page and size. Note: this extracts embedded image objects, not vector graphics/text — " +
        "use RenderPdfPageAsImage to rasterize an entire page as a picture instead.")]
    public static string ExtractPdfImages(
        [Description("Absolute or relative path to the PDF file")] string path,
        [Description("Directory to save extracted images into (created if it doesn't exist)")] string output_dir,
        [Description("1-based page number to start extracting from (default: 1)")] int start_page = 1,
        [Description("1-based page number to stop extracting at (default: 0 means last page)")] int end_page = 0,
        [Description("Maximum number of images to extract in total (default: 0 means unlimited)")] int max_images = 0)
    {
        try
        {
            string? resolvedPath = ResolvePath(path, out string? error);
            if (resolvedPath is null)
                return JsonError(error!);

            if (string.IsNullOrWhiteSpace(output_dir))
                return JsonError("output_dir must not be empty.");

            string resolvedOutputDir;
            try { resolvedOutputDir = Path.GetFullPath(output_dir); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { return JsonError($"Invalid output_dir '{output_dir}': {ex.Message}"); }

            Directory.CreateDirectory(resolvedOutputDir);

            using var doc = new PdfDocument();
            doc.LoadFromFile(resolvedPath);

            int totalPages = doc.Pages.Count;
            if (!TryResolveRange(start_page, end_page, totalPages, out int start, out int end, out string? rangeError))
                return JsonError(rangeError!);

            var imageHelper = new PdfImageHelper();
            var saved = new List<object>();
            string baseName = Path.GetFileNameWithoutExtension(resolvedPath);
            int imageIndex = 0;

            for (int i = start; i <= end; i++)
            {
                if (max_images > 0 && imageIndex >= max_images)
                    break;

                PdfImageInfo[] imageInfos = imageHelper.GetImagesInfo(doc.Pages[i - 1]);
                for (int j = 0; j < imageInfos.Length; j++)
                {
                    if (max_images > 0 && imageIndex >= max_images)
                        break;

                    using Stream imageStream = imageInfos[j].Image;
                    using Image image = Image.FromStream(imageStream);
                    string fileName = $"{baseName}_p{i}_img{j + 1}.png";
                    string outPath = Path.Combine(resolvedOutputDir, fileName);
                    image.Save(outPath, ImageFormat.Png);

                    saved.Add(new
                    {
                        page = i,
                        index = j + 1,
                        file = outPath,
                        widthPx = image.Width,
                        heightPx = image.Height
                    });
                    imageIndex++;
                }
            }

            return SafeSerialize(new
            {
                path = resolvedPath,
                outputDir = resolvedOutputDir,
                startPage = start,
                endPage = end,
                imageCount = saved.Count,
                images = saved
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Render a single PDF page as a full picture (PNG) — a screenshot of the whole page including text, " +
        "vector graphics and images. Useful for scanned documents or when embedded text/images aren't sufficient. " +
        "Returns the saved image file path and pixel dimensions.")]
    public static string RenderPdfPageAsImage(
        [Description("Absolute or relative path to the PDF file")] string path,
        [Description("1-based page number to render")] int page,
        [Description("Path (including file name) to save the rendered PNG image to")] string output_path,
        [Description("Resolution in dots per inch for the rendered image (default: 150)")] int dpi = 150)
    {
        try
        {
            string? resolvedPath = ResolvePath(path, out string? error);
            if (resolvedPath is null)
                return JsonError(error!);

            if (string.IsNullOrWhiteSpace(output_path))
                return JsonError("output_path must not be empty.");

            string resolvedOutputPath;
            try { resolvedOutputPath = Path.GetFullPath(output_path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { return JsonError($"Invalid output_path '{output_path}': {ex.Message}"); }

            if (dpi <= 0)
                return JsonError("dpi must be greater than 0.");

            using var doc = new PdfDocument();
            doc.LoadFromFile(resolvedPath);

            int totalPages = doc.Pages.Count;
            if (page < 1 || page > totalPages)
                return JsonError($"page must be between 1 and {totalPages} (got {page}).");

            string? outDir = Path.GetDirectoryName(resolvedOutputPath);
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            using Stream pageStream = doc.SaveAsImage(page - 1, dpi, dpi);
            using Image image = Image.FromStream(pageStream);
            image.Save(resolvedOutputPath, ImageFormat.Png);

            return SafeSerialize(new
            {
                path = resolvedPath,
                page,
                dpi,
                file = resolvedOutputPath,
                widthPx = image.Width,
                heightPx = image.Height
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Read the bookmark/outline tree (table of contents) of a PDF file. Returns each bookmark's title, " +
        "target page number (1-based, when resolvable), and nested child bookmarks. " +
        "Use this to understand a PDF's structure before reading specific pages.")]
    public static string GetPdfBookmarks(
        [Description("Absolute or relative path to the PDF file")] string path)
    {
        try
        {
            string? resolvedPath = ResolvePath(path, out string? error);
            if (resolvedPath is null)
                return JsonError(error!);

            using var doc = new PdfDocument();
            doc.LoadFromFile(resolvedPath);

            var bookmarks = BuildBookmarkTree(doc.Bookmarks);

            return SafeSerialize(new
            {
                path = resolvedPath,
                bookmarkCount = CountBookmarks(bookmarks),
                bookmarks
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Read all interactive AcroForm fields from a PDF file (text boxes, checkboxes, radio buttons, " +
        "combo/list boxes, signature fields). Returns each field's name, type, and current value. " +
        "Use this to extract data entered into fillable PDF forms.")]
    public static string GetPdfFormFields(
        [Description("Absolute or relative path to the PDF file")] string path)
    {
        try
        {
            string? resolvedPath = ResolvePath(path, out string? error);
            if (resolvedPath is null)
                return JsonError(error!);

            using var doc = new PdfDocument();
            doc.LoadFromFile(resolvedPath);

            var fields = new List<object>();
            PdfFormFieldCollection? formFields = doc.Form?.Fields;
            int count = formFields?.Count ?? 0;

            for (int i = 0; i < count; i++)
            {
                PdfField field = formFields![i];
                fields.Add(new
                {
                    name = field.Name,
                    fullName = field.FullName,
                    type = field.GetType().Name,
                    page = field.Page is null ? (int?)null : doc.Pages.IndexOf(field.Page) + 1,
                    value = GetFieldValue(field),
                    readOnly = field.ReadOnly
                });
            }

            return SafeSerialize(new
            {
                path = resolvedPath,
                fieldCount = fields.Count,
                fields
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Extract embedded file attachments from a PDF file and save them to an output directory. " +
        "PDFs can carry arbitrary embedded files (documents, spreadsheets, etc.) as attachments, distinct " +
        "from the PDF's own embedded images. Returns the list of saved file paths and metadata.")]
    public static string ExtractPdfAttachments(
        [Description("Absolute or relative path to the PDF file")] string path,
        [Description("Directory to save extracted attachments into (created if it doesn't exist)")] string output_dir)
    {
        try
        {
            string? resolvedPath = ResolvePath(path, out string? error);
            if (resolvedPath is null)
                return JsonError(error!);

            if (string.IsNullOrWhiteSpace(output_dir))
                return JsonError("output_dir must not be empty.");

            string resolvedOutputDir;
            try { resolvedOutputDir = Path.GetFullPath(output_dir); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { return JsonError($"Invalid output_dir '{output_dir}': {ex.Message}"); }

            Directory.CreateDirectory(resolvedOutputDir);

            using var doc = new PdfDocument();
            doc.LoadFromFile(resolvedPath);

            var saved = new List<object>();
            for (int i = 0; i < doc.Attachments.Count; i++)
            {
                var attachment = doc.Attachments[i];
                string fileName = string.IsNullOrWhiteSpace(attachment.FileName)
                    ? $"attachment_{i + 1}"
                    : Path.GetFileName(attachment.FileName);
                string outPath = Path.Combine(resolvedOutputDir, fileName);

                File.WriteAllBytes(outPath, attachment.Data ?? Array.Empty<byte>());

                saved.Add(new
                {
                    fileName = attachment.FileName,
                    file = outPath,
                    sizeBytes = attachment.Data?.Length ?? 0,
                    description = attachment.Description,
                    mimeType = attachment.MimeType
                });
            }

            return SafeSerialize(new
            {
                path = resolvedPath,
                outputDir = resolvedOutputDir,
                attachmentCount = saved.Count,
                attachments = saved
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Read hyperlinks and internal links from a PDF file, optionally limited to a page range. " +
        "Returns each link's source page, on-page location, and target — an external URI, an internal " +
        "destination page number, or a linked external file name.")]
    public static string GetPdfHyperlinks(
        [Description("Absolute or relative path to the PDF file")] string path,
        [Description("1-based page number to start scanning from (default: 1)")] int start_page = 1,
        [Description("1-based page number to stop scanning at (default: 0 means last page)")] int end_page = 0)
    {
        try
        {
            string? resolvedPath = ResolvePath(path, out string? error);
            if (resolvedPath is null)
                return JsonError(error!);

            using var doc = new PdfDocument();
            doc.LoadFromFile(resolvedPath);

            int totalPages = doc.Pages.Count;
            if (!TryResolveRange(start_page, end_page, totalPages, out int start, out int end, out string? rangeError))
                return JsonError(rangeError!);

            var links = new List<object>();
            for (int i = start; i <= end; i++)
            {
                var page = doc.Pages[i - 1];
                for (int a = 0; a < page.Annotations.Count; a++)
                {
                    PdfAnnotation annotation = page.Annotations[a];
                    var (linkType, target) = annotation switch
                    {
                        PdfUriAnnotation uri => ("uri", (object)uri.Uri),
                        PdfFileLinkAnnotation file => ("file", file.FileName),
                        PdfDocumentLinkAnnotation docLink => ("internal", docLink.Destination?.PageNumber + 1),
                        _ => (null, null)
                    };

                    if (linkType is null)
                        continue;

                    var rect = annotation.Rectangle;
                    links.Add(new
                    {
                        page = i,
                        type = linkType,
                        target,
                        bounds = new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height }
                    });
                }
            }

            return SafeSerialize(new
            {
                path = resolvedPath,
                startPage = start,
                endPage = end,
                linkCount = links.Count,
                links
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static object? GetFieldValue(PdfField field) => field switch
    {
        PdfTextBoxFieldWidget textBox => textBox.Text,
        PdfCheckBoxWidgetFieldWidget checkBox => checkBox.Checked,
        PdfRadioButtonListFieldWidget radio => radio.SelectedValue,
        PdfChoiceWidgetFieldWidget choice => choice.SelectedValue,
        PdfSignatureFieldWidget => "(signature field)",
        _ => null
    };

    private static List<object> BuildBookmarkTree(PdfBookmarkCollection bookmarks)
    {
        var result = new List<object>();
        for (int i = 0; i < bookmarks.Count; i++)
        {
            PdfBookmark bookmark = bookmarks[i];
            result.Add(new
            {
                title = bookmark.Title,
                page = bookmark.Destination is null ? (int?)null : bookmark.Destination.PageNumber + 1,
                children = BuildBookmarkTree(bookmark)
            });
        }
        return result;
    }

    private static int CountBookmarks(List<object> bookmarks)
    {
        int count = bookmarks.Count;
        foreach (dynamic b in bookmarks)
            count += CountBookmarks(b.children);
        return count;
    }

    private static string? ResolvePath(string path, out string? error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path must not be empty.";
            return null;
        }

        string resolvedPath;
        try { resolvedPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid path '{path}': {ex.Message}";
            return null;
        }

        if (!File.Exists(resolvedPath))
        {
            error = $"File not found: {resolvedPath}";
            return null;
        }

        error = null;
        return resolvedPath;
    }

    private static bool TryResolveRange(int startPage, int endPage, int totalPages, out int start, out int end, out string? error)
    {
        start = 0;
        end = 0;
        error = null;

        if (startPage < 1)
        {
            error = "start_page must be at least 1.";
            return false;
        }

        start = Math.Min(startPage, Math.Max(totalPages, 1));
        end = endPage <= 0 || endPage > totalPages ? totalPages : endPage;

        if (end < start)
        {
            error = "end_page must be greater than or equal to start_page.";
            return false;
        }

        return true;
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });

    private static string SafeSerialize(object payload) =>
        JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
}
