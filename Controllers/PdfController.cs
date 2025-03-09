using System.Security.Claims;
using ElasticSearchPdfApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElasticSearchPdfApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        private readonly PdfProcessingService _pdfProcessingService;
        private readonly ElasticsearchService _elasticsearchService;

        private readonly ILogger<PdfController> _logger;

        public PdfController(
            PdfProcessingService pdfProcessingService,
            ElasticsearchService elasticsearchService,
            ILogger<PdfController> logger
        )
        {
            _pdfProcessingService = pdfProcessingService;
            _elasticsearchService = elasticsearchService;
            _logger = logger;
        }

        [Authorize(Roles = "Admin,Editor")]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadPdf(List<IFormFile> files, [FromQuery] string role)
        {
            _logger.LogInformation("Uploading PDF files");
            if (files == null || files.Count == 0)
            {
                _logger.LogWarning("No file uploaded");
                return BadRequest("No file uploaded");
            }

            var pdfDocuments = new List<ElasticSearchPdfApi.Models.PdfDocument>();

            foreach (var file in files)
            {
                if (
                    !file.ContentType.Equals(
                        "application/pdf",
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return BadRequest("Only PDF files are allowed");
                }

                using (var stream = file.OpenReadStream())
                {
                    var pdfDocument = await _pdfProcessingService.ProcessPdfAsync(
                        stream,
                        file.FileName,
                        role
                    );
                    pdfDocuments.Add(pdfDocument);
                }
            }
            _logger.LogInformation("PDF files uploaded successfully by {User}", User.Identity.Name);

            return Ok(pdfDocuments);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPdfs(
            [FromQuery] string query,
            [FromQuery] int? proximityDistance = null
        )
        {
            _logger.LogInformation("Searching for PDF files");
            var roles = GetRoles();
            if (string.IsNullOrWhiteSpace(query))
            {
                var allDocuments = await _elasticsearchService.GetAllDocumentsAsync(roles);
                return Ok(allDocuments);
            }
            _logger.LogInformation(
                "Searching for PDF files with query: {Query} by {User}",
                query,
                User.Identity.Name
            );
            var results = await _elasticsearchService.PdfSearchAsync(
                query,
                roles,
                proximityDistance ?? 10
            );
            _logger.LogInformation(
                "PDF files found with query: {Query} by {User}",
                query,
                User.Identity.Name
            );
            return Ok(results);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPdf(string id)
        {
            _logger.LogInformation(
                "Getting PDF file with id: {Id} by {User}",
                id,
                User.Identity.Name
            );
            var document = await _elasticsearchService.GetDocumentAsync(id);

            if (document == null)
            {
                _logger.LogWarning("PDF file with id {Id} not found", id);
                return NotFound();
            }

            if (!System.IO.File.Exists(document.FilePath))
            {
                _logger.LogWarning(
                    "PDF file with id {Id} not found on server by {User}",
                    id,
                    User.Identity.Name
                );
                return NotFound("PDF file not found on server");
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(document.FilePath);
            _logger.LogInformation(
                "PDF file with id {Id} downloaded by {User}",
                id,
                User.Identity.Name
            );
            return File(fileBytes, document.ContentType, document.FileName);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Editor")]
        public async Task<IActionResult> DeletePdf(string id)
        {
            _logger.LogInformation("Deleting PDF file with id: {Id}", id);
            var document = await _elasticsearchService.GetDocumentAsync(id);

            if (document == null)
            {
                _logger.LogWarning("PDF file with id {Id} not found", id);
                return NotFound();
            }

            var deleted = await _elasticsearchService.DeleteDocumentAsync(id);
            _logger.LogInformation(
                "PDF file with id {Id} deleted from Elasticsearch by {User}",
                id,
                User.Identity.Name
            );
            if (deleted && System.IO.File.Exists(document.FilePath))
            {
                System.IO.File.Delete(document.FilePath);
                _logger.LogInformation(
                    "PDF file with id {Id} deleted from server by {User}",
                    id,
                    User.Identity.Name
                );
            }

            return Ok(new { success = deleted });
        }

        [HttpPost("reindex")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ReindexAllDocuments()
        {
            _logger.LogInformation("Reindexing all documents");
            var success = await _elasticsearchService.ReindexAllDocumentsAsync();
            _logger.LogInformation("All documents reindexed successfully");
            return Ok(new { success });
        }

        private string[] GetRoles()
        {
            var role = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
            if (role == "Admin")
                return ["Admin", "Editor", "Internal", "User"];
            else if (role == "Editor")
                return ["Editor", "Internal", "User"];
            else if (role == "Internal")
                return ["Internal", "User"];
            else if (role == "User")
                return ["User"];

            return [role];
        }
    }
}
