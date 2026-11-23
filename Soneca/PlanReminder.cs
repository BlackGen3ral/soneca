using System.Net.Mail;
using System.Text.Json;
using Microsoft.Win32;

namespace Soneca;

internal sealed record PlanReminder(Guid Id, string Email, DateOnly Renewal, int LeadDays,
    bool WarnedBefore = false, bool WarnedOnDay = false)
{
    public int DaysLeft(DateOnly today) => Renewal.DayNumber - today.DayNumber;

    public static PlanReminder Create(string email, DateOnly renewal, int leadDays, PlanReminder? existing)
    {
        email = email.Trim();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) ||
            !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Digite um endereço de e-mail válido.");
        if (leadDays is < 1 or > 60) throw new ArgumentException("Escolha uma antecedência de 1 a 60 dias.");
        bool sameDate = existing?.Renewal == renewal;
        return new PlanReminder(existing?.Id ?? Guid.NewGuid(), email, renewal, leadDays,
            sameDate && existing!.WarnedBefore, sameDate && existing!.WarnedOnDay);
    }

    public string Countdown(DateOnly today) => DaysLeft(today) switch
    {
        0 => "Renova hoje",
        1 => "Falta 1 dia",
        -1 => "Data passou há 1 dia",
        > 1 and var days => $"Faltam {days} dias",
        var days => $"Data passou há {-days} dias"
    };

    public bool Due(DateOnly today) => DaysLeft(today) switch
    {
        0 => !WarnedOnDay,
        > 0 and var days => days <= LeadDays && !WarnedBefore,
        _ => false
    };

    public PlanReminder AfterNotice(DateOnly today) => DaysLeft(today) == 0
        ? this with { WarnedOnDay = true }
        : this with { WarnedBefore = true };
}

internal static class ReminderStore
{
    private const string RegistryPath = @"Software\Artho\Soneca";
    private const string RegistryValue = "PlanReminders";

    public static List<PlanReminder> Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return key?.GetValue(RegistryValue) is string json
            ? JsonSerializer.Deserialize<List<PlanReminder>>(json) ?? []
            : [];
    }

    public static void Save(List<PlanReminder> entries)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue(RegistryValue, JsonSerializer.Serialize(entries));
    }
}

