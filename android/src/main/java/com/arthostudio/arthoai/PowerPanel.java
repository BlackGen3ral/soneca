package com.arthostudio.arthoai;

import android.app.AlertDialog;
import android.app.TimePickerDialog;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.net.Uri;
import android.provider.Settings;
import android.text.InputType;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.time.OffsetDateTime;
import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.Locale;
import java.util.concurrent.Executor;
import org.json.JSONObject;

final class PowerPanel {
    private static final String[] DAYS = {"Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"};
    private final MainActivity owner;
    private final Executor worker;
    private final LinearLayout page;
    private final TextView notice;
    private final TextView oneTime;
    private final TextView weekly;
    private final TextView wakeRule;
    private final Button wakeNow;
    private final Button wakeSchedule;
    private final Button cancelWake;
    private final Button forgetWake;
    private final Button stopNow;
    private final Button restart;
    private final Button stopSchedule;
    private final Button cancelStop;
    private final Button connect;
    private StatusClient.Pairing pairing;
    private JSONObject lastState;
    private boolean busy;

    PowerPanel(MainActivity owner, Executor worker) {
        this.owner = owner;
        this.worker = worker;
        page = owner.column();
        TextView title = label("Energia do PC", 26, MainActivity.INK);
        title.setAccessibilityHeading(true);
        page.addView(title);
        notice = label("", 14, MainActivity.MUTED);
        notice.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        page.addView(notice, owner.spaced(7));
        connect = action(page, "Parear com Windows", true, owner::showPairing);

        LinearLayout wake = section("Ligar");
        wakeRule = label("", 14, MainActivity.MUTED);
        wake.addView(wakeRule, owner.spaced(6));
        wakeNow = action(wake, "Ligar agora", false, this::wake);
        wakeSchedule = action(wake, "Programar para ligar", false, this::chooseWakeMode);
        cancelWake = action(wake, "Cancelar horário de ligar", false, () -> {
            WakeSchedules.clearRule(owner);
            notice.setText("Horário de ligar cancelado.");
            render();
        });
        forgetWake = action(wake, "Esquecer este PC", false, () -> new AlertDialog.Builder(owner)
                .setTitle("Esquecer este PC?")
                .setMessage("Isso remove o PC do celular e cancela o horário de ligar salvo aqui.")
                .setNegativeButton("Voltar", null).setPositiveButton("Esquecer", (dialog, button) -> owner.disconnect()).show());

        LinearLayout stop = section("Desligar e reiniciar");
        oneTime = label("", 14, MainActivity.MUTED);
        weekly = label("", 14, MainActivity.MUTED);
        stop.addView(oneTime, owner.spaced(6));
        stop.addView(weekly, owner.spaced(4));
        LinearLayout immediate = new LinearLayout(owner);
        stop.addView(immediate, owner.spaced(8));
        stopNow = inlineAction(immediate, "Desligar agora", () -> confirm("Desligar", "shutdown"), 0);
        restart = inlineAction(immediate, "Reiniciar agora", () -> confirm("Reiniciar", "restart"), 6);
        stopSchedule = action(stop, "Programar para desligar", false, this::chooseStopMode);
        cancelStop = action(stop, "Cancelar desligamento único", false,
                () -> request("POST", "/v1/energy/shutdown/cancel", "Desligamento único cancelado.", false));
        action(stop, "Atualizar estado", false, this::refresh);
        render();
    }

    View view() { return page; }

    void pairingChanged(StatusClient.Pairing value) {
        if (pairing == value) return;
        pairing = value;
        lastState = null;
        busy = false;
        WakeTarget cached = WakeSchedules.target(owner);
        if (value != null && cached != null && !cached.address.equals(value.host)) WakeSchedules.forget(owner);
        render();
        if (value != null) refresh();
    }

    void forget() {
        WakeSchedules.forget(owner);
        pairingChanged(null);
        render();
    }

    void refreshLocal() { render(); }

    private void refresh() {
        if (pairing != null) request("GET", "/v1/energy", "", false);
        else {
            notice.setText("No Windows, permita o controle na aba Celular e leia um novo QR Code.");
            render();
        }
    }

