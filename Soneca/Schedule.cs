using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Soneca;
public static class TimeRules
{
    public static DateTimeOffset After(string minutes, DateTimeOffset now)
    {
        if (!int.TryParse(minutes, out int n) || n < 1 || n > 10080)
            throw new ArgumentException("Digite um tempo entre 1 e 10080 minutos (7 dias).");
        return now.AddMinutes(n);
    }
    public static DateTimeOffset At(string clock, DateTimeOffset now)
    {
        if (!TimeOnly.TryParseExact(clock.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            throw new ArgumentException("Digite o horário no formato 24 horas, como 23:30.");
        var local = now.LocalDateTime.Date.Add(t.ToTimeSpan());
        if (local <= now.LocalDateTime) local = local.AddDays(1);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) throw new ArgumentException("Este horário não existe devido à mudança de horário do Windows.");
        return new DateTimeOffset(local);
    }
    public static string Countdown(DateTimeOffset target, DateTimeOffset now)
    {
        var left = target - now;
        if (left <= TimeSpan.Zero) return "00:00:00";
        left = TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));
        return $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
    }
}
public sealed class Schedule
{
    private readonly bool demo;
    private DateTimeOffset? simulated;
    private string Name => "Soneca-" + WindowsIdentity.GetCurrent().User!.Value;
    public Schedule(bool demo = false) => this.demo = demo;
    private static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect(); return service;
    }
    public DateTimeOffset? Read(bool harmlessTest = false)
    {
        if (demo) return simulated;
        dynamic root = Connect().GetFolder("\\");
        try
        {
            dynamic task = root.GetTask(harmlessTest ? Name + "-Test" : Name);
            string boundary = task.Definition.Triggers.Item(1).StartBoundary;
            return DateTimeOffset.Parse(boundary, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070002) { return null; }
    }
    public void Set(DateTimeOffset target, bool harmlessTest = false)
    {
        if (target < DateTimeOffset.Now.AddSeconds(30)) throw new ArgumentException("Escolha pelo menos 1 minuto a partir de agora.");
        if (demo) { simulated = target; return; }
        dynamic service = Connect();
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = "Desligamento programado pelo Artho Control. Cancele pelo aplicativo.";
        task.Principal.LogonType = 3; task.Principal.RunLevel = 0;
        task.Settings.Enabled = true;
        task.Settings.StartWhenAvailable = false;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.WakeToRun = true;
        task.Settings.ExecutionTimeLimit = "PT5M";
        dynamic trigger = task.Triggers.Create(1);
        trigger.StartBoundary = target.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        trigger.EndBoundary = target.AddMinutes(2).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        dynamic action = task.Actions.Create(0);
        action.Path = System.IO.Path.Combine(Environment.SystemDirectory, harmlessTest ? "cmd.exe" : "shutdown.exe");
        action.Arguments = harmlessTest ? "/c exit 0" : "/s /t 0";
        service.GetFolder("\\").RegisterTaskDefinition(harmlessTest ? Name + "-Test" : Name, task, 6, null, null, 3, null);
    }
    public void Cancel(bool harmlessTest = false)
    {
        if (demo) { simulated = null; return; }
        try { Connect().GetFolder("\\").DeleteTask(harmlessTest ? Name + "-Test" : Name, 0); }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070002) { }
    }
    public static void IntegrationTest()
    {
        var schedule = new Schedule();
        try
        {
            schedule.Cancel(true);
            if (schedule.Read(true) is not null) throw new Exception("Tarefa ausente deveria retornar vazio.");
            var target = DateTimeOffset.Now.AddHours(4);
            schedule.Set(target, true);
            if (Math.Abs((schedule.Read(true)!.Value - target).TotalSeconds) > 1) throw new Exception("Horário não foi persistido.");
            dynamic task = Connect().GetFolder("\\").GetTask(schedule.Name + "-Test");
            if ((string)task.Definition.Actions.Item(1).Arguments != "/c exit 0") throw new Exception("Ação de teste inválida.");
        }
        finally { schedule.Cancel(true); }
        if (schedule.Read(true) is not null) throw new Exception("Tarefa não foi cancelada.");
    }
}
