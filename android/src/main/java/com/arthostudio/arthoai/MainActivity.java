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
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.os.Build;
import android.os.Bundle;
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
    private static final int BACKGROUND = 0xff141715;
    private static final int HERO = 0xff29392f;
    static final int SURFACE = 0xff202622;
    static final int BORDER = 0xff36423a;
    static final int INK = 0xfff1f4ee;
    static final int MUTED = 0xffb4bfb6;
    static final int ACCENT = 0xffb6dfc2;
    private static final int GREEN = ACCENT;
    private static final int ALERT = 0xffffbcad;
    private static final Locale BRAZIL = new Locale("pt", "BR");
    private static final int SCAN_PAIRING = 17;

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private ScrollView scroll;
    private final View[] pages = new View[4];
    private final Button[] tabs = new Button[4];
    private int selectedTab;
    private UpdatesPanel updates;
    private PowerPanel power;
    private LinearLayout deviceContent;
    private StatusClient.Pairing shownPairing;
    private boolean deviceInitialized;
    private LinearLayout metrics;
    private StatusClient.Pairing pairing;
    private boolean connecting;
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

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().setStatusBarColor(BACKGROUND);
        getWindow().setNavigationBarColor(BACKGROUND);
        if (Build.VERSION.SDK_INT >= 30) getWindow().setDecorFitsSystemWindows(false);

        LinearLayout shell = column();
        shell.setBackgroundColor(BACKGROUND);
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
        content.setPadding(dp(20), dp(14), dp(20), dp(24));
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
        shell.addView(navigation());
        setContentView(shell);
        shell.requestApplyInsets();

        if (saved != null && getLastNonConfigurationInstance() instanceof StatusClient.Pairing) {
            pairing = (StatusClient.Pairing) getLastNonConfigurationInstance();
            requestStatus();
        }
        showTab(saved == null ? 0 : saved.getInt("tab", 0));
        WakeSchedules.restore(this);
        updates.check();
    }

    @Override protected void onResume() {
        super.onResume();
        if (updates != null) updates.resumed();
        if (power != null) power.refreshLocal();
    }

    @Override protected void onSaveInstanceState(Bundle state) {
        super.onSaveInstanceState(state);
        state.putInt("tab", selectedTab);
    }

    private View dashboard() {
        LinearLayout page = column();
        TextView title = label("Painel do PC", 32, INK, "sans-serif-medium");
        title.setAccessibilityHeading(true);
        page.addView(title);
        page.addView(hero(), spaced(16));

        metrics = column();
        metrics.setVisibility(View.GONE);
        TextView section = label("Estado do PC", 21, INK, "sans-serif-medium");
        section.setAccessibilityHeading(true);
        metrics.addView(section);
        LinearLayout performance = row();
        LinearLayout memoryCard = metric("Memória em uso");
        memory = value(memoryCard);
        memoryBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        memoryBar.setMax(100);
        memoryBar.setProgressTintList(ColorStateList.valueOf(ACCENT));
        memoryBar.setProgressBackgroundTintList(ColorStateList.valueOf(BORDER));
        memoryBar.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        memoryBar.setVisibility(View.GONE);
        LinearLayout.LayoutParams bar = new LinearLayout.LayoutParams(-1, dp(5));
        bar.topMargin = dp(8);
        memoryCard.addView(memoryBar, bar);
        LinearLayout cpuCard = metric("Processador");
        cpu = value(cpuCard);
        addMetricPair(performance, memoryCard, cpuCard);
        metrics.addView(performance, spaced(10));

        LinearLayout activity = row();
        LinearLayout uptimeCard = metric("Tempo ligado");
        uptime = value(uptimeCard);
        LinearLayout networkCard = metric("Rede do PC");
        network = value(networkCard);
        addMetricPair(activity, uptimeCard, networkCard);
        metrics.addView(activity, spaced(10));
        page.addView(metrics, spaced(22));
        return page;
    }

    private View devicePage() {
        LinearLayout page = column();
        TextView title = label("Dispositivos", 32, INK, "sans-serif-medium");
        title.setAccessibilityHeading(true);
        page.addView(title);
        page.addView(label("PCs pareados e horários semanais.", 15, MUTED, "sans-serif"), spaced(6));
        deviceContent = column();
        page.addView(deviceContent, spaced(17));
        return page;
    }

    private void updateDevices() {
        if (deviceInitialized && shownPairing == pairing) return;
        deviceInitialized = true;
        shownPairing = pairing;
        deviceContent.removeAllViews();
        if (pairing == null) {
            deviceContent.addView(label("Conecte o celular ao PC de controle para ver os dispositivos.", 15, MUTED, "sans-serif"));
            Button connect = button("Conectar ao PC", true);
            connect.setOnClickListener(view -> showPairing());
            deviceContent.addView(connect, spaced(12));
            return;
        }
        DevicesPanel panel = new DevicesPanel(this, pairing, worker);
        deviceContent.addView(panel.view());
        panel.load();
    }

    private View navigation() {
        LinearLayout nav = row();
        nav.setBackgroundColor(SURFACE);
        nav.setPadding(dp(10), dp(8), dp(10), dp(8));
        String[] labels = {"Início", "Dispositivos", "Energia", "Atualizar"};
        for (int index = 0; index < tabs.length; index++) {
            final int tab = index;
            Button button = button(labels[index], false);
            button.setTextSize(12);
            button.setPadding(dp(4), dp(8), dp(4), dp(8));
            button.setOnClickListener(view -> showTab(tab));
            tabs[index] = button;
            LinearLayout.LayoutParams size = new LinearLayout.LayoutParams(0, -2, 1f);
            size.setMargins(dp(3), 0, dp(3), 0);
            nav.addView(button, size);
        }
        return nav;
    }

    private void showTab(int requested) {
        selectedTab = requested >= 0 && requested < tabs.length ? requested : 0;
        for (int index = 0; index < tabs.length; index++) {
            boolean selected = index == selectedTab;
            pages[index].setVisibility(selected ? View.VISIBLE : View.GONE);
            tabs[index].setSelected(selected);
            decorateButton(tabs[index], selected);
        }
        if (selectedTab == 1) updateDevices();
        if (selectedTab == 2) power.refreshLocal();
        scroll.scrollTo(0, 0);
    }

    void indicateUpdate(boolean available) {
        tabs[3].setText(available ? "Atualizar •" : "Atualizar");
        tabs[3].setContentDescription(available ? "Atualizações, nova versão disponível" : "Atualizações");
    }

    private View header() {
        LinearLayout header = row();
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(dp(20), dp(10), dp(20), dp(10));
        ImageView mark = new ImageView(this);
        mark.setImageResource(R.drawable.artho_mark);
        mark.setScaleType(ImageView.ScaleType.FIT_CENTER);
        mark.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        header.addView(mark, new LinearLayout.LayoutParams(dp(36), dp(36)));
        TextView brand = label("Artho AI", 20, INK, "sans-serif-medium");
        LinearLayout.LayoutParams brandSize = new LinearLayout.LayoutParams(0, -2, 1f);
        brandSize.leftMargin = dp(10);
        header.addView(brand, brandSize);
        return header;
    }

    private View hero() {
        LinearLayout panel = column();
        panel.setBackground(surface(HERO, 18, BORDER));
        panel.setPadding(dp(19), dp(18), dp(19), dp(18));
        status = label("Sem conexão", 13, ACCENT, "sans-serif-medium");
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        panel.addView(status);
        computer = label("Conecte seu PC", 27, INK, "sans-serif-medium");
        computer.setAccessibilityHeading(true);
        panel.addView(computer, spaced(7));
        detail = label("Abra a aba Celular no Artho AI para Windows e gere um QR Code.", 15, MUTED, "sans-serif");
        panel.addView(detail, spaced(5));
        primary = button("Conectar ao PC", true);
        primary.setOnClickListener(view -> {
            if (pairing == null) showPairing();
            else requestStatus();
        });
        panel.addView(primary, spaced(14));
        connectionOptions = button("Gerenciar conexão", false);
        connectionOptions.setOnClickListener(view -> showPairing());
        connectionOptions.setVisibility(View.GONE);
        panel.addView(connectionOptions, spaced(6));
        return panel;
    }

    private void showPairing() {
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
            requestStatus();
        } catch (IllegalArgumentException error) {
            detail.setText("QR Code inválido. Gere outro na aba Celular do Artho AI no PC.");
        }
    }

    private void requestStatus() {
        if (pairing == null || connecting) return;
        StatusClient.Pairing target = pairing;
        connecting = true;
        setStatus("●  Consultando o PC", ACCENT);
        detail.setText("Aguardando a resposta do Windows…");
        primary.setEnabled(false);
        primary.setText("Consultando…");
        connectionOptions.setEnabled(false);
        worker.execute(() -> {
            try {
                JSONObject result = StatusClient.readStatus(target);
                show(() -> { if (pairing == target) render(result); });
            } catch (SSLHandshakeException error) {
                show(() -> { if (pairing == target) invalidPairing("Gere um novo QR Code no Artho AI para Windows."); });
            } catch (SecurityException error) {
                show(() -> { if (pairing == target) invalidPairing("A sessão terminou ou o QR Code expirou. Gere outro no PC."); });
            } catch (Exception error) {
                show(() -> {
                    if (pairing != target) return;
                    power.pairingChanged(null);
                    showEmpty("●  PC indisponível", ALERT, "Confira se a sessão está aberta no PC e se o celular está na mesma rede.");
                });
            }
        });
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
        setStatus("●  Dados recebidos", GREEN);
        power.pairingChanged(pairing);
        restoreActions();
    }

    private void invalidPairing(String message) {
        pairing = null;
        power.pairingChanged(null);
        showEmpty("●  Código inválido", ALERT, message);
    }

    void disconnect() {
        pairing = null;
        power.forget();
        showEmpty("●  Sem conexão", MUTED, "Pareie seu celular para consultar o computador.");
    }

    private void showEmpty(String title, int color, String message) {
        setStatus(title, color);
        computer.setText("Nenhum PC conectado");
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
        if (selectedTab == 1) updateDevices();
    }

    void show(Runnable action) {
        runOnUiThread(() -> {
            if (!isFinishing() && !isDestroyed()) action.run();
        });
    }

    private LinearLayout metric(String title) {
        LinearLayout card = column();
        card.setMinimumHeight(dp(94));
        card.setBackground(surface(SURFACE, 16, BORDER));
        card.setPadding(dp(13), dp(13), dp(13), dp(13));
        card.addView(label(title, 13, MUTED, "sans-serif-medium"));
        return card;
    }

    private TextView value(LinearLayout card) {
        TextView result = label("—", 18, INK, "sans-serif-medium");
        card.addView(result, spaced(6));
        return result;
    }

    private void addMetricPair(LinearLayout row, View left, View right) {
        LinearLayout.LayoutParams first = new LinearLayout.LayoutParams(0, -1, 1);
        first.rightMargin = dp(10);
        row.addView(left, first);
        row.addView(right, new LinearLayout.LayoutParams(0, -1, 1));
    }

    Button button(String title, boolean prominent) {
        Button button = new Button(this);
        button.setAllCaps(false);
        button.setText(title);
        button.setTextSize(15);
        button.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        button.setMinHeight(dp(50));
        button.setPadding(dp(14), dp(9), dp(14), dp(9));
        button.setStateListAnimator(null);
        button.setBackgroundTintList(null);
        decorateButton(button, prominent);
        return button;
    }

    private void decorateButton(Button button, boolean prominent) {
        int color = prominent ? BACKGROUND : INK;
        button.setTextColor(new ColorStateList(
                new int[][]{{-android.R.attr.state_enabled}, {}}, new int[]{prominent ? 0xff4e6957 : MUTED, color}));
        int fill = prominent ? ACCENT : SURFACE;
        button.setBackground(new RippleDrawable(ColorStateList.valueOf(prominent ? 0x30141715 : 0x33b6dfc2),
                surface(fill, 12, prominent ? 0 : BORDER), null));
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
        if (updates != null) updates.close();
        worker.shutdownNow();
        super.onDestroy();
    }
}