    private void wake() {
        WakeTarget target = WakeSchedules.target(owner);
        if (target == null || busy) return;
        busy = true;
        render();
        worker.execute(() -> {
            String result;
            try {
                target.send(owner.getApplicationContext());
                result = "Sinal enviado. Confira se " + target.name + " ligou.";
            } catch (Exception error) {
                result = error.getMessage() == null ? "Não foi possível enviar o sinal para ligar." : error.getMessage();
            }
            String message = result;
            owner.show(() -> {
                busy = false;
                notice.setText(message);
                render();
            });
        });
    }

    private void chooseStopMode() {
        if (lastState == null) return;
        new AlertDialog.Builder(owner).setTitle("Programar desligamento")
                .setItems(new String[]{"Daqui a alguns minutos", "Em um horário", "Toda semana", "Cancelar rotina semanal"},
                        (dialog, choice) -> {
                            switch (choice) {
                                case 0: minutesDialog(); break;
                                case 1:
                                    new TimePickerDialog(owner, (picker, hour, minute) ->
                                            request("POST", "/v1/energy/shutdown/at?value=" + clock(hour, minute),
                                                    "Horário salvo no Windows.", false), 18, 0, true).show();
                                    break;
                                case 2:
                                    JSONObject rule = lastState == null ? null : lastState.optJSONObject("shutdownWeekly");
                                    weeklyDialog("Desligar toda semana", rule == null ? "" : rule.optString("time"),
                                            rule == null ? 0 : rule.optInt("days"), (time, days) ->
                                                    request("POST", "/v1/energy/shutdown/weekly?value=" + time + "&days=" + days,
                                                            "Rotina semanal salva no Windows.", false), false);
                                    break;
                                case 3: request("POST", "/v1/energy/shutdown/weekly?days=0",
                                        "Rotina semanal cancelada.", false); break;
                            }
                        }).setNegativeButton("Voltar", null).show();
    }

    private void chooseWakeMode() {
        if (!WakeSchedules.canSetExact(owner)) {
            notice.setText("Autorize alarmes exatos no Android e volte para programar.");
            openAlarmSettings();
            return;
        }
        new AlertDialog.Builder(owner).setTitle("Programar para ligar")
                .setMessage("O celular precisa estar ligado e no Wi-Fi do PC no horário escolhido.")
                .setPositiveButton("Uma vez", (dialog, button) -> new TimePickerDialog(owner, (picker, hour, minute) ->
                        wakeSaved(WakeSchedules.saveOnce(owner, clock(hour, minute))), 8, 0, true).show())
                .setNeutralButton("Toda semana", (dialog, button) ->
                        weeklyDialog("Ligar toda semana", WakeSchedules.time(owner), WakeSchedules.days(owner),
                                (clock, days) -> wakeSaved(WakeSchedules.save(owner, clock, days)), true))
                .setNegativeButton("Cancelar", null).show();
    }

    private void wakeSaved(boolean saved) {
        notice.setText(saved ? "Horário de ligar salvo no celular."
                : "O Android não aceitou o horário. Confira a permissão de alarmes.");
        render();
    }

