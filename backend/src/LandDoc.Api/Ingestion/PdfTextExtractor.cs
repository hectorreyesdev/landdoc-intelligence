using System.Text;
using UglyToad.PdfPig;

namespace LandDoc.Api.Ingestion;

/// <summary>
/// Extracts text from a text-based (digital) PDF using PdfPig, concatenating page text in page order.
/// No OCR — scanned/handwritten documents are a PRD non-goal and out of scope.
/// </summary>
public sealed class PdfTextExtractor
{
    public string Extract(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var document = PdfDocument.Open(pdfBytes);

        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }
}
