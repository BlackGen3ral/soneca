package com.arthostudio.arthoai;

import android.app.AlertDialog;
import android.text.InputType;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.io.IOException;
import java.time.OffsetDateTime;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.UUID;
import java.util.concurrent.Executor;
import org.json.JSONArray;
import org.json.JSONObject;

final class DevicesPanel {
    private static final String[] DAYS = {"Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"};
    private final MainActivity owner;
    private final StatusClient.Pairing pairing;
    private final Executor worker;
    private final LinearLayout panel;
    private LinearLayout cards;
    private TextView notice;
    private boolean busy;

    DevicesPanel(MainActivity owner, StatusClient.Pairing pairing, Executor worker) {
        this.owner = owner;
        this.pairing = pairing;
        this.worker = worker;
        panel = column();
        Button refresh = owner.button("Atualizar status", false);
        refresh.setOnClickListener(view -> load());
        panel.addView(refresh);
        notice = text("");
        notice.setAccessibilityLiveRegion(TextView.ACCESSIBILITY_LIVE_REGION_POLITE);
        panel.addView(notice, owner.spaced(10));
        cards = column();
        panel.addView(cards);
    }

    ViewGroup view() { return panel; }

    void load() {
        request("GET", "/v1/devices", "Consultando os PCs na rede…", result -> {
            JSONArray list = result.getJSONArray("devices");
            cards.removeAllViews();
            notice.setText(list.length() == 0 ? "Pareie os PCs na aba Dispositivos do Windows." : "");
            for (int index = 0; index < list.length(); index++) addCard(list.getJSONObject(index));
        });
    }

    private void addCard(JSONObject pc) {
        String id = UUID.fromString(pc.optString("id")).toString();
        String name = pc.optString("name");
        LinearLayout card = column();
        card.setBackground(owner.surface(MainActivity.SURFACE, 16, MainActivity.BORDER));
        card.setPadding(dp(16), dp(14), dp(16), dp(14));
        cards.addView(card, owner.spaced(10));
        TextView title = text(name);
        title.setTextSize(20);
        title.setAccessibilityHeading(true);
        title.setPadding(0, 0, 0, dp(4));
        card.addView(title);
        String last = "ainda não respondeu";
        if (!pc.isNull("lastContact")) {
            last = OffsetDateTime.parse(pc.optString("lastContact")).atZoneSameInstant(ZoneId.systemDefault())
                    .format(DateTimeFormatter.ofPattern("dd/MM/yyyy HH:mm:ss"));
        }
        TextView connection = text((pc.optBoolean("online") ? "Online" : "Sem resposta") + " · Último contato: " + last);
        connection.setTextColor(pc.optBoolean("online") ? MainActivity.ACCENT : MainActivity.MUTED);
        card.addView(connection);
        card.addView(text("Ligar " + describe(pc.optJSONObject("wake"))
                + "\nDesligar " + describe(pc.optJSONObject("shutdown"))), owner.spaced(8));
        if (!pc.isNull("lastRoutine")) card.addView(text(pc.optString("lastRoutine")), owner.spaced(5));
        Button wake = owner.button("Ligar agora", true);
        wake.setOnClickListener(view -> request("POST", "/v1/devices/" + id + "/wake",
                "Enviando o sinal…", result -> notice.setText("Sinal enviado para " + name + ".")));
        card.addView(wake, owner.spaced(10));
        Button edit = owner.button("Editar horários", false);
        edit.setOnClickListener(view -> { if (!busy) edit(id, name, pc); });
        card.addView(edit, owner.spaced(6));
    }

    private static String describe(JSONObject rule) {
        if (rule == null) return "sem horário";
        StringBuilder days = new StringBuilder();
        for (int day = 0; day < DAYS.length; day++) {
            if ((rule.optInt("days") & (1 << day)) == 0) continue;
            if (days.length() > 0) days.append(", ");
            days.append(DAYS[day]);
        }
        return "às " + rule.optString("time") + " (" + days + ")";
    }

