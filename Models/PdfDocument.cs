using System;
using System.Collections.Generic;

namespace ElasticSearchPdfApi.Models
{
    public class PdfDocument
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UploadDate { get; set; } = DateTime.UtcNow;
        public string ContentType { get; set; } = "application/pdf";
        public string Role { get; set; }

        public string ParentId { get; set; }
        public bool IsParent { get; set; }
        public int ChunkIndex { get; set; }
        public int TotalChunks { get; set; }
    }

    public class PdfSearchResult
    {
        public string Id { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public List<string> Highlights { get; set; } = new List<string>();
        public DateTime UploadDate { get; set; }
        public long FileSize { get; set; }
        public string Role { get; set; }
    }
}
