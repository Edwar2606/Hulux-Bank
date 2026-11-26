
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HuluxApi
{
    public class FreebaseService
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private readonly string _namespace;
        private readonly string _apiKey;

        public FreebaseService(
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _httpClient = httpClient;

            _endpoint = configuration["Freebase:Endpoint"]
                ?? throw new InvalidOperationException(
                    "Falta configurar el endpoint de Freebase");

            _namespace = configuration["Freebase:Namespace"]
                ?? throw new InvalidOperationException(
                    "Falta configurar el namespace de Freebase");

            _apiKey = Environment.GetEnvironmentVariable(
                "FREEBASE_API_KEY")
                ?? throw new InvalidOperationException(
                    "No se encontró la variable FREEBASE_API_KEY");
        }

        public async Task<JsonDocument> QueryAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            var body = JsonSerializer.Serialize(new
            {
                @namespace = _namespace,
                protocol = "mysql",
                query = query
            });

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _endpoint);

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);

            request.Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(
                cancellationToken);

            var result = JsonDocument.Parse(content);

            if (result.RootElement.TryGetProperty(
                "error", out var error)
                && error.ValueKind != JsonValueKind.Null)
            {
                var message = error.GetString();

                result.Dispose();

                throw new InvalidOperationException(
                    $"Error de Freebase: {message}");
            }

            return result;
        }
    }
}
