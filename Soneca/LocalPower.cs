using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Soneca;

internal enum PowerCommand { Shutdown, Restart, Suspend }

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
        bool suspend = command == PowerCommand.Suspend;
        string executable = suspend ? Path.Combine(AppContext.BaseDirectory, "Soneca.exe")
            : Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        using var process = Process.Start(new ProcessStartInfo(
            executable, suspend ? WindowsSuspend.Argument : Arguments(command))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        return process is not null && (suspend || (process.WaitForExit(2500) && process.ExitCode == 0));
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
            if (kind == "suspend")
            {
                context.Response.OnCompleted(async () =>
                {
                    await Task.Delay(400).ConfigureAwait(false);
                    try { lock (Gate) perform(PowerCommand.Suspend); }
                    catch (Exception error) { Debug.WriteLine("Suspensão: " + error.Message); }
                });
                return Results.Json(new { accepted = true });
            }
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
        app.MapPost("/v1/energy/shutdown/{mode}", (HttpContext context, string mode) => SchedulePower(context, mode, suspend: false));
        app.MapPost("/v1/energy/suspend/{mode}", (HttpContext context, string mode) => SchedulePower(context, mode, suspend: true));

        IResult SchedulePower(HttpContext context, string mode, bool suspend)
        {
            if (!allowed(context)) return Results.Unauthorized();
            var schedule = suspend ? Schedule.ForSuspend() : new Schedule();
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
                        case "weekly" when !suspend:
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
        }

        IResult State()
        {
            try
            {
                lock (Gate)
                {
                    DateTimeOffset? scheduled = new Schedule().Read();
                    DateTimeOffset? suspension = Schedule.ForSuspend().Read();
                    return Results.Json(new
                    {
                        name = Environment.MachineName, address = adapter.Address.ToString(), adapter.Mac, adapter.Prefix,
                        shutdownAt = scheduled > DateTimeOffset.Now ? scheduled : null,
                        suspendControl = true,
                        suspendAt = suspension > DateTimeOffset.Now ? suspension : null,
                        shutdownWeekly = PcStore.Read().LocalShutdown
                    });
                }
            }
            catch (Exception) { return Results.BadRequest(new { error = "O Windows não conseguiu consultar os agendamentos." }); }
        }
    }
}
