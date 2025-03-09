using ElasticSearchPdfApi.Models;
using Nest;

namespace ElasticSearchPdfApi.Services
{
    public class ElasticsearchService
    {
        private readonly IElasticClient _elasticClient;
        private readonly ILogger<ElasticsearchService> _logger;
        private const string IndexName = "pdf_documents";

        public ElasticsearchService(
            IElasticClient elasticClient,
            ILogger<ElasticsearchService> logger
        )
        {
            _elasticClient = elasticClient;
            _logger = logger;
            CreateIndexIfNotExists();
        }

        private void CreateIndexIfNotExists()
        {
            if (!_elasticClient.Indices.Exists(IndexName).Exists)
            {
                var createIndexResponse = _elasticClient.Indices.Create(
                    IndexName,
                    c =>
                        c.Settings(s =>
                                s.Analysis(a =>
                                    a.Analyzers(an =>
                                            an.Custom(
                                                "portuguese_analyzer",
                                                ca =>
                                                    ca.Tokenizer("standard")
                                                        .Filters("lowercase", "portuguese_stemmer")
                                            )
                                        )
                                        .TokenFilters(tf =>
                                            tf.Stemmer(
                                                "portuguese_stemmer",
                                                st => st.Language("portuguese")
                                            )
                                        )
                                )
                            )
                            .Map<PdfDocument>(m =>
                                m.AutoMap()
                                    .Properties(ps =>
                                        ps.Text(t =>
                                                t.Name(n => n.Content)
                                                    .Analyzer("portuguese_analyzer")
                                                    .Fields(ff =>
                                                        ff.Keyword(k =>
                                                            k.Name("keyword").Index(false)
                                                        )
                                                    )
                                            )
                                            .Text(t =>
                                                t.Name(n => n.FileName)
                                                    .Analyzer("portuguese_analyzer")
                                            )
                                    )
                            )
                );

                if (!createIndexResponse.IsValid)
                {
                    throw new Exception(
                        $"Failed to create index: {createIndexResponse.DebugInformation}"
                    );
                }
                else
                {
                    _logger.LogInformation("Index created successfully: {IndexName}", IndexName);
                }
            }
        }

        public async Task<bool> IndexDocumentAsync(PdfDocument document)
        {
            var indexResponse = await _elasticClient.IndexDocumentAsync(document);
            if (!indexResponse.IsValid)
            {
                _logger.LogError(
                    "Error indexing document: {Error}",
                    indexResponse.DebugInformation
                );
            }
            return indexResponse.IsValid;
        }

