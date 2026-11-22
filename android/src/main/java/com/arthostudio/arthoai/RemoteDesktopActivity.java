package com.arthostudio.arthoai;

import android.app.Activity;
import android.app.AlertDialog;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.Paint;
import android.graphics.RectF;
import android.os.Build;
import android.os.Bundle;
import android.os.SystemClock;
import android.text.InputFilter;
import android.text.InputType;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.atomic.AtomicBoolean;

public final class RemoteDesktopActivity extends Activity {
    private final ExecutorService stream = Executors.newSingleThreadExecutor();
    private final ExecutorService input = Executors.newSingleThreadExecutor();
    private final AtomicBoolean painting = new AtomicBoolean();
    private final AtomicBoolean moving = new AtomicBoolean();
    private final Object lifecycle = new Object();
    private final List<Button> controls = new ArrayList<>();
    private volatile RemoteDesktopClient client;
    private volatile int generation;
    private boolean visible;
    private boolean pointerOnly;
    private TextView status;
    private Button connection;
    private Screen screen;

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        if (Build.VERSION.SDK_INT >= 30) getWindow().setDecorFitsSystemWindows(false);
        LinearLayout page = column();
        page.setBackgroundColor(0xff111b2c);
        page.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                Insets safe = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(safe.left + dp(9), safe.top + dp(7), safe.right + dp(9), safe.bottom + dp(7));
            } else view.setPadding(dp(9), dp(7), dp(9), dp(7));
            return insets;
        });
        LinearLayout bar = new LinearLayout(this);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        Button back = button("Voltar");
        back.setOnClickListener(view -> finish());
        bar.addView(back);
        TextView title = new TextView(this);
        title.setText("Tela remota");
        title.setTextSize(19);
        title.setTextColor(Color.WHITE);
        title.setPadding(dp(8), 0, 0, 0);
        title.setAccessibilityHeading(true);
        bar.addView(title, new LinearLayout.LayoutParams(0, -2, 1));
        connection = button("Conectar");
        connection.setOnClickListener(view -> {
            if (client == null) connect();
            else disconnect("Tela remota encerrada.");
        });
        bar.addView(connection);
        page.addView(bar);

        status = new TextView(this);
        status.setText("No Windows, abra Celular e libere o acesso à tela.");
        status.setTextColor(0xffbdcce0);
        status.setTextSize(13);
        status.setPadding(dp(3), dp(4), dp(3), dp(7));
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        page.addView(status);

        screen = new Screen();
        screen.setBackgroundColor(0xff050a13);
        screen.setContentDescription("Tela do Windows. Toque para clicar, segure para clique direito ou arraste.");
        page.addView(screen, new LinearLayout.LayoutParams(-1, 0, 1));

        LinearLayout mouse = new LinearLayout(this);
        Button mode = tool(mouse, "Modo ponteiro", () -> { });
        mode.setOnClickListener(view -> {
            pointerOnly = !pointerOnly;
            mode.setText(pointerOnly ? "Modo toque" : "Modo ponteiro");
            status.setText(pointerOnly ? "Deslize para mover o ponteiro sem clicar." : "Toque para clicar ou arraste para mover um item.");
        });
        tool(mouse, "Clique direito", () -> send("right", screen.pointerX, screen.pointerY, null, 0));
        tool(mouse, "Teclado", this::keyboard);
        page.addView(mouse);

        LinearLayout scroll = new LinearLayout(this);
        tool(scroll, "Rolar para cima", () -> send("scroll", 0, 0, null, 2));
        tool(scroll, "Rolar para baixo", () -> send("scroll", 0, 0, null, -2));
        page.addView(scroll);
        setContentView(page);
        page.requestApplyInsets();
        ready(false);
    }

    @Override protected void onStart() {
        super.onStart();
        visible = true;
        connect();
    }

    @Override protected void onStop() {
        visible = false;
        disconnect("Tela remota encerrada.");
        super.onStop();
    }

    @Override protected void onDestroy() {
        stream.shutdownNow();
        input.shutdown();
        super.onDestroy();
    }

    private void connect() {
        int attempt;
        synchronized (lifecycle) { attempt = ++generation; }
        ready(false);
        connection.setEnabled(false);
        status.setText("Conectando ao PC…");
        stream.execute(() -> {
            RemoteDesktopClient session = null;
            try {
                StatusClient.Pairing pairing = PairingVault.load(getApplicationContext());
                if (pairing == null) throw new IllegalStateException("Volte ao início e pareie o celular com o PC.");
                session = RemoteDesktopClient.connect(pairing);
                synchronized (lifecycle) {
                    if (generation != attempt) return;
                    client = session;
                }
                while (attempt == generation && !Thread.currentThread().isInterrupted()) {
                    long started = SystemClock.elapsedRealtime();
                    if (painting.compareAndSet(false, true)) {
                        RemoteDesktopClient.Frame frame;
                        try { frame = session.frame(); }
                        catch (Exception error) { painting.set(false); throw error; }
                        runOnUiThread(() -> {
                            painting.set(false);
                            if (attempt != generation || !visible) return;
                            if (screen.frame == null) {
                                ready(true);
                                status.setText("Arraste ou toque na tela. Gire o celular para ampliar a imagem.");
                            }
                            screen.frame = frame;
                            screen.invalidate();
                        });
                    }
                    Thread.sleep(Math.max(1, 125 - (SystemClock.elapsedRealtime() - started)));
                }
            } catch (Exception error) {
                String message = error instanceof SecurityException ? "Vínculo removido. Pareie novamente."
                        : error.getMessage();
                runOnUiThread(() -> {
                    if (attempt == generation && visible)
                        disconnect(message == null ? "Não foi possível acessar a tela." : message);
                });
            } finally {
                if (session != null) session.disconnect();
            }
        });
    }

    private void disconnect(String message) {
        RemoteDesktopClient previous;
        synchronized (lifecycle) {
            generation++;
            previous = client;
            client = null;
        }
        if (previous != null) input.execute(previous::disconnect);
        screen.clear();
        ready(false);
        status.setText(message);
    }

    private void ready(boolean active) {
        connection.setEnabled(true);
        connection.setText(active ? "Encerrar" : "Conectar");
        for (Button control : controls) control.setEnabled(active);
    }

    private void send(String action, int x, int y, String text, int delta) {
        RemoteDesktopClient current = client;
        if (current == null) return;
        boolean move = action.equals("move");
        if (move && !moving.compareAndSet(false, true)) return;
        try {
            input.execute(() -> {
                try {
                    if (client == current) current.input(action, x, y, text, delta);
                } catch (Exception error) {
                    runOnUiThread(() -> {
                        if (client == current && visible) status.setText("O PC não aceitou o comando. Confira a sessão do Windows.");
                    });
                } finally {
                    if (move) moving.set(false);
                }
            });
        } catch (RejectedExecutionException stopped) {
            if (move) moving.set(false);
        }
    }

    private void keyboard() {
        LinearLayout fields = column();
        fields.setPadding(dp(16), 0, dp(16), 0);
        EditText text = new EditText(this);
        text.setSingleLine(true);
        text.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        text.setImeOptions(android.view.inputmethod.EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING);
        text.setFilters(new InputFilter[]{new InputFilter.LengthFilter(256)});
        text.setHint("Texto para digitar no PC");
        fields.addView(text);
        LinearLayout keys = new LinearLayout(this);
        fields.addView(keys);
        for (String key : new String[]{"Tab", "Escape", "Backspace", "Enter"}) {
            Button button = button(key.equals("Backspace") ? "Apagar" : key.equals("Escape") ? "Esc" : key);
            button.setOnClickListener(view -> send("key", 0, 0, key, 0));
            keys.addView(button, new LinearLayout.LayoutParams(0, -2, 1));
        }
        new AlertDialog.Builder(this).setTitle("Teclado do PC").setView(fields)
                .setPositiveButton("Enviar texto", (dialog, which) -> {
                    String value = text.getText().toString();
                    if (!value.isEmpty()) send("text", 0, 0, value, 0);
                }).setNegativeButton("Fechar", null).show();
    }

    private LinearLayout column() {
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        return layout;
    }

    private Button button(String label) {
        Button button = new Button(this);
        button.setAllCaps(false);
        button.setText(label);
        button.setTextSize(13);
        button.setMinHeight(dp(43));
        button.setPadding(dp(5), dp(4), dp(5), dp(4));
        return button;
    }

    private Button tool(LinearLayout row, String label, Runnable run) {
        Button button = button(label);
        button.setOnClickListener(view -> run.run());
        row.addView(button, new LinearLayout.LayoutParams(0, -2, 1));
        controls.add(button);
        return button;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    private final class Screen extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
        private final RectF fit = new RectF();
        private final int slop = ViewConfiguration.get(RemoteDesktopActivity.this).getScaledTouchSlop();
        RemoteDesktopClient.Frame frame;
        int pointerX = 32768, pointerY = 32768;
        private float startX, startY;
        private long touchAt;
        private boolean down, dragging;

        Screen() { super(RemoteDesktopActivity.this); setClickable(true); }

        @Override protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            if (frame == null) return;
            float scale = Math.min((float) getWidth() / frame.image.getWidth(), (float) getHeight() / frame.image.getHeight());
            float width = frame.image.getWidth() * scale, height = frame.image.getHeight() * scale;
            float left = (getWidth() - width) / 2, top = (getHeight() - height) / 2;
            fit.set(left, top, left + width, top + height);
            canvas.drawBitmap(frame.image, null, fit, paint);
            if (frame.x >= 0 && frame.x <= 65535 && frame.y >= 0 && frame.y <= 65535) {
                float x = fit.left + fit.width() * frame.x / 65535f;
                float y = fit.top + fit.height() * frame.y / 65535f;
                paint.setStyle(Paint.Style.STROKE);
                paint.setStrokeWidth(dp(2));
                paint.setColor(Color.BLACK);
                canvas.drawCircle(x, y, dp(5), paint);
                paint.setColor(Color.WHITE);
                canvas.drawCircle(x, y, dp(4), paint);
                paint.setStyle(Paint.Style.FILL);
            }
        }

        @Override public boolean onTouchEvent(MotionEvent event) {
            if (frame == null || fit.isEmpty() || client == null) return false;
            float x = event.getX(), y = event.getY();
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    if (!fit.contains(x, y)) return false;
                    down = true;
                    dragging = false;
                    startX = x;
                    startY = y;
                    touchAt = SystemClock.elapsedRealtime();
                    if (pointerOnly) point("move", x, y);
                    return true;
                case MotionEvent.ACTION_MOVE:
                    if (!down) return false;
                    if (!pointerOnly && !dragging && Math.hypot(x - startX, y - startY) > slop) {
                        dragging = true;
                        point("down", startX, startY);
                    }
                    if (pointerOnly || dragging) point("move", x, y);
                    return true;
                case MotionEvent.ACTION_UP:
                    if (!down) return false;
                    if (dragging) point("up", x, y);
                    else if (pointerOnly) point("move", x, y);
                    else {
                        point(SystemClock.elapsedRealtime() - touchAt > 500 ? "right" : "click", x, y);
                        performClick();
                    }
                    down = dragging = false;
                    return true;
                case MotionEvent.ACTION_CANCEL:
                case MotionEvent.ACTION_POINTER_DOWN:
                    if (dragging) send("up", pointerX, pointerY, null, 0);
                    down = dragging = false;
                    return true;
                default: return down;
            }
        }

        @Override public boolean performClick() { return super.performClick(); }

        private void point(String action, float x, float y) {
            pointerX = normalized(x, fit.left, fit.width());
            pointerY = normalized(y, fit.top, fit.height());
            send(action, pointerX, pointerY, null, 0);
        }

        private int normalized(float value, float origin, float span) {
            return Math.round(65535 * Math.max(0f, Math.min(1f, (value - origin) / span)));
        }

        void clear() {
            frame = null;
            down = dragging = false;
            invalidate();
        }
    }
}
