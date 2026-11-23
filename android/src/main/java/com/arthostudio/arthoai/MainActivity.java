package com.arthostudio.arthoai;

import android.app.Activity;
import android.app.Dialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.view.inputmethod.EditorInfo;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import java.text.DateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import javax.net.ssl.SSLHandshakeException;
import org.json.JSONObject;

public final class MainActivity extends Activity {
    static final int BACKGROUND = 0xff131713;
    static final int SURFACE = 0xff222722;
    static final int BORDER = 0xff3f4a3b;
    static final int INK = 0xfff4f5eb;
    static final int MUTED = 0xffb4beaf;
    static final int ACCENT = 0xffd8eeac;
    private static final int GREEN = ACCENT;
    private static final int ALERT = 0xffffc4ac;
    private static final Locale BRAZIL = new Locale("pt", "BR");
    private static final int SCAN_PAIRING = 17;
    private static final int HOUSE_TAB = 4;
    private static final int[] NAV_ICONS = {R.drawable.nav_home, R.drawable.nav_devices, R.drawable.nav_power,
            R.drawable.nav_update, R.drawable.nav_house};

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler connectionTimer = new Handler(Looper.getMainLooper());
    private final Runnable reconnect = () -> requestStatus(false);
    private ScrollView scroll;
    private final View[] pages = new View[4];
    private final Button[] tabs = new Button[NAV_ICONS.length];
    private HousePanel house;
    private int selectedTab;
    private UpdatesPanel updates;
    private PowerPanel power;
    private LinearLayout deviceContent;
    private StatusClient.Pairing shownPairing;
    private boolean deviceInitialized;
    private boolean deviceOnline;
    private LinearLayout metrics;
    private StatusClient.Pairing pairing;
    private boolean connecting;
    private boolean visible;
    private boolean legacyLink;
    private boolean online;
    private TextView status;
    private TextView computer;
    private TextView detail;
    private TextView memory;
    private TextView cpu;
    private TextView uptime;
    private TextView network;
    private ProgressBar memoryBar;
    private Button primary;
    private Button connectionOptions;
    private Button remoteDesktop;

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().setStatusBarColor(BACKGROUND);
        getWindow().setNavigationBarColor(BACKGROUND);
        if (Build.VERSION.SDK_INT >= 30) getWindow().setDecorFitsSystemWindows(false);

        LinearLayout shell = column();
        shell.setBackground(new GlassBackdrop());
        if (Build.VERSION.SDK_INT >= 30) {
            shell.setOnApplyWindowInsetsListener((view, insets) -> {
                Insets safe = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(safe.left, safe.top, safe.right, safe.bottom);
                return insets;
            });
        }
        shell.addView(header());
        scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        shell.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        FrameLayout frame = new FrameLayout(this);
        scroll.addView(frame, new ScrollView.LayoutParams(-1, -2));
        LinearLayout content = column();
        content.setPadding(dp(18), dp(14), dp(18), dp(24));
        frame.addView(content, new FrameLayout.LayoutParams(-1, -2, Gravity.TOP | Gravity.CENTER_HORIZONTAL));
        frame.addOnLayoutChangeListener((view, left, top, right, bottom, oldLeft, oldTop, oldRight, oldBottom) -> {
            int width = Math.min(dp(620), right - left);
            FrameLayout.LayoutParams params = (FrameLayout.LayoutParams) content.getLayoutParams();
            if (width > 0 && params.width != width) {
                params.width = width;
                content.setLayoutParams(params);
            }
        });

        pages[0] = dashboard();
        pages[1] = devicePage();
        power = new PowerPanel(this, worker);
        pages[2] = power.view();
        updates = new UpdatesPanel(this);
        pages[3] = updates.view();
        for (View page : pages) content.addView(page);
        house = new HousePanel(this, worker);
        shell.addView(house, new LinearLayout.LayoutParams(-1, 0, 1));
        LinearLayout.LayoutParams dock = new LinearLayout.LayoutParams(-1, -2);
        dock.setMargins(dp(10), dp(4), dp(10), dp(8));
        shell.addView(navigation(), dock);
        setContentView(shell);
        shell.requestApplyInsets();

