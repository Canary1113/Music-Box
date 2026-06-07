using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MusicBox.Services
{
    public sealed class RasterPdfExportService
    {
        private const float A4WidthPoints = 595.276f;
        private const float A4HeightPoints = 841.89f;

        public void ExportJpegPages(string path, IReadOnlyList<RasterPdfPage> pages)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("PDF export path is required.", nameof(path));
            }

            if (pages == null || pages.Count == 0)
            {
                throw new InvalidOperationException("No rendered pages are available for PDF export.");
            }

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            WritePdf(stream, pages);
        }

        private static void WritePdf(Stream output, IReadOnlyList<RasterPdfPage> pages)
        {
            using var body = new MemoryStream();
            WriteAscii(body, "%PDF-1.4\n");

            int pageCount = pages.Count;
            int totalObjects = 2 + pageCount * 3;
            var offsets = new long[totalObjects + 1];
            int currentObject = 1;

            offsets[currentObject] = body.Position;
            WriteObject(body, currentObject++, "<< /Type /Catalog /Pages 2 0 R >>");

            var kids = new StringBuilder();
            for (int i = 0; i < pageCount; i++)
            {
                int pageObjectId = 3 + i * 3;
                kids.Append(pageObjectId.ToString(CultureInfo.InvariantCulture)).Append(" 0 R ");
            }

            offsets[currentObject] = body.Position;
            WriteObject(body, currentObject++, $"<< /Type /Pages /Kids [{kids.ToString().TrimEnd()}] /Count {pageCount} >>");

            for (int index = 0; index < pageCount; index++)
            {
                RasterPdfPage page = pages[index];
                int pageObjectId = 3 + index * 3;
                int imageObjectId = pageObjectId + 1;
                int contentObjectId = pageObjectId + 2;

                float scale = Math.Min(
                    A4WidthPoints / Math.Max(1f, page.PixelWidth),
                    A4HeightPoints / Math.Max(1f, page.PixelHeight));
                float drawWidth = Math.Max(1f, page.PixelWidth * scale);
                float drawHeight = Math.Max(1f, page.PixelHeight * scale);
                float offsetX = (A4WidthPoints - drawWidth) * 0.5f;
                float offsetY = (A4HeightPoints - drawHeight) * 0.5f;

                string contentStream = string.Create(
                    CultureInfo.InvariantCulture,
                    $"q {drawWidth:0.###} 0 0 {drawHeight:0.###} {offsetX:0.###} {offsetY:0.###} cm /Im{index + 1} Do Q");

                offsets[pageObjectId] = body.Position;
                WriteObject(
                    body,
                    pageObjectId,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {A4WidthPoints:0.###} {A4HeightPoints:0.###}] /Resources << /XObject << /Im{index + 1} {imageObjectId} 0 R >> >> /Contents {contentObjectId} 0 R >>"));

                offsets[imageObjectId] = body.Position;
                WriteStreamObject(
                    body,
                    imageObjectId,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"<< /Type /XObject /Subtype /Image /Width {Math.Max(1, page.PixelWidth)} /Height {Math.Max(1, page.PixelHeight)} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {page.JpegBytes.Length} >>"),
                    page.JpegBytes);

                offsets[contentObjectId] = body.Position;
                WriteStreamObject(body, contentObjectId, $"<< /Length {Encoding.ASCII.GetByteCount(contentStream)} >>", Encoding.ASCII.GetBytes(contentStream));
            }

            long xrefOffset = body.Position;
            WriteAscii(body, $"xref\n0 {totalObjects + 1}\n");
            WriteAscii(body, "0000000000 65535 f \n");
            for (int i = 1; i <= totalObjects; i++)
            {
                WriteAscii(body, $"{offsets[i]:0000000000} 00000 n \n");
            }

            WriteAscii(body, $"trailer\n<< /Size {totalObjects + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
            body.Position = 0;
            body.CopyTo(output);
        }

        private static void WriteObject(Stream stream, int objectId, string body)
        {
            WriteAscii(stream, $"{objectId} 0 obj\n{body}\nendobj\n");
        }

        private static void WriteStreamObject(Stream stream, int objectId, string dictionary, byte[] bytes)
        {
            WriteAscii(stream, $"{objectId} 0 obj\n{dictionary}\nstream\n");
            stream.Write(bytes, 0, bytes.Length);
            WriteAscii(stream, "\nendstream\nendobj\n");
        }

        private static void WriteAscii(Stream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    public sealed record RasterPdfPage(byte[] JpegBytes, int PixelWidth, int PixelHeight);
}