        public async Task<IEnumerable<PdfSearchResult>> PdfSearchAsync(
            string searchTerm,
            string[] roles,
            int proximityDistance = 50
        )
        {
            var searchTerms = searchTerm.Split(
                new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries
            );

            var mustQueries =
                new List<Func<QueryContainerDescriptor<PdfDocument>, QueryContainer>>();

            mustQueries.Add(s =>
                s.Match(m =>
                    m.Field(p => p.Content)
                        .Query(searchTerm)
                        .Operator(Operator.And)
                        .Fuzziness(Fuzziness.Auto)
                )
            );

            if (searchTerms.Length > 1)
            {
                foreach (var term in searchTerms)
                {
                    mustQueries.Add(s =>
                        s.Bool(b =>
                            b.Should(
                                    sh =>
                                        sh.Match(m =>
                                            m.Field(p => p.Content)
                                                .Query(term)
                                                .Fuzziness(Fuzziness.Auto)
                                        ),
                                    sh =>
                                        sh.Match(m =>
                                            m.Field(p => p.FileName)
                                                .Query(term)
                                                .Fuzziness(Fuzziness.Auto)
                                        )
                                )
                                .MinimumShouldMatch(1)
                        )
                    );
                }
            }

            var searchResponse = await _elasticClient.SearchAsync<PdfDocument>(s =>
                s.Index(IndexName)
                    .Query(q => q.Bool(b => b.Must(mustQueries)))
                    .Sort(sort => sort.Descending(SortSpecialField.Score))
                    .Highlight(h =>
                        h.PreTags("<mark>")
                            .PostTags("</mark>")
                            .Fields(
                                f => f.Field(p => p.Content).FragmentSize(300).NumberOfFragments(5),
                                f => f.Field(p => p.FileName).FragmentSize(150).NumberOfFragments(1)
                            )
                    )
                    .Size(1000)
            );

            if (!searchResponse.IsValid)
            {
                Console.WriteLine($"Search error: {searchResponse.DebugInformation}");
                return new List<PdfSearchResult>();
            }

            var results = new List<PdfSearchResult>();
            var parentDocuments = new Dictionary<string, PdfSearchResult>();

            foreach (var hit in searchResponse.Hits.Where(w => roles.Contains(w.Source.Role)))
            {
                bool isChunk = !string.IsNullOrEmpty(hit.Source.ParentId);
                string docId = isChunk ? hit.Source.ParentId : hit.Source.Id;

                if (!parentDocuments.ContainsKey(docId))
                {
                    var searchResult = new PdfSearchResult
                    {
                        Id = docId,
                        FileName = isChunk
                            ? hit.Source.FileName.Split('(')[0].Trim()
                            : hit.Source.FileName,
                        UploadDate = hit.Source.UploadDate,
                        FileSize = hit.Source.FileSize,
                        Highlights = new List<string>(),
                    };

                    parentDocuments[docId] = searchResult;
                    results.Add(searchResult);
                }

                if (hit.Highlight.ContainsKey("content"))
                {
                    parentDocuments[docId].Highlights.AddRange(hit.Highlight["content"]);
                }

                if (hit.Highlight.ContainsKey("fileName"))
                {
                    parentDocuments[docId].Highlights.AddRange(hit.Highlight["fileName"]);
                }
            }

            return results;
        }

        public async Task<bool> DeleteDocumentAsync(string id)
        {
            var deleteResponse = await _elasticClient.DeleteAsync<PdfDocument>(
                id,
                d => d.Index(IndexName)
            );
            return deleteResponse.IsValid;
        }

        public async Task<PdfDocument> GetDocumentAsync(string id)
        {
            var getResponse = await _elasticClient.GetAsync<PdfDocument>(
                id,
                g => g.Index(IndexName)
            );
            return getResponse.IsValid ? getResponse.Source : null;
        }

        public async Task<IEnumerable<PdfDocument>> GetAllDocumentsAsync(string[] roles = null)
        {
            var searchResponse = await _elasticClient.SearchAsync<PdfDocument>(s =>
                s.Index(IndexName).Size(1000).Query(q => q.MatchAll())
            );

            if (!searchResponse.IsValid)
            {
                Console.WriteLine($"Search error: {searchResponse.DebugInformation}");
                return new List<PdfDocument>();
            }

            var parentDocuments = new Dictionary<string, PdfDocument>();

            foreach (
                var document in searchResponse.Documents.Where(d =>
                    roles == null || roles.Contains(d.Role)
                )
            )
            {
                bool isChunk = !string.IsNullOrEmpty(document.ParentId);
                string docId = isChunk ? document.ParentId : document.Id;

                if (!parentDocuments.ContainsKey(docId))
                {
                    var parentDoc = new PdfDocument
                    {
                        Id = docId,
                        ParentId = null,
                        FileName = isChunk
                            ? document.FileName.Split('(')[0].Trim()
                            : document.FileName,
                        UploadDate = document.UploadDate,
                        FileSize = document.FileSize,
                        Content = document.Content,
                        Role = document.Role,
                    };

                    parentDocuments[docId] = parentDoc;
                }
                else
                {
                    parentDocuments[docId].Content += "\n" + document.Content;
                }
            }

            return parentDocuments.Values;
        }

