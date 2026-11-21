package com.arthostudio.arthoai;

import android.app.Activity;
import android.app.Dialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
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
    private static final int BACKGROUND = 0xff080d15;
    private static final int HERO = 0xff030810;
    private static final int SURFACE = 0xff101a27;
    private static final int BORDER = 0xff253448;
    private static final int INK = 0xffedf6fd;
    private static final int MUTED = 0xffa0b3c7;
    private static final int ACCENT = 0xff99ddff;
    private static final int GREEN = 0xff94e4c3;
    private static final int ALERT = 0xffffbcad;
    private static final Locale BRAZIL = new Locale("pt", "BR");
    private static final int SCAN_PAIRING = 17;

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final ExecutorService updateWorker = Executors.newSingleThreadExecutor();
    private AndroidUpdater.Release availableUpdate;
    private TextView updateStatus;
    private Button updateButton;
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
    private Button devicesButton;

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().setStatusBarColor(BACKGROUND);
        getWindow().setNavigationBarColor(BACKGROUND);
        if (Build.VERSION.SDK_INT >= 30) getWindow().setDecorFitsSystemWindows(false);

        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(BACKGROUND);
        scroll.setFillViewport(true);
        scroll.setClipToPadding(false);
        if (Build.VERSION.SDK_INT >= 30) {
            scroll.setOnApplyWindowInsetsListener((view, insets) -> {
                Insets safe = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(safe.left, safe.top, safe.right, safe.bottom);
                return insets;
            });
        }
        FrameLayout frame = new FrameLayout(this);
        scroll.addView(frame, new ScrollView.LayoutParams(-1, -2));
        LinearLayout page = column();
        frame.addView(page, new FrameLayout.LayoutParams(-1, -2, Gravity.TOP | Gravity.CENTER_HORIZONTAL));
        frame.addOnLayoutChangeListener((view, left, top, right, bottom, oldLeft, oldTop, oldRight, oldBottom) -> {
            int width = Math.min(dp(620), right - left);
            FrameLayout.LayoutParams params = (FrameLayout.LayoutParams) page.getLayoutParams();
            if (width > 0 && params.width != width) {
                params.width = width;
                page.setLayoutParams(params);
            }
        });
        page.setPadding(dp(20), dp(16), dp(20), dp(28));
        setContentView(scroll);
        scroll.requestApplyInsets();

        page.addView(header());
        page.addView(hero(), spaced(20));

        Button update = button("Conectar ao PC", true);
        primary = update;
        update.setOnClickListener(view -> {
            if (pairing == null) showPairing();
            else requestStatus();
        });
        page.addView(update, spaced(14));
        connectionOptions = button("Gerenciar conexão", false);
        connectionOptions.setOnClickListener(view -> showPairing());
        connectionOptions.setVisibility(View.GONE);
        page.addView(connectionOptions, spaced(6));
        devicesButton = button("Dispositivos", false);
        devicesButton.setVisibility(View.GONE);
        devicesButton.setOnClickListener(view -> {
            if (pairing != null) new PcDevicesDialog(this, pairing, worker).show();
        });
        page.addView(devicesButton, spaced(6));

        TextView section = label("Visão geral", 23, INK, "sans-serif-medium");
        section.setAccessibilityHeading(true);
        page.addView(section, spaced(24));
        LinearLayout performance = row();
        LinearLayout memoryCard = metric("Memória em uso");
        memory = value(memoryCard);
        memoryBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        memoryBar.setMax(100);
        memoryBar.setProgressTintList(ColorStateList.valueOf(ACCENT));
        memoryBar.setProgressBackgroundTintList(ColorStateList.valueOf(BORDER));
        memoryBar.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        memoryBar.setVisibility(View.GONE);
        LinearLayout.LayoutParams barParams = new LinearLayout.LayoutParams(-1, dp(5));
        barParams.topMargin = dp(10);
        memoryCard.addView(memoryBar, barParams);
        LinearLayout cpuCard = metric("Processador");
        cpu = value(cpuCard);
        addMetricPair(performance, memoryCard, cpuCard);
        page.addView(performance, spaced(12));

        LinearLayout activity = row();
        LinearLayout uptimeCard = metric("Tempo ligado");
        uptime = value(uptimeCard);
        LinearLayout networkCard = metric("Rede do PC");
        network = value(networkCard);
        addMetricPair(activity, uptimeCard, networkCard);
        page.addView(activity, spaced(10));

        TextView updates = label("Atualizações", 20, INK, "sans-serif-medium");
        updates.setAccessibilityHeading(true);
        page.addView(updates, spaced(24));
        updateStatus = label("", 13, MUTED, "sans-serif");
        updateStatus.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        page.addView(updateStatus, spaced(6));
        updateButton = button("Verificar atualizações", false);
        updateButton.setOnClickListener(view -> {
            if (availableUpdate == null) checkUpdates();
            else downloadUpdate(availableUpdate);
        });
        page.addView(updateButton, spaced(8));
        checkUpdates();

        if (saved != null) {
            Object previous = getLastNonConfigurationInstance();
            if (previous instanceof StatusClient.Pairing) {
                pairing = (StatusClient.Pairing) previous;
                requestStatus();
            }
        }
    }

    private void checkUpdates() {
        updateButton.setEnabled(false);
        updateStatus.setText("Verificando novas versões no GitHub…");
        updateWorker.execute(() -> {
            try {
                String version = getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
                AndroidUpdater.Release found = AndroidUpdater.check(version);
                runOnUiThread(() -> {
                    if (isFinishing() || isDestroyed()) return;
                    availableUpdate = found;
                    updateStatus.setText(found == null ? "Versão " + version + " em dia"
                            : "Versão " + found.version + " disponível");
                    updateButton.setText(found == null ? "Verificar atualizações" : "Baixar e instalar");
                    updateButton.setEnabled(true);
                });
            } catch (Exception e) {
                runOnUiThread(() -> updateMessage("Não foi possível consultar o GitHub. Tente novamente."));
            }
        });
    }

    private void downloadUpdate(AndroidUpdater.Release release) {
        PackageManager manager = getPackageManager();
        if (!manager.canRequestPackageInstalls()) {
            Intent settings = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.fromParts("package", getPackageName(), null));
            try {
                startActivity(settings);
                updateMessage("Permita a instalação para o Artho AI, volte e toque em Baixar e instalar.");
            } catch (android.content.ActivityNotFoundException e) {
                updateMessage("A opção de instalação pelo Artho AI não está disponível neste aparelho.");
            }
            return;
        }
        updateButton.setEnabled(false);
        updateStatus.setText("Baixando e verificando a versão " + release.version + "…");
        updateWorker.execute(() -> {
            try {
                AndroidUpdater.download(getApplicationContext(), release);
                runOnUiThread(this::openUpdateInstaller);
            } catch (Exception e) {
                runOnUiThread(() -> updateMessage("Não foi possível baixar ou validar a atualização. Tente novamente."));
            }
        });
    }

    private void openUpdateInstaller() {
        if (isFinishing() || isDestroyed()) return;
        Uri uri = new Uri.Builder().scheme("content").authority(getPackageName() + ".updates")
                .appendPath(UpdateApkProvider.APK_NAME).build();
        Intent install = new Intent(Intent.ACTION_VIEW).setDataAndType(uri, UpdateApkProvider.MIME_TYPE)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try {
            startActivity(install);
            updateMessage("Confirme a atualização na tela do Android.");
        } catch (android.content.ActivityNotFoundException | SecurityException e) {
            updateMessage("O Android não abriu o instalador. Confira a permissão do Artho AI e tente de novo.");
        }
    }

    private void updateMessage(String message) {
        if (isFinishing() || isDestroyed()) return;
        updateStatus.setText(message);
        updateButton.setEnabled(true);
    }

    private View header() {
        LinearLayout header = row();
        header.setGravity(Gravity.CENTER_VERTICAL);
        ImageView mark = new ImageView(this);
        mark.setImageResource(R.drawable.artho_mark);
        mark.setScaleType(ImageView.ScaleType.FIT_CENTER);
        mark.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        header.addView(mark, new LinearLayout.LayoutParams(dp(36), dp(36)));
        TextView brand = label("Artho AI", 20, INK, "sans-serif-medium");
        LinearLayout.LayoutParams brandSize = new LinearLayout.LayoutParams(0, -2, 1f);
        brandSize.leftMargin = dp(10);
        header.addView(brand, brandSize);
        TextView local = label("Rede local", 12, MUTED, "sans-serif-medium");
        local.setPadding(dp(11), dp(6), dp(11), dp(6));
        local.setBackground(surface(SURFACE, 14, BORDER));
        header.addView(local);
        return header;
    }

    private View hero() {
        LinearLayout panel = column();
        panel.setBackground(surface(HERO, 26, BORDER));
        panel.setClipToOutline(true);
        panel.setPadding(dp(1), dp(1), dp(1), dp(1));

        FrameLayout artwork = new FrameLayout(this);
        ImageView nucleus = new ImageView(this);
        nucleus.setImageResource(R.drawable.nucleus);
        nucleus.setScaleType(ImageView.ScaleType.CENTER_CROP);
        nucleus.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        artwork.addView(nucleus, new FrameLayout.LayoutParams(-1, -1));
        View fade = new View(this);
        fade.setBackground(new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                new int[] { Color.TRANSPARENT, Color.TRANSPARENT, HERO }));
        artwork.addView(fade, new FrameLayout.LayoutParams(-1, -1));
        panel.addView(artwork, new LinearLayout.LayoutParams(-1, dp(180)));

        LinearLayout text = column();
        text.setPadding(dp(18), dp(4), dp(18), dp(18));
        status = label("●  Sem conexão", 13, MUTED, "sans-serif-medium");
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        text.addView(status);
        computer = label("Nenhum PC conectado", 26, INK, "sans-serif-medium");
        computer.setAccessibilityHeading(true);
        text.addView(computer, spaced(7));
        detail = label("Pareie seu celular para consultar o computador.", 14, MUTED, "sans-serif");
        text.addView(detail, spaced(5));
        panel.addView(text);
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
                pairing = null;
                dialog.dismiss();
                showEmpty("●  Sem conexão", MUTED, "Pareie seu celular para consultar o computador.");
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
                show(() -> render(result));
            } catch (SSLHandshakeException error) {
                show(() -> invalidPairing("Gere um novo QR Code no Artho AI para Windows."));
            } catch (SecurityException error) {
                show(() -> invalidPairing("A sessão terminou ou o QR Code expirou. Gere outro no PC."));
            } catch (Exception error) {
                show(() -> showEmpty("●  PC indisponível", ALERT,
                        "Confira se a sessão está aberta no PC e se o celular está na mesma rede."));
            }
        });
    }

    private void render(JSONObject result) {
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
        restoreActions();
    }

    private void invalidPairing(String message) {
        pairing = null;
        showEmpty("●  Código inválido", ALERT, message);
    }

    private void showEmpty(String title, int color, String message) {
        setStatus(title, color);
        computer.setText("Nenhum PC conectado");
        detail.setText(message);
        for (TextView metric : new TextView[] { memory, cpu, uptime, network }) metric.setText("—");
        memoryBar.setVisibility(View.GONE);
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
        devicesButton.setVisibility(pairing == null ? View.GONE : View.VISIBLE);
    }

    private void show(Runnable action) {
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

    private Button button(String title, boolean prominent) {
        Button button = new Button(this);
        button.setAllCaps(false);
        button.setText(title);
        button.setTextSize(15);
        button.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        button.setTextColor(ColorStateList.valueOf(prominent ? BACKGROUND : INK));
        button.setMinHeight(dp(50));
        button.setPadding(dp(14), dp(9), dp(14), dp(9));
        button.setStateListAnimator(null);
        button.setBackgroundTintList(null);
        int fill = prominent ? ACCENT : SURFACE;
        button.setBackground(new RippleDrawable(ColorStateList.valueOf(prominent ? 0x33081016 : 0x3379ceff),
                surface(fill, 14, prominent ? 0 : BORDER), null));
        return button;
    }

    private GradientDrawable surface(int color, int radius, int stroke) {
        GradientDrawable result = new GradientDrawable();
        result.setColor(color);
        result.setCornerRadius(dp(radius));
        if (stroke != 0) result.setStroke(dp(1), stroke);
        return result;
    }

    private LinearLayout column() {
        LinearLayout result = new LinearLayout(this);
        result.setOrientation(LinearLayout.VERTICAL);
        return result;
    }

    private LinearLayout row() { return new LinearLayout(this); }

    private TextView label(String text, int size, int color, String font) {
        TextView view = new TextView(this);
        view.setText(text);
        view.setTextSize(size);
        view.setTextColor(color);
        view.setTypeface(Typeface.create(font, Typeface.NORMAL));
        return view;
    }

    private LinearLayout.LayoutParams spaced(int top) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2);
        params.topMargin = dp(top);
        return params;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    @Override public Object onRetainNonConfigurationInstance() { return pairing; }

    @Override protected void onDestroy() {
        updateWorker.shutdownNow();
        worker.shutdownNow();
        super.onDestroy();
    }
}