    private void edit(String id, String name, JSONObject pc) {
        LinearLayout fields = column();
        fields.addView(text("Escolha os dias de cada rotina. Os horários seguem o relógio do PC de controle."));
        RuleEditor wake = new RuleEditor(fields, "Ligar", "08:00", pc.optJSONObject("wake"));
        RuleEditor stop = new RuleEditor(fields, "Desligar", "18:00", pc.optJSONObject("shutdown"));
        TextView message = text("");
        fields.addView(message);
        AlertDialog editor = new AlertDialog.Builder(owner).setTitle(name).setView(scroll(fields))
                .setNegativeButton("Cancelar", null).setPositiveButton("Salvar", null).create();
        editor.show();
        editor.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view -> {
            try {
                String wakeTime = wake.value();
                String stopTime = stop.value();
                int wakeDays = wake.days();
                int stopDays = stop.days();
                if (!wakeTime.isEmpty() && wakeTime.equals(stopTime) && (wakeDays & stopDays) != 0)
                    throw new IllegalArgumentException("Use horários diferentes para ligar e desligar nos mesmos dias.");
                String path = "/v1/devices/" + id + "/routine?wakeTime=" + wakeTime + "&wakeDays=" + wakeDays
                        + "&shutdownTime=" + stopTime + "&shutdownDays=" + stopDays;
                editor.dismiss();
                request("POST", path, "Salvando os horários…", result -> load());
            } catch (IllegalArgumentException ex) { message.setText(ex.getMessage()); }
        });
    }

    private final class RuleEditor {
        private final CheckBox enabled;
        private final EditText clock;
        private final CheckBox[] days = new CheckBox[DAYS.length];

        RuleEditor(LinearLayout fields, String title, String example, JSONObject current) {
            enabled = new CheckBox(owner);
            enabled.setText(title);
            enabled.setChecked(current != null);
            fields.addView(enabled);
            clock = new EditText(owner);
            clock.setSingleLine();
            clock.setInputType(InputType.TYPE_CLASS_DATETIME | InputType.TYPE_DATETIME_VARIATION_TIME);
            clock.setContentDescription("Horário para " + title.toLowerCase(java.util.Locale.ROOT) + ", HH:mm");
            clock.setText(current == null ? example : current.optString("time"));
            fields.addView(clock);
            int mask = current == null ? 126 : current.optInt("days");
            LinearLayout row = null;
            for (int day = 0; day < DAYS.length; day++) {
                if (day % 3 == 0) {
                    row = new LinearLayout(owner);
                    fields.addView(row);
                }
                CheckBox check = new CheckBox(owner);
                check.setText(DAYS[day]);
                check.setChecked((mask & (1 << day)) != 0);
                days[day] = check;
                row.addView(check);
            }
            enabled.setOnCheckedChangeListener((button, active) -> update(active));
            update(enabled.isChecked());
        }

        int days() {
            int mask = 0;
            if (enabled.isChecked()) {
                for (int day = 0; day < days.length; day++) if (days[day].isChecked()) mask |= 1 << day;
            }
            return mask;
        }

        String value() {
            if (!enabled.isChecked()) return "";
            String value = clock.getText().toString().trim();
            if (!value.matches("(?:[01][0-9]|2[0-3]):[0-5][0-9]") || days() == 0)
                throw new IllegalArgumentException("Use HH:mm e marque pelo menos um dia em cada rotina ativa.");
            return value;
        }

        private void update(boolean active) {
            clock.setEnabled(active);
            for (CheckBox day : days) day.setEnabled(active);
        }
    }

    private void request(String method, String route, String waiting, Result accepted) {
        if (busy) return;
        busy = true;
        notice.setText(waiting);
        worker.execute(() -> {
            try {
                JSONObject result = StatusClient.request(pairing, method, route);
                owner.runOnUiThread(() -> {
                    busy = false;
                    if (!isOpen()) return;
                    try { accepted.show(result); }
                    catch (Exception ex) { notice.setText("O Windows enviou dados inválidos."); }
                });
            } catch (Exception error) {
                String message = error instanceof IOException && error.getMessage() != null ? error.getMessage()
                        : "Confira a conexão com o PC de controle e pareie novamente se a sessão tiver terminado.";
                owner.runOnUiThread(() -> {
                    busy = false;
                    if (isOpen()) notice.setText(message);
                });
            }
        });
    }

    private boolean isOpen() { return !owner.isFinishing() && !owner.isDestroyed() && panel.isAttachedToWindow(); }
    private interface Result { void show(JSONObject data) throws Exception; }

    private LinearLayout column() {
        LinearLayout view = new LinearLayout(owner);
        view.setOrientation(LinearLayout.VERTICAL);
        return view;
    }

    private ViewGroup scroll(LinearLayout view) {
        view.setPadding(dp(22), dp(8), dp(22), dp(8));
        ScrollView scroll = new ScrollView(owner);
        scroll.addView(view);
        return scroll;
    }

    private TextView text(String value) {
        TextView view = new TextView(owner);
        view.setText(value);
        view.setTextSize(15);
        return view;
    }

    private int dp(int value) {
        return Math.round(value * owner.getResources().getDisplayMetrics().density);
    }
}
