using System.Text;
using ContosoDashboard.Models;
using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.Services;

public class DocumentValidatorTests
{
    private readonly DocumentValidator _validator = new();

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public void ValidateFile_AcceptsAllowedExtensionAndSignature(string fileName, byte[] signature)
    {
        var result = _validator.ValidateFile(fileName, signature.LongLength, new MemoryStream(signature));

        Assert.True(result.IsValid, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.FileType));
    }

    public static IEnumerable<object[]> SupportedFiles()
    {
        yield return ["report.pdf", Encoding.ASCII.GetBytes("%PDF-")];
        yield return ["photo.jpg", new byte[] { 0xFF, 0xD8, 0xFF }];
        yield return ["photo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }];
        yield return ["doc.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }];
        yield return ["sheet.xlsx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }];
        yield return ["slides.pptx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }];
        yield return ["doc.doc", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }];
        yield return ["sheet.xls", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }];
        yield return ["slides.ppt", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }];
        yield return ["notes.txt", Encoding.UTF8.GetBytes("plain text")];
    }

    [Fact]
    public void ValidateFile_RejectsUnsupportedEmptyOversizedAndMismatchedFiles()
    {
        Assert.False(_validator.ValidateFile("run.exe", 1, new MemoryStream([1])).IsValid);
        Assert.Contains("empty", _validator.ValidateFile("empty.pdf", 0, Stream.Null).Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("25 MB", _validator.ValidateFile("large.pdf", 26_214_401, Stream.Null).Error!);
        Assert.False(_validator.ValidateFile("text.pdf", 4, new MemoryStream(Encoding.UTF8.GetBytes("text"))).IsValid);
        Assert.False(_validator.ValidateFile("binary.txt", 3, new MemoryStream([0x41, 0x00, 0x42])).IsValid);
        Assert.False(_validator.ValidateFile("invalid.txt", 2, new MemoryStream([0xC3, 0x28])).IsValid);
    }

    [Fact]
    public void ValidateFile_AcceptsExactly25MbAndReturnsServerMimeType()
    {
        var bytes = new byte[26_214_400];
        Encoding.ASCII.GetBytes("%PDF-").CopyTo(bytes, 0);

        var result = _validator.ValidateFile("report.pdf", bytes.LongLength, new MemoryStream(bytes));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("application/pdf", result.FileType);
    }

    [Fact]
    public void ValidateMetadata_TrimsTitleAndNormalizesDistinctTags()
    {
        var result = _validator.ValidateMetadata("  Quarterly report  ", null, "Reports", [" Blue ", "blue", "Finance"]);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("Quarterly report", result.Title);
        Assert.Equal(new[] { "blue", "finance" }, result.Tags);
        Assert.False(_validator.ValidateMetadata(new string('x', 201), null, "Reports", []).IsValid);
        Assert.False(_validator.ValidateMetadata("Title", null, "Reports", Enumerable.Range(0, 11).Select(i => $"tag{i}").ToArray()).IsValid);
    }

    [Fact]
    public void ValidateMetadata_RejectsMissingTitleAndInvalidCategory()
    {
        Assert.False(_validator.ValidateMetadata(" ", null, "Reports", []).IsValid);
        Assert.False(_validator.ValidateMetadata("Title", null, "Unknown", []).IsValid);
        Assert.Contains("Personal Files", string.Join(" ", DocumentCategories.All));
    }
}