    private void minutesDialog() {
        EditText minutes = new EditText(owner);
        minutes.setInputType(InputType.TYPE_CLASS_NUMBER);
        minutes.setText("30");
        minutes.setContentDescription("Minutos até desligar");
        LinearLayout container = form();
        container.addView(minutes);
        AlertDialog dialog = new AlertDialog.Builder(owner).setTitle("Desligar em minutos").setView(container)
                .setNegativeButton("Cancelar", null).setPositiveButton("Salvar", null).create();
        dialog.show();
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view -> {
            String text = minutes.getText().toString();
            if (!text.matches("[0-9]{1,5}") || Integer.parseInt(text) < 1 || Integer.parseInt(text) > 10080) {
                minutes.setError("Use de 1 a 10.080 minutos.");
                return;
            }
            dialog.dismiss();
            request("POST", "/v1/energy/shutdown/after?value=" + text, "Desligamento agendado no Windows.", false);
        });
    }

    private void confirm(String verb, String command) {
        if (lastState == null) return;
        String name = lastState.optString("name", "o PC");
        new AlertDialog.Builder(owner).setTitle(verb + " " + name + "?")
                .setMessage("Salve seu trabalho no computador antes de continuar.")
                .setNegativeButton("Cancelar", null).setPositiveButton(verb + " agora", (dialog, button) ->
                        request("POST", "/v1/energy/now/" + command, "Comando aceito pelo Windows.", true)).show();
    }

    private void weeklyDialog(String title, String oldTime, int oldDays, RuleResult save, boolean wake) {
        LinearLayout fields = form();
        TextView correction = label("", 14, MainActivity.MUTED);
        EditText time = new EditText(owner);
        time.setSingleLine(true);
        time.setInputType(InputType.TYPE_CLASS_DATETIME | InputType.TYPE_DATETIME_VARIATION_TIME);
        time.setText(oldTime.isEmpty() ? (wake ? "08:00" : "18:00") : oldTime);
        time.setContentDescription("Horário HH:mm");
        fields.addView(time);
        CheckBox[] days = new CheckBox[7];
        LinearLayout row = null;
        int seedDays = oldDays > 0 ? oldDays : 126;
        for (int day = 0; day < days.length; day++) {
            if (day % 3 == 0) {
                row = new LinearLayout(owner);
                fields.addView(row);
            }
            CheckBox check = new CheckBox(owner);
            check.setText(DAYS[day]);
            check.setChecked((seedDays & (1 << day)) != 0);
            row.addView(check);
            days[day] = check;
        }
        if (wake) fields.addView(label("O celular precisa estar ligado no mesmo Wi-Fi no horário.", 14, MainActivity.MUTED),
                owner.spaced(7));
        fields.addView(correction, owner.spaced(7));
        ScrollView scroll = new ScrollView(owner);
        scroll.addView(fields);
        AlertDialog dialog = new AlertDialog.Builder(owner).setTitle(title).setView(scroll)
                .setNegativeButton("Cancelar", null).setPositiveButton("Salvar", null).create();
        dialog.show();
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view -> {
            int mask = 0;
            for (int day = 0; day < days.length; day++) if (days[day].isChecked()) mask |= 1 << day;
            String clock = time.getText().toString().trim();
            if (mask == 0 || !WakeSchedules.validTime(clock)) {
                correction.setText("Digite HH:mm e selecione pelo menos um dia.");
                return;
            }
            if (wake && !WakeSchedules.canSetExact(owner)) {
                correction.setText("Autorize alarmes exatos no Android, volte e toque em Salvar.");
                openAlarmSettings();
                return;
            }
            dialog.dismiss();
            save.save(clock, mask);
        });
    }

    private void openAlarmSettings() {
        try {
            owner.startActivity(new Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
                    Uri.fromParts("package", owner.getPackageName(), null)));
        } catch (ActivityNotFoundException | SecurityException ignored) { }
    }

    private void request(String method, String route, String success, boolean immediate) {
        StatusClient.Pairing target = pairing;
        if (target == null || busy) return;
        busy = true;
        notice.setText("Consultando o Windows…");
        render();
        worker.execute(() -> {
            try {
                JSONObject result = StatusClient.request(target, method, route);
                owner.show(() -> {
                    if (pairing != target) return;
                    busy = false;
                    if (!immediate) {
                        lastState = result;
                        rememberWake(target, result);
                    } else lastState = null;
                    notice.setText(success);
                    render();
                });
            } catch (Exception error) {
                owner.show(() -> {
                    if (pairing != target) return;
                    busy = false;
                    lastState = null;
                    String message = error.getMessage();
                    notice.setText(immediate ? "Confirme no PC se a ação aconteceu antes de tentar novamente."
                            : message == null || message.isEmpty() ? "Confira a conexão ou leia um novo QR Code." : message);
                    render();
                });
            }
        });
    }

    private void rememberWake(StatusClient.Pairing target, JSONObject data) {
        if (!target.host.equals(data.optString("address"))) return;
        try {
            WakeSchedules.remember(owner, new WakeTarget(target.host, data.optString("mac"),
                    data.optInt("prefix"), data.optString("name", "Meu PC")));
        } catch (IllegalArgumentException ignored) { }
    }

    private void render() {
        WakeTarget target = WakeSchedules.target(owner);
        String time = WakeSchedules.time(owner);
        long once = WakeSchedules.once(owner);
        String ruleInfo = once > 0
                ? Instant.ofEpochMilli(once).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("dd/MM 'às' HH:mm"))
                : time.isEmpty() ? "Sem horário programado" : describe(time, WakeSchedules.days(owner));
        String info = target == null
                ? "Aguardando um PC com Wake-on-LAN configurado."
                : target.name + " · " + ruleInfo;
        String report = WakeSchedules.report(owner);
        if (!report.isEmpty()) info += "\n" + report;
        if (!time.isEmpty() && !WakeSchedules.canSetExact(owner)) info += "\nRotina pausada. Permita alarmes exatos no Android.";
        wakeRule.setText(info);
        wakeNow.setEnabled(!busy && target != null);
        wakeSchedule.setEnabled(!busy && target != null);
        cancelWake.setVisibility(time.isEmpty() ? View.GONE : View.VISIBLE);
        forgetWake.setVisibility(target == null ? View.GONE : View.VISIBLE);
        boolean available = !busy && lastState != null;
        connect.setVisibility(!busy && lastState == null ? View.VISIBLE : View.GONE);
        stopNow.setEnabled(available);
        restart.setEnabled(available);
        stopSchedule.setEnabled(available);
        String at = lastState == null ? "" : lastState.optString("shutdownAt", "");
        if (at.isEmpty() || "null".equals(at)) {
            oneTime.setText(lastState == null ? "Conecte ao Windows com a permissão de energia ativada." : "Sem desligamento único");
            cancelStop.setVisibility(View.GONE);
        } else {
            String shown = OffsetDateTime.parse(at).format(DateTimeFormatter.ofPattern("dd/MM 'às' HH:mm"));
            oneTime.setText("Desligamento único · " + shown);
            cancelStop.setVisibility(View.VISIBLE);
            cancelStop.setEnabled(available);
        }
        JSONObject rule = lastState == null ? null : lastState.optJSONObject("shutdownWeekly");
        weekly.setText(rule == null ? "" : "Desligamento semanal · " + describe(rule.optString("time"), rule.optInt("days")));
    }

    private static String describe(String clock, int days) {
        StringBuilder selected = new StringBuilder();
        for (int day = 0; day < DAYS.length; day++) {
            if ((days & (1 << day)) == 0) continue;
            if (selected.length() > 0) selected.append(", ");
            selected.append(DAYS[day]);
        }
        return clock + " · " + selected;
    }
    private static String clock(int hour, int minute) { return String.format(Locale.ROOT, "%02d:%02d", hour, minute); }

    private LinearLayout section(String title) {
        LinearLayout group = owner.column();
        group.setPadding(owner.dp(15), owner.dp(13), owner.dp(15), owner.dp(13));
        group.setBackground(owner.surface(MainActivity.SURFACE, 16, MainActivity.BORDER));
        group.addView(label(title, 17, MainActivity.INK));
        page.addView(group, owner.spaced(17));
        return group;
    }
    private LinearLayout form() {
        LinearLayout fields = owner.column();
        fields.setPadding(owner.dp(20), owner.dp(12), owner.dp(20), owner.dp(12));
        return fields;
    }
    private TextView label(String text, int size, int color) { return owner.label(text, size, color, "sans-serif-medium"); }
    private Button action(LinearLayout where, String text, boolean primary, Runnable run) {
        Button button = owner.button(text, primary);
        button.setOnClickListener(view -> { if (!busy) run.run(); });
        where.addView(button, owner.spaced(6));
        return button;
    }
    private Button inlineAction(LinearLayout where, String text, Runnable run, int inset) {
        Button button = owner.button(text, false);
        button.setOnClickListener(view -> { if (!busy) run.run(); });
        LinearLayout.LayoutParams size = new LinearLayout.LayoutParams(0, -2, 1);
        size.leftMargin = owner.dp(inset);
        where.addView(button, size);
        return button;
    }
    private interface RuleResult { void save(String clock, int days); }
}
