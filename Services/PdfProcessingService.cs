using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace ElasticSearchPdfApi.Services
{
    public class PdfProcessingService
    {
        private readonly string _uploadDirectory;
        private readonly ElasticsearchService _elasticsearchService;

        public PdfProcessingService(
            string uploadDirectory,
            ElasticsearchService elasticsearchService
        )
        {
            _uploadDirectory = uploadDirectory;
            _elasticsearchService = elasticsearchService;

            if (!Directory.Exists(_uploadDirectory))
            {
                Directory.CreateDirectory(_uploadDirectory);
            }
        }

        public async Task<ElasticSearchPdfApi.Models.PdfDocument> ProcessPdfAsync(
            Stream fileStream,
            string fileName,
            string role
        )
        {
            string uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
            string filePath = Path.Combine(_uploadDirectory, uniqueFileName);
            using (var fileStreamWriter = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(fileStreamWriter);
            }

            string pdfText = ExtractTextFromPdf(filePath);
            var docId = Guid.NewGuid().ToString();

            // If content is very large, split into chunks and index separately
            if (pdfText.Length > 30000)
            {
                const int chunkSize = 30000;
                int chunkCount = (int)Math.Ceiling((double)pdfText.Length / chunkSize);

                // Create a parent document with metadata (no content)
                var parentDocument = new ElasticSearchPdfApi.Models.PdfDocument
                {
                    Id = docId,
                    FileName = fileName,
                    FilePath = filePath,
                    Content = $"[Large document split into {chunkCount} chunks]",
                    FileSize = new FileInfo(filePath).Length,
                    UploadDate = DateTime.UtcNow,
                    Role = role ?? "User",
                    IsParent = true,
                };

                await _elasticsearchService.IndexDocumentAsync(parentDocument);

                // Index each chunk
                for (int i = 0; i < pdfText.Length; i += chunkSize)
                {
                    int length = Math.Min(chunkSize, pdfText.Length - i);
                    string chunk = pdfText.Substring(i, length);

                    var chunkDoc = new ElasticSearchPdfApi.Models.PdfDocument
                    {
                        Id = $"{docId}_chunk_{i / chunkSize}",
                        FileName = $"{fileName} (chunk {i / chunkSize + 1} of {chunkCount})",
                        FilePath = filePath,
                        Content = chunk,
                        FileSize = new FileInfo(filePath).Length,
                        UploadDate = DateTime.UtcNow,
                        Role = role ?? "User",
                        ParentId = docId,
                        ChunkIndex = i / chunkSize,
                        TotalChunks = chunkCount,
                    };

                    await _elasticsearchService.IndexDocumentAsync(chunkDoc);
                }

                return parentDocument;
            }

            // For smaller documents, index normally
            var pdfDocument = new ElasticSearchPdfApi.Models.PdfDocument
            {
                Id = docId,
                FileName = fileName,
                FilePath = filePath,
                Content = pdfText,
                FileSize = new FileInfo(filePath).Length,
                UploadDate = DateTime.UtcNow,
                Role = role ?? "User",
            };

            await _elasticsearchService.IndexDocumentAsync(pdfDocument);
            return pdfDocument;
        }

        private string ExtractTextFromPdf(string filePath)
        {
            try
            {
                var text = new System.Text.StringBuilder();

                using (var pdfReader = new PdfReader(filePath))
                {
                    using (var pdfDocument = new iText.Kernel.Pdf.PdfDocument(pdfReader))
                    {
                        for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
                        {
                            var strategy = new SimpleTextExtractionStrategy();
                            string pageText = PdfTextExtractor.GetTextFromPage(
                                pdfDocument.GetPage(i),
                                strategy
                            );
                            text.AppendLine(pageText);
                        }
                    }
                }

                return text.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
