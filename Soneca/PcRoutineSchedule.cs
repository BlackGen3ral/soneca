using System.Globalization;
using System.IO;
using System.Security.Principal;

namespace Soneca;

internal static class PcRoutineSchedule
{
    private static string Name => "ArthoAI-PcRoutines-" + WindowsIdentity.GetCurrent().User!.Value;

    public static void Register(IEnumerable<PairedPc> computers, bool harmless = false, WeeklyRule? localShutdown = null)
    {
        var rules = computers.SelectMany(pc => new[] { pc.Wake, pc.Shutdown }).Append(localShutdown).OfType<WeeklyRule>()
            .GroupBy(rule => rule.Time).Select(group => new WeeklyRule(group.Key, group.Aggregate(0, (days, rule) => days | rule.Days)))
            .ToArray();
        string name = harmless ? Name + "-Test" : Name;
        if (rules.Length == 0)
        {
            Remove(harmless);
            return;
        }

        dynamic service = Connect();
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = "Rotinas semanais de energia dos PCs pareados no Artho AI.";
        task.Principal.LogonType = 3;
        task.Principal.RunLevel = 0;
        task.Settings.StartWhenAvailable = false;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.WakeToRun = false;
        task.Settings.ExecutionTimeLimit = "PT2M";
        task.Settings.MultipleInstances = 2;
        foreach (var rule in rules)
        {
            dynamic trigger = task.Triggers.Create(3);
            trigger.StartBoundary = NextOccurrence(rule, DateTime.Now).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            trigger.DaysOfWeek = (short)rule.Days;
            trigger.WeeksInterval = (short)1;
        }
        dynamic action = task.Actions.Create(0);
        action.Path = harmless ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : Environment.ProcessPath!;
        action.Arguments = harmless ? "/c exit 0" : "--run-pc-routines";
        service.GetFolder("\\").RegisterTaskDefinition(name, task, 6, null, null, 3, null);
    }

    public static void Remove(bool harmless = false)
    {
        try { Connect().GetFolder("\\").DeleteTask(harmless ? Name + "-Test" : Name, 0); }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070002) { }
    }

    internal static DateTime NextOccurrence(WeeklyRule rule, DateTime now)
    {
        for (int offset = 0; offset <= 14; offset++)
        {
            DateTime candidate = now.Date.AddDays(offset).Add(rule.Clock.ToTimeSpan());
            if (candidate > now && rule.Includes(candidate.DayOfWeek) &&
                !TimeZoneInfo.Local.IsInvalidTime(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified))) return candidate;
        }
        throw new InvalidOperationException("Não foi possível calcular o próximo horário.");
    }

    internal static void IntegrationTest(PairedPc sample)
    {
        try
        {
            Register(new[] { sample }, harmless: true, localShutdown: new WeeklyRule("18:30", 62));
            dynamic definition = Connect().GetFolder("\\").GetTask(Name + "-Test").Definition;
            if ((short)definition.Triggers.Item(1).DaysOfWeek != sample.Wake!.Days ||
                (short)definition.Triggers.Item(2).DaysOfWeek != 62 ||
                (string)definition.Actions.Item(1).Arguments != "/c exit 0")
                throw new InvalidOperationException("A tarefa de teste não preservou os dias e a ação inofensiva.");
        }
        finally { Remove(harmless: true); }
    }

    private static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        return service;
    }
}
