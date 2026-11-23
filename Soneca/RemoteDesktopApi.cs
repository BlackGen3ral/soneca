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
            bool multiMonitor = context.Request.Query["monitors"] == "1";
            return desktop.Connect(multiMonitor) is { } id ? Results.Json(new { session = id }) : Unavailable();
        });

        app.MapGet("/v1/desktop/{session:guid}/frame", (HttpContext context, Guid session) =>
        {
            if (!authorized(context)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var profile = DesktopFrameProfile.FromQuery(context.Request.Query["quality"].ToString());
                if (desktop.Read(session, profile) is not { } frame) return Results.StatusCode(StatusCodes.Status410Gone);
                context.Response.Headers["X-Artho-Pointer-X"] = frame.PointerX.ToString();
                context.Response.Headers["X-Artho-Pointer-Y"] = frame.PointerY.ToString();
                if (frame.Monitor is not null)
                {
                    context.Response.Headers["X-Artho-Monitor"] = frame.Monitor;
                    context.Response.Headers["X-Artho-Monitor-Number"] = frame.MonitorNumber.ToString();
                    context.Response.Headers["X-Artho-Monitor-Count"] = frame.MonitorCount.ToString();
                }
                return Results.Bytes(frame.Image, "image/jpeg");
            }
            catch (IOException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
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

    private static IResult Unavailable() => Results.BadRequest(new
    {
        error = "Confira se a tela remota está permitida na aba Celular e se o Windows está desbloqueado."
    });
}
