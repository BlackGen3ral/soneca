package com.arthostudio.arthoai;

import android.app.AlertDialog;
import android.app.TimePickerDialog;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
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
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
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
    private final TextView wakeFeedback;
    private final ExecutorService wakeWorker = Executors.newSingleThreadExecutor();
    private final Handler wakeTimer = new Handler(Looper.getMainLooper());
    private final Runnable wakeTimeout = this::wakeUnconfirmed;
    private final Button wakeNow;
    private final Button wakeSchedule;
    private final Button cancelWake;
    private final Button forgetWake;
    private final Button stopNow;
    private final Button internetShutdown;
    private final TextView internetFeedback;
    private final Button restart;
    private final Button suspendNow;
    private final LinearLayout suspendGroup;
    private final TextView suspendTime;
    private final Button suspendSchedule;
    private final Button cancelSuspend;
    private final Button stopSchedule;
    private final Button cancelStop;
    private final Button connect;
    private StatusClient.Pairing pairing;
    private JSONObject lastState;
    private boolean busy;
    private boolean sendingWake;
    private boolean sendingInternetShutdown;
    private final ExecutorService internetWorker = Executors.newSingleThreadExecutor();
    private final Runnable receiptCheck = this::checkReceipt;
    private StatusClient.Pairing receiptTarget;
    private long receiptIssued;
    private boolean checkingReceipt;
    private boolean foreground;
    private WakeTarget waitingForWake;
    private int wakeAttempt;

    PowerPanel(MainActivity owner, Executor worker) {
        this.owner = owner;
        this.worker = worker;
        page = owner.column();
        page.addView(owner.pageTitle("Energia"));
        LinearLayout status = section("PC de controle");
        status.setBackground(owner.highlightSurface());
        notice = label("Conecte-se ao Windows para consultar o estado da energia.", 14, MainActivity.MUTED);
        notice.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        status.addView(notice, owner.spaced(6));
        connect = action(status, "Parear com Windows", true, owner::showPairing);
        action(status, "Atualizar estado", false, this::refresh);

        LinearLayout quick = section("Na rede de casa");
        wakeNow = action(quick, "Ligar pelo Wi-Fi de casa", true, this::wake);
        wakeFeedback = label("", 13, MainActivity.MUTED);
        wakeFeedback.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        quick.addView(wakeFeedback, owner.spaced(9));
        LinearLayout immediate = new LinearLayout(owner);
        quick.addView(immediate, owner.spaced(10));
        suspendNow = inlineAction(immediate, "Suspender", "Suspender agora", () -> confirm("Suspender", "suspend"), 6);
        LinearLayout restartAndStop = new LinearLayout(owner);
        quick.addView(restartAndStop, owner.spaced(6));
        stopNow = inlineAction(restartAndStop, "Desligar", "Desligar agora", () -> confirm("Desligar", "shutdown"), 0);
        restart = inlineAction(restartAndStop, "Reiniciar", "Reiniciar agora", () -> confirm("Reiniciar", "restart"), 6);
        LinearLayout internet = section("Fora de casa");
        internetShutdown = action(internet, "Desligar pela internet", false, this::confirmInternetShutdown);
        internetFeedback = label("Ative essa opção na aba Celular do Artho AI para Windows.", 13, MainActivity.MUTED);
        internetFeedback.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        internet.addView(internetFeedback, owner.spaced(7));

        LinearLayout schedule = section("Agendamentos");
        wakeRule = label("", 14, MainActivity.MUTED);
        schedule.addView(label("Ligar", 15, MainActivity.INK), owner.spaced(10));
        schedule.addView(wakeRule, owner.spaced(4));
        wakeSchedule = action(schedule, "Programar para ligar", false, this::chooseWakeMode);
        cancelWake = action(schedule, "Cancelar horário de ligar", false, () -> {
            WakeSchedules.clearRule(owner);
            notice.setText("Horário de ligar cancelado.");
            render();
        });
        View divider = owner.divider();
        LinearLayout.LayoutParams separator = new LinearLayout.LayoutParams(-1, owner.dp(1));
        separator.topMargin = owner.dp(13);
        schedule.addView(divider, separator);
        schedule.addView(label("Desligar", 15, MainActivity.INK), owner.spaced(10));
        oneTime = label("", 14, MainActivity.MUTED);
        weekly = label("", 14, MainActivity.MUTED);
        schedule.addView(oneTime, owner.spaced(4));
        schedule.addView(weekly, owner.spaced(4));
        stopSchedule = action(schedule, "Programar para desligar", false, this::chooseStopMode);
        cancelStop = action(schedule, "Cancelar desligamento único", false,
                () -> request("POST", "/v1/energy/shutdown/cancel", "Desligamento único cancelado.", false));
        suspendGroup = owner.column();
        schedule.addView(suspendGroup, owner.spaced(13));
        suspendGroup.addView(owner.divider());
        suspendGroup.addView(label("Suspender", 15, MainActivity.INK), owner.spaced(10));
        suspendTime = label("", 14, MainActivity.MUTED);
        suspendGroup.addView(suspendTime, owner.spaced(4));
        suspendSchedule = action(suspendGroup, "Programar suspensão", false, this::chooseSuspendMode);
        cancelSuspend = action(suspendGroup, "Cancelar suspensão", false,
                () -> request("POST", "/v1/energy/suspend/cancel", "Suspensão cancelada no Windows.", false));
        forgetWake = action(page, "Esquecer este PC", false, () -> new AlertDialog.Builder(owner)
                .setTitle("Esquecer este PC?")
                .setMessage("Isso remove o PC do celular e cancela o horário de ligar salvo aqui.")
                .setNegativeButton("Voltar", null).setPositiveButton("Esquecer", (dialog, button) -> owner.disconnect()).show());
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
        clearWakeAttempt();
        clearReceipt();
        internetFeedback.setText("Ative essa opção na aba Celular do Artho AI para Windows.");
        pairingChanged(null);
        render();
    }

    void onPairingRevoked(StatusClient.Pairing revoked) {
        WakeTarget saved = WakeSchedules.target(owner);
        if (saved != null && saved.address.equals(revoked.host)) {
            WakeSchedules.forget(owner);
            clearWakeAttempt();
        }
    }

    void peerResponded(StatusClient.Pairing peer, JSONObject status) {
        JSONObject wake = status.optJSONObject("wake");
        if (peer.durable && wake != null) rememberWake(peer, wake);
        if (waitingForWake != null && peer.durable && waitingForWake.address.equals(peer.host)) {
            String name = waitingForWake.name;
            clearWakeAttempt();
            wakeFeedback.setText(name + " respondeu na rede.");
        }
        render();
    }

    void close() {
        wakeTimer.removeCallbacks(wakeTimeout);
        wakeWorker.shutdownNow();
        setForeground(false);
        internetWorker.shutdownNow();
    }

    void setForeground(boolean visible) {
        foreground = visible;
        wakeTimer.removeCallbacks(receiptCheck);
        if (visible && receiptTarget != null) wakeTimer.post(receiptCheck);
    }

    void refreshLocal() { render(); }

    private void refresh() {
        if (pairing != null) request("GET", "/v1/energy", "", false);
        else {
            notice.setText("No Windows, permita o controle na aba Celular e leia um novo QR Code.");
            render();
        }
    }

    void wake() {
        WakeTarget target = WakeSchedules.target(owner);
        if (target == null || sendingWake) return;
        clearWakeAttempt();
        int attempt = wakeAttempt;
        sendingWake = true;
        wakeFeedback.setText("Enviando sinal para " + target.name + "…");
        render();
        wakeWorker.execute(() -> {
            String errorMessage = null;
            try {
                WakeTarget current = WakeSchedules.target(owner);
                if (current == null || !target.samePc(current))
                    throw new java.io.IOException("Os dados do PC mudaram. Tente novamente.");
                target.send(owner.getApplicationContext());
            } catch (Exception error) {
                errorMessage = error.getMessage() == null ? "Não foi possível enviar o sinal para ligar." : error.getMessage();
            }
            String failure = errorMessage;
            owner.show(() -> {
                if (wakeAttempt != attempt) return;
                sendingWake = false;
                if (failure != null) wakeFeedback.setText(failure);
                else {
                    waitingForWake = target;
                    wakeFeedback.setText("Sinal enviado. Aguardando " + target.name + " responder na rede…");
                    wakeTimer.postDelayed(wakeTimeout, 90_000);
                    owner.retryAfterWake();
                }
                render();
            });
        });
    }

    private void clearWakeAttempt() {
        wakeAttempt++;
        wakeTimer.removeCallbacks(wakeTimeout);
        waitingForWake = null;
        sendingWake = false;
        wakeFeedback.setText("");
    }

    private void wakeUnconfirmed() {
        waitingForWake = null;
        wakeFeedback.setText("Sinal enviado. O PC ainda não respondeu no Artho AI.");
        render();
    }

    private void chooseStopMode() {
        if (lastState == null) return;
        new AlertDialog.Builder(owner).setTitle("Programar desligamento")
                .setItems(new String[]{"Daqui a alguns minutos", "Em um horário", "Toda semana", "Cancelar rotina semanal"},
                        (dialog, choice) -> {
                            switch (choice) {
                                case 0: minutesDialog(false); break;
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

    private void chooseSuspendMode() {
        if (lastState == null || !lastState.optBoolean("suspendControl")) return;
        new AlertDialog.Builder(owner).setTitle("Programar suspensão")
                .setItems(new String[]{"Daqui a alguns minutos", "Em um horário"}, (dialog, choice) -> {
                    if (choice == 0) minutesDialog(true);
                    else new TimePickerDialog(owner, (picker, hour, minute) ->
                            request("POST", "/v1/energy/suspend/at?value=" + clock(hour, minute),
                                    "Suspensão agendada no Windows.", false), 18, 0, true).show();
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

    private void minutesDialog(boolean suspend) {
        EditText minutes = new EditText(owner);
        minutes.setInputType(InputType.TYPE_CLASS_NUMBER);
        minutes.setText("30");
        minutes.setContentDescription(suspend ? "Minutos até suspender" : "Minutos até desligar");
        LinearLayout container = form();
        container.addView(minutes);
        AlertDialog dialog = new AlertDialog.Builder(owner).setTitle(suspend ? "Suspender em minutos" : "Desligar em minutos").setView(container)
                .setNegativeButton("Cancelar", null).setPositiveButton("Salvar", null).create();
        dialog.show();
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view -> {
            String text = minutes.getText().toString();
            if (!text.matches("[0-9]{1,5}") || Integer.parseInt(text) < 1 || Integer.parseInt(text) > 10080) {
                minutes.setError("Use de 1 a 10.080 minutos.");
                return;
            }
            dialog.dismiss();
            String route = suspend ? "/v1/energy/suspend/after?value=" : "/v1/energy/shutdown/after?value=";
            request("POST", route + text, suspend ? "Suspensão agendada no Windows." : "Desligamento agendado no Windows.", false);
        });
    }

    private void confirm(String verb, String command) {
        if (lastState == null) return;
        String name = lastState.optString("name", "o PC");
        boolean suspend = command.equals("suspend");
        new AlertDialog.Builder(owner).setTitle(verb + " " + name + "?")
                .setMessage(suspend ? "A conexão é pausada até o PC despertar." : "Salve seu trabalho no computador antes de continuar.")
                .setNegativeButton("Cancelar", null).setPositiveButton(verb + " agora", (dialog, button) ->
                        request("POST", "/v1/energy/now/" + command,
                                suspend ? "Solicitação de suspensão enviada." : "Comando aceito pelo Windows.", true)).show();
    }

    private void confirmInternetShutdown() {
        StatusClient.Pairing target = owner.internetPowerPairing();
        if (target == null || sendingInternetShutdown) return;
        WakeTarget saved = WakeSchedules.target(owner);
        String name = saved != null && saved.address.equals(target.host) ? saved.name : "o PC pareado";
        new AlertDialog.Builder(owner).setTitle("Desligar " + name + " pela internet?")
                .setMessage("Salve seu trabalho no computador antes de continuar.")
                .setNegativeButton("Cancelar", null)
                .setPositiveButton("Desligar", (dialog, button) -> sendInternetShutdown(target)).show();
    }

    private void sendInternetShutdown(StatusClient.Pairing target) {
        if (owner.internetPowerPairing() != target || sendingInternetShutdown || receiptTarget != null) return;
        sendingInternetShutdown = true;
        long issued = System.currentTimeMillis();
        internetShutdown.setEnabled(false);
        internetFeedback.setText("Enviando pedido pela internet…");
        internetWorker.execute(() -> {
            String message = null;
            boolean uncertain = false;
            try {
                message = owner.internetPowerPairing() == target ? InternetPowerClient.requestShutdown(target, issued)
                        : "O pareamento mudou.";
            } catch (Exception error) {
                uncertain = true;
            }
            String result = message;
            boolean sendUncertain = uncertain;
            owner.show(() -> {
                sendingInternetShutdown = false;
                if (owner.internetPowerPairing() == target) {
                    if (result != null) internetFeedback.setText(result);
                    else {
                        receiptTarget = target;
                        receiptIssued = issued;
                        internetFeedback.setText(sendUncertain ? "Aguardando confirmação do envio…"
                                : "Pedido enviado. Aguardando o Windows receber…");
                        if (foreground) wakeTimer.post(receiptCheck);
                    }
                }
                render();
            });
        });
    }

    private void checkReceipt() {
        if (!foreground || checkingReceipt || receiptTarget == null) return;
        StatusClient.Pairing target = receiptTarget;
        long issued = receiptIssued;
        if (owner.internetPowerPairing() != target) {
            clearReceipt();
            render();
            return;
        }
        checkingReceipt = true;
        internetWorker.execute(() -> {
            InternetPowerClient.Receipt receipt = null;
            try { receipt = InternetPowerClient.readReceipt(target, issued); }
            catch (Exception ignored) { }
            InternetPowerClient.Receipt result = receipt;
            owner.show(() -> {
                checkingReceipt = false;
                if (target != receiptTarget || issued != receiptIssued) {
                    if (foreground && receiptTarget != null) wakeTimer.post(receiptCheck);
                    return;
                }
                if (owner.internetPowerPairing() != target) clearReceipt();
                else if (result == InternetPowerClient.Receipt.RECEIVED) {
                    clearReceipt();
                    internetFeedback.setText("O Windows recebeu o pedido.");
                } else if (result == InternetPowerClient.Receipt.EXPIRED
                        || System.currentTimeMillis() - issued > 35_000) {
                    clearReceipt();
                    internetFeedback.setText("Prazo expirado sem confirmação do Windows.");
                } else {
                    if (result == InternetPowerClient.Receipt.WAITING)
                        internetFeedback.setText("Pedido enviado. Aguardando o Windows receber…");
                    if (foreground) wakeTimer.postDelayed(receiptCheck, 2500);
                }
                render();
            });
        });
    }

    private void clearReceipt() {
        receiptTarget = null;
        wakeTimer.removeCallbacks(receiptCheck);
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
            boolean first = WakeSchedules.target(owner) == null;
            WakeSchedules.remember(owner, new WakeTarget(target.host, data.optString("mac"),
                    data.optInt("prefix"), data.optString("name", "Meu PC")));
            if (first) wakeFeedback.setText("");
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
        wakeNow.setEnabled(!sendingWake && target != null);
        if (target == null && !sendingWake) wakeFeedback.setText("Conecte ao PC uma vez para salvar o acesso de ligar pela rede.");
        wakeSchedule.setEnabled(!busy && target != null);
        cancelWake.setVisibility(time.isEmpty() ? View.GONE : View.VISIBLE);
        forgetWake.setVisibility(target == null ? View.GONE : View.VISIBLE);
        boolean available = !busy && lastState != null;
        connect.setVisibility(!busy && lastState == null ? View.VISIBLE : View.GONE);
        stopNow.setEnabled(available);
        internetShutdown.setEnabled(!sendingInternetShutdown && receiptTarget == null && owner.internetPowerPairing() != null);
        restart.setEnabled(available);
        stopSchedule.setEnabled(available);
        boolean canSuspend = lastState != null && lastState.optBoolean("suspendControl");
        suspendNow.setVisibility(canSuspend ? View.VISIBLE : View.GONE);
        suspendNow.setEnabled(available);
        suspendGroup.setVisibility(canSuspend ? View.VISIBLE : View.GONE);
        suspendSchedule.setEnabled(available);
        String atSleep = canSuspend ? lastState.optString("suspendAt", "") : "";
        boolean hasSleep = !atSleep.isEmpty() && !"null".equals(atSleep);
        suspendTime.setText(hasSleep ? "Suspensão agendada · " +
                OffsetDateTime.parse(atSleep).format(DateTimeFormatter.ofPattern("dd/MM 'às' HH:mm")) : "Sem suspensão agendada");
        suspendSchedule.setText(hasSleep ? "Alterar suspensão" : "Programar suspensão");
        cancelSuspend.setVisibility(hasSleep ? View.VISIBLE : View.GONE);
        cancelSuspend.setEnabled(available);
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
        weekly.setVisibility(rule == null ? View.GONE : View.VISIBLE);
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
        LinearLayout group = owner.card();
        TextView heading = label(title, 18, MainActivity.INK);
        heading.setAccessibilityHeading(true);
        group.addView(heading);
        page.addView(group, owner.spaced(12));
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
    private Button inlineAction(LinearLayout where, String text, String description, Runnable run, int inset) {
        Button button = owner.button(text, false);
        button.setContentDescription(description);
        button.setTextSize(13);
        button.setPadding(owner.dp(4), owner.dp(8), owner.dp(4), owner.dp(8));
        button.setOnClickListener(view -> run.run());
        LinearLayout.LayoutParams size = new LinearLayout.LayoutParams(0, -2, 1);
        size.leftMargin = owner.dp(inset);
        where.addView(button, size);
        return button;
    }
    private interface RuleResult { void save(String clock, int days); }
}
