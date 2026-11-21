using System.IO;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Soneca;

internal static class MobileDeviceApi
{
    public static void Map(WebApplication app, Func<HttpContext, bool> authorized, IPAddress address)
    {
        var network = PcLan.Adapters().First(entry => entry.Address.Equals(address));
        bool Allowed(HttpContext context) => context.Connection.RemoteIpAddress is { } source &&
            PcLan.SameSubnet(network, source) && authorized(context);

        app.MapGet("/v1/devices", async (HttpContext context) =>
        {
            if (!Allowed(context)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            try { return Results.Json(new { devices = await PcDeviceService.ListAsync().ConfigureAwait(false) }); }
            catch (IOException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPost("/v1/devices/{id:guid}/wake", async (HttpContext context, Guid id) =>
        {
            if (!Allowed(context)) return Results.Unauthorized();
            try { await PcDeviceService.WakeAsync(id).ConfigureAwait(false); return Results.Json(new { sent = true }); }
            catch (Exception) { return Results.BadRequest(new { error = "Não foi possível enviar o sinal para esse PC. Confira o pareamento e a rede." }); }
        });
        app.MapPost("/v1/devices/{id:guid}/routine", (HttpContext context, Guid id) =>
        {
            if (!Allowed(context)) return Results.Unauthorized();
            try
            {
                var query = context.Request.Query;
                if (!int.TryParse(query["wakeDays"], out int wakeDays) ||
                    !int.TryParse(query["shutdownDays"], out int shutdownDays))
                    return Results.BadRequest(new { error = "Informe os dias da semana de cada rotina." });
                PcDeviceService.Save(id, WeeklyRule.Create(query["wakeTime"].ToString(), wakeDays),
                    WeeklyRule.Create(query["shutdownTime"].ToString(), shutdownDays));
                return Results.Json(new { saved = true });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception) { return Results.BadRequest(new { error = "O Windows não conseguiu salvar os horários." }); }
        });
    }
}