        public async Task<bool> ReindexAllDocumentsAsync()
        {
            try
            {
                var allDocuments = await GetAllDocumentsAsync();

                if (!allDocuments.Any())
                {
                    return true;
                }

                if (_elasticClient.Indices.Exists(IndexName).Exists)
                {
                    await _elasticClient.Indices.DeleteAsync(IndexName);
                }

                var createIndexResponse = _elasticClient.Indices.Create(
                    IndexName,
                    c =>
                        c.Settings(s =>
                                s.Analysis(a =>
                                    a.Analyzers(an =>
                                            an.Custom(
                                                "portuguese_analyzer",
                                                ca =>
                                                    ca.Tokenizer("standard")
                                                        .Filters("lowercase", "portuguese_stemmer")
                                            )
                                        )
                                        .TokenFilters(tf =>
                                            tf.Stemmer(
                                                "portuguese_stemmer",
                                                st => st.Language("portuguese")
                                            )
                                        )
                                )
                            )
                            .Map<PdfDocument>(m =>
                                m.Properties(ps =>
                                    ps.Text(t =>
                                            t.Name(n => n.FileName).Analyzer("portuguese_analyzer")
                                        )
                                        .Text(t =>
                                            t.Name(n => n.Content)
                                                .Analyzer("portuguese_analyzer")
                                                .Fields(f =>
                                                    f.Keyword(k => k.Name("keyword").Index(false))
                                                )
                                        )
                                )
                            )
                );

                if (!createIndexResponse.IsValid)
                {
                    throw new Exception(
                        $"Failed to create index: {createIndexResponse.DebugInformation}"
                    );
                }

                const int maxContentLength = 30000;

                foreach (var document in allDocuments)
                {
                    if (document.Content == null || document.Content.Length <= maxContentLength)
                    {
                        var indexResponse = await _elasticClient.IndexDocumentAsync(document);
                        if (!indexResponse.IsValid)
                        {
                            Console.WriteLine(
                                $"Failed to reindex document {document.Id}: {indexResponse.DebugInformation}"
                            );
                            continue;
                        }
                    }
                    else
                    {
                        string originalId = document.Id;
                        int chunkCount = (int)
                            Math.Ceiling((double)document.Content.Length / maxContentLength);

                        var parentDoc = new PdfDocument
                        {
                            Id = originalId,
                            FileName = document.FileName,
                            FilePath = document.FilePath,
                            Content = $"[Large document split into {chunkCount} chunks]",
                            FileSize = document.FileSize,
                            UploadDate = document.UploadDate,
                            Role = document.Role,
                        };

                        var parentResponse = await _elasticClient.IndexDocumentAsync(parentDoc);
                        if (!parentResponse.IsValid)
                        {
                            Console.WriteLine(
                                $"Failed to index parent document {parentDoc.Id}: {parentResponse.DebugInformation}"
                            );
                            continue;
                        }

                        for (int i = 0; i < document.Content.Length; i += maxContentLength)
                        {
                            int length = Math.Min(maxContentLength, document.Content.Length - i);
                            string chunk = document.Content.Substring(i, length);

                            var chunkDoc = new PdfDocument
                            {
                                Id = $"{originalId}_chunk_{i / maxContentLength}",
                                FileName =
                                    $"{document.FileName} (part {i / maxContentLength + 1} of {chunkCount})",
                                FilePath = document.FilePath,
                                Content = chunk,
                                FileSize = document.FileSize,
                                UploadDate = document.UploadDate,
                                Role = document.Role,
                                ParentId = originalId,
                            };

                            var chunkResponse = await _elasticClient.IndexDocumentAsync(chunkDoc);
                            if (!chunkResponse.IsValid)
                            {
                                Console.WriteLine(
                                    $"Failed to index chunk {chunkDoc.Id}: {chunkResponse.DebugInformation}"
                                );
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reindexing documents: {ex.Message}");
                return false;
            }
        }
    }
}
