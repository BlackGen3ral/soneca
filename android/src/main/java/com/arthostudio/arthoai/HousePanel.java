package com.arthostudio.arthoai;

import android.text.InputFilter;
import android.text.InputType;
import android.view.Gravity;
import android.webkit.RenderProcessGoneDetail;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.io.ByteArrayInputStream;
import java.util.Arrays;
import java.util.Collections;
import java.util.concurrent.ExecutorService;

final class HousePanel extends FrameLayout {
    private static final String PAGE = "https://appassets.androidplatform.net/MinhaCasa.html";
    private final MainActivity activity;
    private final ExecutorService worker;
    private final LinearLayout lock;
    private final EditText password;
    private final Button unlock;
    private final TextView feedback;
    private StatusClient.Pairing paired;
    private HousePackage model;
    private boolean syncing;
    private int viewGeneration;
    private WebView browser;
    private byte[] unlockedPage;
    private boolean refreshOnOpen;

    HousePanel(MainActivity activity, ExecutorService worker) {
        super(activity);
        this.activity = activity;
        this.worker = worker;
        lock = new LinearLayout(activity);
        lock.setOrientation(LinearLayout.VERTICAL);
        int padding = Math.round(28 * getResources().getDisplayMetrics().density);
        lock.setPadding(padding, padding, padding, padding);
        feedback = new TextView(activity);
        feedback.setTextColor(MainActivity.MUTED);
        feedback.setTextSize(15);
        lock.addView(feedback);
        password = new EditText(activity);
        password.setTextColor(MainActivity.INK);
        password.setHintTextColor(MainActivity.MUTED);
        password.setHint("Senha da maquete");
        password.setSingleLine(true);
        password.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        password.setFilters(new InputFilter[]{ new InputFilter.LengthFilter(128) });
        password.setSaveEnabled(false);
        password.setImportantForAutofill(IMPORTANT_FOR_AUTOFILL_NO);
        password.setImeOptions(android.view.inputmethod.EditorInfo.IME_ACTION_GO);
        password.setOnEditorActionListener((view, action, event) -> {
            if (action != android.view.inputmethod.EditorInfo.IME_ACTION_GO) return false;
            unlock();
            return true;
        });
        lock.addView(password);
        unlock = activity.button("Abrir maquete", true);
        unlock.setOnClickListener(view -> unlock());
        lock.addView(unlock);
        addView(lock, new LayoutParams(-1, -2, Gravity.CENTER));
        setVisibility(GONE);
    }

    void show(boolean visible) {
        setVisibility(visible ? VISIBLE : GONE);
        if (!visible) releaseViewer();
        else if (browser == null) {
            if (refreshOnOpen) clearUnlockedPage();
            if (unlockedPage == null) showLocked(null);
            else open(unlockedPage);
        }
    }

    void updatePairing(StatusClient.Pairing link, boolean online) {
        if (link != null && !link.durable) link = null;
        if (paired != link && (paired == null || link == null || !paired.token.equals(link.token))) {
            close();
            HousePackage former = model;
            if (former != null) worker.execute(former::delete);
            paired = link;
            model = link == null ? null : new HousePackage(activity, link);
            syncing = false;
            showLocked(null);
        }
        if (!online || model == null || syncing) return;
        syncing = true;
        HousePackage target = model;
        StatusClient.Pairing connected = link;
        worker.execute(() -> {
            boolean hadCache = target.exists();
            boolean updated = sync(target, connected);
            activity.show(() -> {
                if (model != target) return;
                syncing = false;
                if (updated && unlockedPage != null) refreshOnOpen = true;
                if (hadCache != target.exists()) {
                    if (!target.exists()) close();
                    if (browser == null) showLocked(null);
                }
            });
        });
    }

    private boolean sync(HousePackage target, StatusClient.Pairing connected) {
        try { return target.sync(connected); }
        catch (Exception unavailable) { return false; }
    }

    private void showLocked(String message) {
        boolean available = model != null && model.exists();
        lock.setVisibility(VISIBLE);
        password.setVisibility(available ? VISIBLE : GONE);
        unlock.setVisibility(available ? VISIBLE : GONE);
        unlock.setEnabled(true);
        feedback.setText(message != null ? message : available ? "Digite a senha da maquete."
                : "A maquete aparecerá aqui quando este celular se conectar ao PC pareado.");
    }

    private void unlock() {
        if (!unlock.isEnabled() || model == null || password.length() == 0) return;
        char[] code = new char[password.length()];
        password.getText().getChars(0, code.length, code, 0);
        password.setText("");
        unlock.setEnabled(false);
        android.view.inputmethod.InputMethodManager keyboard = activity.getSystemService(android.view.inputmethod.InputMethodManager.class);
        if (keyboard != null) keyboard.hideSoftInputFromWindow(password.getWindowToken(), 0);
        feedback.setText("Abrindo maquete…");
        HousePackage target = model;
        int generation = ++viewGeneration;
        worker.execute(() -> {
            try {
                byte[] page = target.unlock(code);
                activity.show(() -> {
                    if (generation == viewGeneration) {
                        unlockedPage = page;
                        refreshOnOpen = false;
                        open(page);
                    }
                    else Arrays.fill(page, (byte) 0);
                });
            } catch (Exception error) {
                activity.show(() -> {
                    if (generation == viewGeneration) showLocked("Senha incorreta ou arquivo da maquete inválido.");
                });
            } finally {
                Arrays.fill(code, '\0');
            }
        });
    }

    private void open(byte[] page) {
        if (getVisibility() != VISIBLE) {
            return;
        }

        try {
            WebView view = new WebView(getContext());
            browser = view;
            view.setBackgroundColor(0xff0e1927);
            WebSettings settings = view.getSettings();
            settings.setJavaScriptEnabled(true);
            settings.setAllowFileAccess(false);
            settings.setAllowContentAccess(false);
            settings.setSupportZoom(false);
            view.setWebViewClient(new WebViewClient() {
                @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                    return true;
                }

                @Override public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                    if (request.isForMainFrame() && PAGE.equals(request.getUrl().toString())) {
                        return new WebResourceResponse("text/html", "UTF-8", new ByteArrayInputStream(page));
                    }
                    return new WebResourceResponse("text/plain", "UTF-8", 404, "Not Found",
                            Collections.emptyMap(), new ByteArrayInputStream(new byte[0]));
                }

                @Override public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                    if (request.isForMainFrame()) post(() -> showFailure(view));
                }

                @Override public boolean onRenderProcessGone(WebView view, RenderProcessGoneDetail detail) {
                    showFailure(view);
                    return true;
                }
            });
            addView(view, new LayoutParams(-1, -1));
            view.loadUrl(PAGE);
            lock.setVisibility(GONE);
        } catch (RuntimeException unavailable) {
            showFailure(browser);
        }
    }

    void pause() { if (browser != null) browser.onPause(); }
    void resume() { if (browser != null) browser.onResume(); }

    private void showFailure(WebView view) {
        if (browser != view) return;
        close();
        showLocked("Não foi possível abrir a maquete. Tente novamente.");
    }

    void close() {
        releaseViewer();
        clearUnlockedPage();
    }

    private void clearUnlockedPage() {
        if (unlockedPage != null) Arrays.fill(unlockedPage, (byte) 0);
        unlockedPage = null;
        refreshOnOpen = false;
    }

    private void releaseViewer() {
        viewGeneration++;
        password.setText("");
        if (browser == null) return;
        WebView view = browser;
        browser = null;
        removeView(view);
        view.destroy();
    }
}
