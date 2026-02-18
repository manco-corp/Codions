using System.Text;

namespace Codions.ChatAdapter;

public static class HttpRequestExtensions
{
    extension(HttpRequest request)
    {
        public async Task<string> ReadRequestBodyAsStringAsync()
        {
            // Optional: Enable buffering if the stream needs to be read again later 
            // (e.g., by model binding or other middleware).
            // Note: This can impact performance if the body is large.
            request.EnableBuffering();

            // Use a StreamReader to read the stream content. 
            // The 'leaveOpen: true' argument is important to prevent the stream from being 
            // closed when the StreamReader is disposed, allowing it to be reused.
            using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            {
                var body = await reader.ReadToEndAsync();

                // Reset the stream position to the beginning so that other components 
                // (like model binding) can read it.
                request.Body.Position = 0;

                return body;
            }
        }
    }
}