        if (saved != null && getLastNonConfigurationInstance() instanceof StatusClient.Pairing) {
            pairing = (StatusClient.Pairing) getLastNonConfigurationInstance();
        } else {
            worker.execute(() -> {
                StatusClient.Pairing stored = PairingVault.load(getApplicationContext());
                show(() -> {
                    if (stored == null || pairing != null) return;
                    pairing = stored;
                    house.updatePairing(pairing, false);
                    if (visible) requestStatus(true);
                });
            });
        }
        showTab(saved == null ? 0 : saved.getInt("tab", 0));
        house.updatePairing(pairing, false);
        WakeSchedules.restore(this);
        updates.check();
    }

    @Override protected void onStart() {
        super.onStart();
        visible = true;
        if (pairing != null) requestStatus(!online);
    }

    @Override protected void onStop() {
        visible = false;
        connectionTimer.removeCallbacks(reconnect);
        super.onStop();
    }

    @Override protected void onResume() {
        super.onResume();
        house.resume();
        if (updates != null) updates.resumed();
        if (power != null) power.refreshLocal();
    }

    @Override protected void onPause() {
        house.pause();
        super.onPause();
    }

    @Override protected void onSaveInstanceState(Bundle state) {
        super.onSaveInstanceState(state);
        state.putInt("tab", selectedTab);
    }

    private View dashboard() {
        LinearLayout page = column();
        TextView title = label("Visão geral", 31, INK, "sans-serif-medium");
        title.setAccessibilityHeading(true);
        page.addView(title);
        page.addView(houseCard(), spaced(13));
        page.addView(connectionPanel(), spaced(12));

        metrics = column();
        metrics.setVisibility(View.GONE);
        TextView section = label("Estado do PC", 17, INK, "sans-serif-medium");
        section.setAccessibilityHeading(true);
        metrics.addView(section);
        LinearLayout readings = column();
        readings.setPadding(dp(13), 0, dp(13), 0);
        readings.setBackground(surface(SURFACE, 16, BORDER));
        metrics.addView(readings, spaced(8));
        memory = information(readings, "Memória em uso");
        memoryBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        memoryBar.setMax(100);
        memoryBar.setProgressTintList(ColorStateList.valueOf(ACCENT));
        memoryBar.setProgressBackgroundTintList(ColorStateList.valueOf(BORDER));
        memoryBar.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        memoryBar.setVisibility(View.GONE);
        LinearLayout.LayoutParams bar = new LinearLayout.LayoutParams(-1, dp(4));
        bar.bottomMargin = dp(11);
        readings.addView(memoryBar, bar);
        readings.addView(divider());
        cpu = information(readings, "Processador");
        readings.addView(divider());
        uptime = information(readings, "Tempo ligado");
        readings.addView(divider());
        network = information(readings, "Rede do PC");
        page.addView(metrics, spaced(19));
        return page;
    }

    private View houseCard() {
        FrameLayout card = new FrameLayout(this);
        GradientDrawable background = new GradientDrawable(GradientDrawable.Orientation.TL_BR,
                new int[]{0xff39452e, 0xff2a3426, 0xff20291e});
        background.setCornerRadius(dp(19));
        background.setStroke(dp(1), 0xff59684c);
        card.setBackground(background);

        LinearLayout layout = row();
        layout.setPadding(dp(16), dp(15), dp(10), dp(15));
        layout.setGravity(Gravity.CENTER_VERTICAL);
        card.addView(layout, new FrameLayout.LayoutParams(-1, -2));
        LinearLayout copy = column();
        layout.addView(copy, new LinearLayout.LayoutParams(0, -2, 1.2f));
        TextView title = label("Minha casa", 24, INK, "sans-serif-medium");
        title.setAccessibilityHeading(true);
        copy.addView(title);
        copy.addView(label("Explore a maquete em 3D.", 13, MUTED, "sans-serif"), spaced(3));
        Button open = button("Abrir maquete", true);
        open.setTextSize(13);
        open.setMinHeight(dp(41));
        open.setOnClickListener(view -> showTab(HOUSE_TAB));
        LinearLayout.LayoutParams action = new LinearLayout.LayoutParams(-2, -2);
        action.topMargin = dp(11);
        copy.addView(open, action);

        ImageView art = new ImageView(this);
        art.setImageResource(R.drawable.house_study);
        art.setScaleType(ImageView.ScaleType.FIT_CENTER);
        art.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        layout.addView(art, new LinearLayout.LayoutParams(0, dp(107), .84f));
        return card;
    }

    private View devicePage() {
        LinearLayout page = column();
        TextView title = label("Dispositivos", 31, INK, "sans-serif-medium");
        title.setAccessibilityHeading(true);
        page.addView(title);
        deviceContent = column();
        page.addView(deviceContent, spaced(17));
        return page;
    }

    private void updateDevices() {
        if (deviceInitialized && shownPairing == pairing && deviceOnline == online) return;
        deviceInitialized = true;
        deviceOnline = online;
        shownPairing = pairing;
        deviceContent.removeAllViews();
        if (pairing == null) {
            deviceContent.addView(label("Conecte o celular ao PC de controle para ver os dispositivos.", 15, MUTED, "sans-serif"));
            Button connect = button("Conectar ao PC", true);
            connect.setOnClickListener(view -> showPairing());
            deviceContent.addView(connect, spaced(12));
            return;
        }
        if (!online) {
            deviceContent.addView(label("Aguardando o PC de controle voltar à rede.", 15, MUTED, "sans-serif"));
            return;
        }
        DevicesPanel panel = new DevicesPanel(this, pairing, worker);
        deviceContent.addView(panel.view());
        panel.load();
    }

    private View navigation() {
        LinearLayout nav = row();
        nav.setBackground(surface(0xff1b201a, 17, BORDER));
        nav.setPadding(dp(4), dp(4), dp(4), dp(4));
        String[] labels = {"Início", "PCs", "Energia", "Atualizar", "Casa"};
        for (int index = 0; index < tabs.length; index++) {
            final int tab = index;
            Button button = button(labels[index], false);
            button.setTextSize(11);
            button.setMinHeight(dp(54));
            button.setPadding(dp(2), dp(5), dp(2), dp(4));
            button.setCompoundDrawablePadding(dp(4));
            button.setMaxLines(1);
            if (index == 1) button.setContentDescription("Dispositivos");
            if (index == HOUSE_TAB) button.setContentDescription("Minha casa");
            button.setOnClickListener(view -> showTab(tab));
            tabs[index] = button;
            LinearLayout.LayoutParams size = new LinearLayout.LayoutParams(0, -2, 1f);
            size.setMargins(dp(2), 0, dp(2), 0);
            if (index == HOUSE_TAB) nav.addView(button, 1, size);
            else nav.addView(button, size);
        }
        return nav;
    }

    private void showTab(int requested) {
        selectedTab = requested >= 0 && requested < tabs.length ? requested : 0;
        for (int index = 0; index < tabs.length; index++) {
            boolean selected = index == selectedTab;
            if (index < pages.length) pages[index].setVisibility(selected ? View.VISIBLE : View.GONE);
            tabs[index].setSelected(selected);
            styleTab(tabs[index], index, selected);
        }
        if (selectedTab == 1) updateDevices();
        if (selectedTab == 2) power.refreshLocal();
        boolean showingHouse = selectedTab == HOUSE_TAB;
        scroll.setVisibility(showingHouse ? View.GONE : View.VISIBLE);
        house.show(showingHouse);
        if (showingHouse) house.updatePairing(pairing, online);
        scroll.scrollTo(0, 0);
    }

    void indicateUpdate(boolean available) {
        tabs[3].setText(available ? "Atualizar •" : "Atualizar");
        tabs[3].setContentDescription(available ? "Atualizações, nova versão disponível" : "Atualizações");
    }

    private void styleTab(Button tab, int index, boolean selected) {
        int tint = selected ? ACCENT : MUTED;
        Drawable icon = getDrawable(NAV_ICONS[index]).mutate();
        icon.setTint(tint);
        tab.setCompoundDrawablesWithIntrinsicBounds(null, icon, null, null);
        tab.setTextColor(tint);
        tab.setBackground(new RippleDrawable(ColorStateList.valueOf(0x32d8eeac),
                surface(selected ? 0xff353f2b : Color.TRANSPARENT, 11, selected ? 0xff526047 : 0), null));
    }

    private View header() {
        LinearLayout header = row();
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(dp(18), dp(12), dp(18), dp(8));
        ImageView mark = new ImageView(this);
        mark.setImageResource(R.drawable.artho_mark);
        mark.setScaleType(ImageView.ScaleType.FIT_CENTER);
        mark.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        header.addView(mark, new LinearLayout.LayoutParams(dp(28), dp(28)));
        TextView brand = label("Artho AI", 17, INK, "sans-serif-medium");
        LinearLayout.LayoutParams brandSize = new LinearLayout.LayoutParams(0, -2, 1f);
        brandSize.leftMargin = dp(10);
        header.addView(brand, brandSize);
        return header;
    }

    private View connectionPanel() {
        LinearLayout panel = column();
        panel.setPadding(dp(19), dp(19), dp(19), dp(17));
        panel.setBackground(surface(SURFACE, 18, BORDER));
        status = label("Sem conexão", 13, MUTED, "sans-serif");
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        status.setPadding(dp(9), dp(3), dp(9), dp(3));
        status.setBackground(surface(0xff2b3328, 9, BORDER));
        panel.addView(status, new LinearLayout.LayoutParams(-2, -2));
        computer = label("Conecte seu PC", 24, INK, "sans-serif-medium");
        computer.setAccessibilityHeading(true);
        panel.addView(computer, spaced(8));
        detail = label("No Windows, abra Celular e gere um QR Code.", 15, MUTED, "sans-serif");
        panel.addView(detail, spaced(5));
        primary = button("Conectar ao PC", true);
        primary.setOnClickListener(view -> {
            if (pairing == null) showPairing();
            else requestStatus();
        });
        panel.addView(primary, spaced(12));
        remoteDesktop = button("Tela remota", false);
        remoteDesktop.setVisibility(View.GONE);
        remoteDesktop.setOnClickListener(view -> startActivity(new Intent(this, RemoteDesktopActivity.class)));
        panel.addView(remoteDesktop, spaced(6));
        connectionOptions = button("Trocar conexão", false);
        connectionOptions.setOnClickListener(view -> showPairing());
        connectionOptions.setVisibility(View.GONE);
        panel.addView(connectionOptions, spaced(6));
        return panel;
    }

    void showPairing() {
        if (connecting) return;
        Dialog dialog = new Dialog(this);
        dialog.getWindow().setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));
        ScrollView scroll = new ScrollView(this);
        LinearLayout form = column();
        form.setPadding(dp(22), dp(22), dp(22), dp(18));
        form.setBackground(surface(SURFACE, 24, BORDER));
        scroll.addView(form);
        dialog.setContentView(scroll);

        TextView title = label(pairing == null ? "Conectar ao PC" : "Gerenciar conexão", 24, INK, "sans-serif-medium");
        title.setAccessibilityHeading(true);
        form.addView(title);
        form.addView(label("Na mesma rede Wi-Fi, abra Artho AI → Celular → Gerar QR Code no PC.", 15, MUTED, "sans-serif"), spaced(8));
        Button scan = button("Escanear QR Code", true);
        scan.setOnClickListener(view -> {
            dialog.dismiss();
            startActivityForResult(new Intent(this, PairingScannerActivity.class), SCAN_PAIRING);
        });
        form.addView(scan, spaced(14));
        Button manual = button("Usar código da ponte antiga", false);
        form.addView(manual, spaced(6));
        LinearLayout legacy = column();
        legacy.setVisibility(View.GONE);
        form.addView(legacy);
        manual.setOnClickListener(view -> {
            legacy.setVisibility(View.VISIBLE);
            manual.setVisibility(View.GONE);
        });
        EditText code = new EditText(this);
        code.setTextColor(INK);
        code.setHintTextColor(MUTED);
        code.setHint("Código do Windows");
        code.setContentDescription("Código de conexão do Windows");
        code.setSingleLine(true);
        code.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        code.setImeOptions(EditorInfo.IME_ACTION_GO | EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING);
        code.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO);
        code.setSaveEnabled(false);
        code.setTextSize(16);
        code.setMinHeight(dp(54));
        code.setPadding(dp(14), dp(8), dp(14), dp(8));
        code.setBackground(surface(BACKGROUND, 12, BORDER));
        legacy.addView(code, spaced(16));

        Button paste = button("Colar código", false);
        paste.setOnClickListener(view -> {
            ClipboardManager clipboard = getSystemService(ClipboardManager.class);
            ClipData clip = clipboard == null ? null : clipboard.getPrimaryClip();
            CharSequence text = clip == null || clip.getItemCount() == 0 ? null : clip.getItemAt(0).getText();
            if (text != null) {
                code.setText(text);
                code.setSelection(code.length());
                code.setError(null);
            } else code.setError("Copie o código do Windows primeiro.");
        });
        legacy.addView(paste, spaced(8));
        Runnable submit = () -> {
            try {
                pairing = StatusClient.Pairing.parse(code.getText().toString().trim());
                legacyLink = false;
            } catch (IllegalArgumentException error) {
                code.setError("Cole o código completo mostrado no Windows.");
                code.requestFocus();
                return;
            }
            code.setText("");
            dialog.dismiss();
            requestStatus();
        };
        code.setOnEditorActionListener((view, action, event) -> {
            if (action != EditorInfo.IME_ACTION_GO) return false;
            submit.run();
            return true;
        });
        Button connect = button(pairing == null ? "Conectar" : "Conectar com outro código", true);
        connect.setOnClickListener(view -> submit.run());
        legacy.addView(connect, spaced(12));
        if (pairing != null) {
            Button disconnect = button("Desconectar", false);
            disconnect.setOnClickListener(view -> {
                dialog.dismiss();
                disconnect();
            });
            form.addView(disconnect, spaced(6));
        }
        Button cancel = button("Voltar", false);
        cancel.setOnClickListener(view -> dialog.dismiss());
        form.addView(cancel, spaced(6));
        form.setFocusableInTouchMode(true);
        form.requestFocus();
        dialog.getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE |
                WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN);
        dialog.show();
        dialog.getWindow().setLayout(Math.min(dp(480), getResources().getDisplayMetrics().widthPixels - dp(32)), -2);
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != SCAN_PAIRING || result != RESULT_OK || data == null) return;
        String text = data.getStringExtra(PairingScannerActivity.EXTRA_CODE);
        if (text == null) return;
        try {
            pairing = StatusClient.Pairing.parse(text);
            legacyLink = false;
            requestStatus();
        } catch (IllegalArgumentException error) {
            detail.setText("QR Code inválido. Gere outro na aba Celular do Artho AI no PC.");
        }
    }

    private void requestStatus() { requestStatus(true); }

    private void requestStatus(boolean showProgress) {
        if (pairing == null || connecting) return;
        StatusClient.Pairing target = pairing;
        boolean useLegacy = legacyLink;
        connecting = true;
        connectionTimer.removeCallbacks(reconnect);
        if (showProgress) {
            setStatus("Consultando o PC", ACCENT);
            detail.setText("Aguardando a resposta do Windows…");
            primary.setEnabled(false);
            primary.setText("Consultando…");
        }
        connectionOptions.setEnabled(false);
        worker.execute(() -> {
            try {
                StatusClient.Pairing link = target;
                if (!target.durable && !useLegacy) link = establishLink(target);
                JSONObject result = StatusClient.readStatus(link);
                if (link.durable) PairingVault.accept(getApplicationContext(), link);
                StatusClient.Pairing connected = link;
                show(() -> {
                    if (pairing != target) return;
                    pairing = connected;
                    legacyLink = !connected.durable;
                    online = true;
                    render(result);
                    reconnectAfter(12000);
                });
            } catch (SecurityException error) {
                if (!target.durable) PairingVault.discardPending(getApplicationContext());
                StatusClient.Pairing fallback = PairingVault.invalidate(getApplicationContext(), target);
                show(() -> {
                    if (pairing != target) return;
                    pairing = fallback;
                    legacyLink = false;
                    online = false;
                    power.pairingChanged(null);
                    showEmpty("Pareamento indisponível", ALERT, "Se o vínculo foi removido, escaneie outro QR Code no PC.");
                    if (fallback != null && visible) requestStatus(true);
                });
            } catch (SSLHandshakeException error) {
                show(() -> {
                    if (pairing != target) return;
                    online = false;
                    power.pairingChanged(null);
                    showEmpty("PC diferente do pareamento", ALERT, "Confira se o endereço de rede do Windows mudou.");
                    if (target.durable) reconnectAfter(15000);
                });
            } catch (Exception error) {
                show(() -> {
                    if (pairing != target) return;
                    online = false;
                    power.pairingChanged(null);
                    showEmpty("Aguardando o PC", MUTED, "O app tenta reconectar quando o Artho AI voltar à rede.");
                    reconnectAfter(7000);
                });
            }
        });
    }

    private StatusClient.Pairing establishLink(StatusClient.Pairing invitation) throws Exception {
        StatusClient.Pairing proposed = PairingVault.pendingFor(getApplicationContext(), invitation);
        if (proposed == null) {
            proposed = invitation.generateCredential();
            PairingVault.stage(getApplicationContext(), proposed);
        }
        try { StatusClient.enroll(invitation, proposed); }
        catch (StatusClient.OldPairingProtocol old) {
            PairingVault.discardPending(getApplicationContext());
            return invitation;
        } catch (Exception error) {
            // If the response was lost after the PC committed, the staged credential still works.
            try { StatusClient.readStatus(proposed); }
            catch (Exception unavailable) { throw error; }
        }
        return proposed;
    }

    private void reconnectAfter(long delay) {
        if (visible && pairing != null) connectionTimer.postDelayed(reconnect, delay);
    }

    private void render(JSONObject result) {
        metrics.setVisibility(View.VISIBLE);
        double total = result.optDouble("totalMemoryGb", 0);
        double used = result.optDouble("memoryInUseGb", -1);
        memoryBar.setVisibility(total > 0 && used >= 0 ? View.VISIBLE : View.GONE);
        if (memoryBar.getVisibility() == View.VISIBLE) {
            memoryBar.setProgress((int) Math.round(100 * Math.min(1, used / total)));
            memory.setText(String.format(BRAZIL, "%.1f / %.1f GB", used, total));
        } else memory.setText("Indisponível");
        int count = result.optInt("processorCount", 0);
        cpu.setText(count > 0 ? count + " lógicos" : "Indisponível");
        long hours = result.optLong("uptimeHours", -1);
        String elapsed = hours >= 24 ? (hours / 24) + " d " + (hours % 24) + " h" : hours + " h";
        uptime.setText(hours < 0 ? "Indisponível" : elapsed);
        network.setText(!result.has("networkAvailable") ? "Indisponível"
                : result.optBoolean("networkAvailable") ? "Interface ativa" : "Sem interface ativa");
        computer.setText(result.optString("name", "Computador Windows"));
        String version = result.optString("windowsVersion", "").replace("Microsoft Windows NT ", "");
        detail.setText("Windows " + version + "\nÚltima leitura às "
                + DateFormat.getTimeInstance(DateFormat.SHORT, BRAZIL).format(new Date()));
        setStatus(pairing.durable ? "Conectado · pareamento salvo" : "Conectado · sessão temporária", GREEN);
        power.pairingChanged(pairing);
        restoreActions();
    }

    void disconnect() {
        StatusClient.Pairing old = pairing;
        pairing = null;
        online = false;
        legacyLink = false;
        connectionTimer.removeCallbacks(reconnect);
        power.forget();
        showEmpty("Sem conexão", MUTED, "Pareie seu celular para consultar o computador.");
        worker.execute(() -> {
            PairingVault.clear(getApplicationContext());
            if (old != null && old.durable) {
                try { StatusClient.request(old, "POST", "/v1/pair/revoke"); }
                catch (Exception ignored) {
                    show(() -> {
                        if (pairing == null) detail.setText("Vínculo removido do celular. Se o PC estiver offline, desvincule também no Windows.");
                    });
                }
            }
        });
    }

    private void showEmpty(String title, int color, String message) {
        setStatus(title, color);
        computer.setText(pairing != null && pairing.durable ? "Pareamento salvo" : "Nenhum PC conectado");
        detail.setText(message);
        for (TextView metric : new TextView[] { memory, cpu, uptime, network }) metric.setText("—");
        memoryBar.setVisibility(View.GONE);
        metrics.setVisibility(View.GONE);
        restoreActions();
    }

    private void setStatus(String text, int color) {
        status.setText(text);
        status.setTextColor(color);
    }

    private void restoreActions() {
        connecting = false;
        primary.setText(pairing == null ? "Conectar ao PC" : "Atualizar dados");
        primary.setEnabled(true);
        connectionOptions.setEnabled(true);
        connectionOptions.setVisibility(pairing == null ? View.GONE : View.VISIBLE);
        remoteDesktop.setVisibility(online && pairing != null && pairing.durable ? View.VISIBLE : View.GONE);
        house.updatePairing(pairing, online);
        if (selectedTab == 1) updateDevices();
    }

    void show(Runnable action) {
        runOnUiThread(() -> {
            if (!isFinishing() && !isDestroyed()) action.run();
        });
    }

    private TextView information(LinearLayout list, String title) {
        LinearLayout item = row();
        item.setGravity(Gravity.CENTER_VERTICAL);
        item.setPadding(0, dp(11), 0, dp(11));
        item.addView(label(title, 14, MUTED, "sans-serif"), new LinearLayout.LayoutParams(0, -2, 1f));
        TextView result = label("—", 14, INK, "sans-serif-medium");
        item.addView(result);
        list.addView(item);
        return result;
    }

    View divider() {
        View line = new View(this);
        line.setBackgroundColor(BORDER);
        line.setLayoutParams(new LinearLayout.LayoutParams(-1, dp(1)));
        return line;
    }

    Button button(String title, boolean prominent) {
        Button button = new Button(this);
        button.setAllCaps(false);
        button.setText(title);
        button.setTextSize(15);
        button.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        button.setMinHeight(dp(44));
        button.setPadding(dp(14), dp(9), dp(14), dp(9));
        button.setStateListAnimator(null);
        button.setBackgroundTintList(null);
        decorateButton(button, prominent);
        return button;
    }

    private void decorateButton(Button button, boolean prominent) {
        int color = prominent ? BACKGROUND : INK;
        button.setTextColor(new ColorStateList(
                new int[][]{{-android.R.attr.state_enabled}, {}}, new int[]{prominent ? 0xff687553 : MUTED, color}));
        int fill = prominent ? ACCENT : SURFACE;
        button.setBackground(new RippleDrawable(ColorStateList.valueOf(prominent ? 0x30141715 : 0x35d8eeac),
                surface(fill, 11, prominent ? 0 : BORDER), null));
    }

    GradientDrawable surface(int color, int radius, int stroke) {
        GradientDrawable result = new GradientDrawable();
        result.setColor(color);
        result.setCornerRadius(dp(radius));
        if (stroke != 0) result.setStroke(dp(1), stroke);
        return result;
    }

    LinearLayout column() {
        LinearLayout result = new LinearLayout(this);
        result.setOrientation(LinearLayout.VERTICAL);
        return result;
    }

    private LinearLayout row() { return new LinearLayout(this); }

    TextView label(String text, int size, int color, String font) {
        TextView view = new TextView(this);
        view.setText(text);
        view.setTextSize(size);
        view.setTextColor(color);
        view.setTypeface(Typeface.create(font, Typeface.NORMAL));
        return view;
    }

    LinearLayout.LayoutParams spaced(int top) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2);
        params.topMargin = dp(top);
        return params;
    }

    int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    @Override public Object onRetainNonConfigurationInstance() { return pairing; }

    @Override protected void onDestroy() {
        house.close();
        connectionTimer.removeCallbacks(reconnect);
        if (updates != null) updates.close();
        worker.shutdownNow();
        super.onDestroy();
    }
}
