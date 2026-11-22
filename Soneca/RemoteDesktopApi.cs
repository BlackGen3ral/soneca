using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Soneca;

internal static class RemoteDesktopApi
{
    public static void Map(WebApplication app, Func<HttpContext, bool> authorized, RemoteDesktopSession desktop)
    {
        app.MapPost("/v1/desktop/open", (HttpContext context) =>
        {
            if (!authorized(context)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            return desktop.Connect() is { } id ? Results.Json(new { session = id }) : Unavailable();
        });

        app.MapGet("/v1/desktop/{session:guid}/frame", (HttpContext context, Guid session) =>
        {
            if (!authorized(context)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                if (desktop.Read(session) is not { } frame) return Unavailable();
                context.Response.Headers["X-Artho-Pointer-X"] = frame.PointerX.ToString();
                context.Response.Headers["X-Artho-Pointer-Y"] = frame.PointerY.ToString();
                return Results.Bytes(frame.Image, "image/jpeg");
            }
            catch (IOException) { return Unavailable(); }
        });

        app.MapPost("/v1/desktop/{session:guid}/input", async (HttpContext context, Guid session) =>
        {
            if (!authorized(context)) return Results.Unauthorized();
            if (context.Request.ContentLength is not (> 0 and <= 4096) ||
                context.Features.Get<IHttpMaxRequestBodySizeFeature>() is not { IsReadOnly: false } limit)
                return Results.BadRequest(new { error = "Comando de entrada inválido." });
            try
            {
                limit.MaxRequestBodySize = 4096;
                var input = await context.Request.ReadFromJsonAsync<DesktopInput>(context.RequestAborted);
                return input is { Valid: true } && desktop.Input(session, input)
                    ? Results.Json(new { accepted = true }) : Unavailable();
            }
            catch (Exception ex) when (ex is JsonException or BadHttpRequestException or InvalidOperationException)
            { return Results.BadRequest(new { error = "Comando de entrada inválido." }); }
            catch (IOException) { return Unavailable(); }
        });

        app.MapPost("/v1/desktop/{session:guid}/close", (HttpContext context, Guid session) =>
        {
            if (!authorized(context)) return Results.Unauthorized();
            desktop.Disconnect(session);
            return Results.Json(new { closed = true });
        });
    }

    private static IResult Unavailable() => Results.BadRequest(new { error = "No PC, abra Celular e libere o acesso à tela." });
}
