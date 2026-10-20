using System.Text;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public sealed record FileValidationResult(bool IsValid, string? Error, string? FileType, string? Extension);

public sealed record MetadataValidationResult(
    bool IsValid,
    string? Error,
    string Title,
    string? Description,
    IReadOnlyList<string> Tags);

public sealed class DocumentValidator
{
    public const long MaximumFileSizeBytes = 26_214_400;
    private const int SignatureBytesToRead = 8192;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly IReadOnlyDictionary<string, (string MimeType, byte[]? Signature)> FileTypes =
        new Dictionary<string, (string, byte[]?)>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", Encoding.ASCII.GetBytes("%PDF-")),
            [".doc"] = ("application/msword", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [0x50, 0x4B, 0x03, 0x04]),
            [".xls"] = ("application/vnd.ms-excel", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [0x50, 0x4B, 0x03, 0x04]),
            [".ppt"] = ("application/vnd.ms-powerpoint", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            [".pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", [0x50, 0x4B, 0x03, 0x04]),
            [".txt"] = ("text/plain", null),
            [".jpg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
            [".jpeg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
            [".png"] = ("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
        };

    public FileValidationResult ValidateFile(string originalFileName, long sizeBytes, Stream content)
    {
        if (string.IsNullOrWhiteSpace(originalFileName) ||
            !string.Equals(Path.GetFileName(originalFileName), originalFileName, StringComparison.Ordinal) ||
            originalFileName.Contains('/') || originalFileName.Contains('\\'))
        {
            return InvalidFile("The file name is invalid.");
        }

        if (sizeBytes <= 0)
            return InvalidFile($"{originalFileName} is empty.");

        if (sizeBytes > MaximumFileSizeBytes)
            return InvalidFile($"{originalFileName} is larger than the 25 MB limit.");

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!FileTypes.TryGetValue(extension, out var type))
            return InvalidFile($"{originalFileName} is not a supported type. Supported: PDF, Word, Excel, PowerPoint, text, JPEG, PNG.");

        var originalPosition = content.CanSeek ? content.Position : 0;
        try
        {
            var prefix = new byte[SignatureBytesToRead];
            var bytesRead = ReadPrefix(content, prefix);
            if (!MatchesSignature(extension, type.Signature, prefix.AsSpan(0, bytesRead)))
                return InvalidFile($"{originalFileName} is not a supported type. Supported: PDF, Word, Excel, PowerPoint, text, JPEG, PNG.");

            return new FileValidationResult(true, null, type.MimeType, extension);
        }
        finally
        {
            if (content.CanSeek)
                content.Position = originalPosition;
        }
    }

    public MetadataValidationResult ValidateMetadata(
        string title,
        string? description,
        string category,
        IReadOnlyCollection<string> tags)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length == 0)
            return InvalidMetadata("A document title is required.", normalizedTitle, description, []);
        if (normalizedTitle.Length > 200)
            return InvalidMetadata("The title must be 200 characters or fewer.", normalizedTitle, description, []);
        if (!DocumentCategories.All.Contains(category, StringComparer.Ordinal))
            return InvalidMetadata("Select a valid document category.", normalizedTitle, description, []);

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription?.Length > 2000)
            return InvalidMetadata("The description must be 2000 characters or fewer.", normalizedTitle, normalizedDescription, []);

        var normalizedTags = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedTags.Length > 10)
            return InvalidMetadata("A document can have no more than 10 tags.", normalizedTitle, normalizedDescription, []);
        if (normalizedTags.Any(tag => tag.Length > 50))
            return InvalidMetadata("Tags must be 50 characters or fewer.", normalizedTitle, normalizedDescription, []);

        return new MetadataValidationResult(true, null, normalizedTitle, normalizedDescription, normalizedTags);
    }

    public bool IsPreviewable(string fileType) => fileType is "application/pdf" or "image/jpeg" or "image/png";

    private static bool MatchesSignature(string extension, byte[]? signature, ReadOnlySpan<byte> prefix)
    {
        if (extension == ".txt")
        {
            if (prefix.Contains((byte)0))
                return false;

            try
            {
                StrictUtf8.GetString(prefix);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        return signature is not null && prefix.StartsWith(signature);
    }

    private static int ReadPrefix(Stream content, byte[] buffer)
    {
        var count = 0;
        while (count < buffer.Length)
        {
            var read = content.Read(buffer, count, buffer.Length - count);
            if (read == 0)
                break;
            count += read;
        }
        return count;
    }

    private static FileValidationResult InvalidFile(string error) => new(false, error, null, null);

    private static MetadataValidationResult InvalidMetadata(string error, string title, string? description, IReadOnlyList<string> tags) =>
        new(false, error, title, description, tags);
}