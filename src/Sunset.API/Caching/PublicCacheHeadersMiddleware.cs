using System.Security.Cryptography;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Net.Http.Headers;

namespace Sunset.API.Caching;

/// <summary>
/// For the cacheable public GETs: adds <c>Cache-Control</c> and an <c>ETag</c> (and answers <c>304</c> to a matching
/// <c>If-None-Match</c>). Sits INSIDE the output cache, so a response is stored with these headers and a cache hit
/// replays them; it only ever runs on a miss.
/// </summary>
public sealed class PublicCacheHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var cacheable = HttpMethods.IsGet(context.Request.Method)
            && context.GetEndpoint()?.Metadata.GetMetadata<OutputCacheAttribute>() is not null
            // Same rule as the output cache itself: nothing user-specific is ever shared.
            && string.IsNullOrEmpty(context.Request.Headers.Authorization);

        if (!cacheable)
        {
            await next(context);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = original;
        }

        if (context.Response.StatusCode == StatusCodes.Status200OK && buffer.Length > 0)
        {
            var etag = $"W/\"{Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))[..32].ToLowerInvariant()}\"";
            context.Response.Headers[HeaderNames.CacheControl] = PublicCaching.CacheControl;
            context.Response.Headers[HeaderNames.ETag] = etag;

            if (context.Request.Headers.IfNoneMatch.Any(v => v is not null && v.Split(',').Select(t => t.Trim()).Contains(etag)))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = null;
                return;
            }
        }

        buffer.Position = 0;
        context.Response.ContentLength = buffer.Length;
        await buffer.CopyToAsync(original, context.RequestAborted);
    }
}
