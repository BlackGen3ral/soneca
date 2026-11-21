using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Soneca;

internal enum PowerCommand { Shutdown, Restart }

internal static class LocalPower
{
    private static readonly object Gate = new();

    internal static string Arguments(PowerCommand command) => command switch
    {
        PowerCommand.Shutdown => "/s /t 0",
        PowerCommand.Restart => "/r /t 0",
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    public static bool Run(PowerCommand command)
    {
        using var process = Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "shutdown.exe"), Arguments(command))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        return process is not null && process.WaitForExit(2500) && process.ExitCode == 0;
    }

    public static void Map(WebApplication app, Func<HttpContext, bool> allowed, LanAdapter adapter,
        Action? scheduleChanged, Func<PowerCommand, bool>? perform = null)
    {
        perform ??= Run;
        app.MapGet("/v1/energy", (HttpContext context) =>
        {
            if (!allowed(context)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            return State();
        });
        app.MapPost("/v1/energy/now/{kind}", (HttpContext context, string kind) =>
        {
            if (!allowed(context)) return Results.Unauthorized();
            if (kind is not ("shutdown" or "restart")) return Results.BadRequest(new { error = "Ação de energia inválida." });
            var command = kind == "restart" ? PowerCommand.Restart : PowerCommand.Shutdown;
            try
            {
                lock (Gate)
                    return perform(command) ? Results.Json(new { accepted = true })
                        : Results.BadRequest(new { error = "O Windows recusou a ação de energia." });
            }
            catch (Exception) { return Results.BadRequest(new { error = "O Windows não conseguiu executar a ação." }); }
        });
        app.MapPost("/v1/energy/shutdown/{mode}", (HttpContext context, string mode) =>
        {
            if (!allowed(context)) return Results.Unauthorized();
            var schedule = new Schedule();
            try
            {
                lock (Gate)
                {
                    var value = context.Request.Query["value"].ToString();
                    switch (mode)
                    {
                        case "cancel": schedule.Cancel(); break;
                        case "after": schedule.Set(TimeRules.After(value, DateTimeOffset.Now)); break;
                        case "at": schedule.Set(TimeRules.At(value, DateTimeOffset.Now)); break;
                        case "weekly":
                            if (!int.TryParse(context.Request.Query["days"], out int days))
                                return Results.BadRequest(new { error = "Marque os dias da semana." });
                            var rule = WeeklyRule.Create(value, days);
                            PcStore.Update(state => state.LocalShutdown = rule, syncSchedules: true);
                            break;
                        default: return Results.BadRequest(new { error = "Modo de agendamento inválido." });
                    }
                }
                scheduleChanged?.Invoke();
                return State();
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception) { return Results.BadRequest(new { error = "O Windows não conseguiu alterar o agendamento." }); }
        });

        IResult State()
        {
            try
            {
                lock (Gate)
                {
                    DateTimeOffset? scheduled = new Schedule().Read();
                    return Results.Json(new
                    {
                        name = Environment.MachineName, address = adapter.Address.ToString(), adapter.Mac, adapter.Prefix,
                        shutdownAt = scheduled > DateTimeOffset.Now ? scheduled : null,
                        shutdownWeekly = PcStore.Read().LocalShutdown
                    });
                }
            }
            catch (Exception) { return Results.BadRequest(new { error = "O Windows não conseguiu consultar os agendamentos." }); }
        }
    }
}
