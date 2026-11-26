package com.arthostudio.arthoai;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.content.pm.ActivityInfo;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
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
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.net.ssl.SSLHandshakeException;

public final class RemoteDesktopActivity extends Activity {
    private static final int PANEL = MainActivity.SURFACE;
    private static final int TEXT = MainActivity.INK;
    private static final int MUTED = MainActivity.MUTED;
    private final ExecutorService stream = Executors.newSingleThreadExecutor();
    private final ExecutorService input = Executors.newSingleThreadExecutor();
    private final AtomicBoolean painting = new AtomicBoolean();
    private final Object lifecycle = new Object();
    private final List<Button> controls = new ArrayList<>();
    private volatile RemoteDesktopClient client;
    private volatile int generation;
    private volatile int inputEpoch;
    private volatile boolean detailMode;
    private boolean visible, streamRequested, interactive;
    private PointerMove queuedMove;
    private TextView status;
    private Button connection;
    private Button orientation;
    private Screen screen;
    private LinearLayout workspace, controller;
    private Joystick joystick;

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        if (Build.VERSION.SDK_INT >= 30) getWindow().setDecorFitsSystemWindows(false);
        LinearLayout page = column();
        getWindow().setStatusBarColor(MainActivity.BACKGROUND);
        getWindow().setNavigationBarColor(MainActivity.BACKGROUND);
        page.setBackgroundColor(MainActivity.BACKGROUND);
        page.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                Insets safe = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(safe.left + dp(12), safe.top + dp(6), safe.right + dp(12), safe.bottom + dp(6));
            } else view.setPadding(dp(12), dp(6), dp(12), dp(6));
            return insets;
        });
        LinearLayout bar = new LinearLayout(this);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        Button back = button("‹");
        back.setContentDescription("Voltar");
        back.setOnClickListener(view -> finish());
        bar.addView(back);
        TextView title = new TextView(this);
        title.setText("Tela remota");
        title.setTextSize(20);
        title.setTextColor(TEXT);
        title.setPadding(dp(12), 0, 0, 0);
        title.setAccessibilityHeading(true);
        bar.addView(title, new LinearLayout.LayoutParams(0, -2, 1));
        orientation = button("Horizontal");
        orientation.setOnClickListener(view -> setRequestedOrientation(
                getResources().getConfiguration().orientation == Configuration.ORIENTATION_LANDSCAPE
                        ? ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
                        : ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE));
        LinearLayout.LayoutParams rotationButton = new LinearLayout.LayoutParams(-2, -2);
        rotationButton.rightMargin = dp(6);
        bar.addView(orientation, rotationButton);
        connection = button("Conectar");
        connection.setOnClickListener(view -> {
            if (streamRequested) disconnect("Tela remota encerrada.");
            else connect();
        });
        bar.addView(connection);
        page.addView(bar);

        status = new TextView(this);
        status.setText("Conecte-se ao PC pareado.");
        status.setTextColor(MUTED);
        status.setTextSize(13);
        status.setPadding(dp(3), dp(4), dp(3), dp(7));
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        LinearLayout streamStatus = new LinearLayout(this);
        streamStatus.setGravity(Gravity.CENTER_VERTICAL);
        streamStatus.addView(status, new LinearLayout.LayoutParams(0, -2, 1));

        Button quality = button("Fluidez");
        quality.setContentDescription("Qualidade da tela remota. Fluidez ativada.");
        quality.setOnClickListener(view -> {
            detailMode = !detailMode;
            quality.setText(detailMode ? "Nitidez" : "Fluidez");
            quality.setContentDescription(detailMode ? "Qualidade da tela remota. Nitidez ativada."
                    : "Qualidade da tela remota. Fluidez ativada.");
        });
        streamStatus.addView(quality);
        page.addView(streamStatus);

        workspace = new LinearLayout(this);
        page.addView(workspace, new LinearLayout.LayoutParams(-1, 0, 1));
        screen = new Screen();
        screen.setBackground(round(0xff070c14, 12, MainActivity.BORDER));
        screen.setClipToOutline(true);
        screen.setContentDescription("Tela do Windows. Toque para clicar, segure para clique direito ou arraste.");
        workspace.addView(screen);
        controller = new LinearLayout(this);
        controller.setGravity(Gravity.CENTER_VERTICAL);
        controller.setPadding(dp(6), dp(6), dp(6), dp(6));
        controller.setBackground(round(PANEL, 15, MainActivity.BORDER));
        joystick = new Joystick();
        controller.addView(joystick, new LinearLayout.LayoutParams(dp(108), dp(117)));
        LinearLayout actions = column();
        controller.addView(actions, new LinearLayout.LayoutParams(0, -2, 1));
        LinearLayout clicks = new LinearLayout(this);
        actions.addView(clicks);
        tool(clicks, "Clique", () -> screen.click("click"));
        tool(clicks, "Direito", () -> screen.click("right"))
                .setContentDescription("Clique direito");
        LinearLayout scroll = new LinearLayout(this);
        actions.addView(scroll);
        tool(scroll, "Rolar ↑", () -> send("scroll", 0, 0, null, 2)).setContentDescription("Rolar para cima");
        tool(scroll, "Rolar ↓", () -> send("scroll", 0, 0, null, -2)).setContentDescription("Rolar para baixo");
        LinearLayout typing = new LinearLayout(this);
        actions.addView(typing);
        tool(typing, "Teclado", this::keyboard);
        workspace.addView(controller);
        layoutController(getResources().getConfiguration());
        setContentView(page);
        page.requestApplyInsets();
        ready(false);
    }

    @Override public void onConfigurationChanged(Configuration configuration) {
        super.onConfigurationChanged(configuration);
        joystick.stop();
        screen.cancelGesture();
        layoutController(configuration);
    }

    private void layoutController(Configuration configuration) {
        boolean wide = configuration.orientation == Configuration.ORIENTATION_LANDSCAPE;
        orientation.setText(wide ? "Vertical" : "Horizontal");
        workspace.setOrientation(wide ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        workspace.setGravity(Gravity.CENTER_VERTICAL);
        screen.setLayoutParams(wide ? new LinearLayout.LayoutParams(0, -1, 1) : new LinearLayout.LayoutParams(-1, 0, 1));
        LinearLayout.LayoutParams panel = new LinearLayout.LayoutParams(wide ? dp(256) : -1, -2);
        panel.setMargins(wide ? dp(8) : 0, wide ? 0 : dp(8), 0, 0);
        controller.setLayoutParams(panel);
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
        streamRequested = true;
        connection.setText("Encerrar");
        ready(false);
        status.setText("Conectando ao PC…");
        stream.execute(() -> {
            RemoteDesktopClient session = null;
            try {
                StatusClient.Pairing pairing = PairingVault.load(getApplicationContext());
                if (pairing == null) throw new IllegalStateException("Volte ao início e pareie o celular com o PC.");
                int failures = 0;
                RemoteFramePacing pacing = new RemoteFramePacing();
                while (attempt == generation && !Thread.currentThread().isInterrupted()) {
                    try {
                        if (session == null) {
                            session = RemoteDesktopClient.connect(pairing);
                            synchronized (lifecycle) {
                                if (generation != attempt) return;
                                client = session;
                            }
                        }
                        if (!painting.compareAndSet(false, true)) {
                            Thread.sleep(8);
                            continue;
                        }
                        RemoteFramePacing.Quality quality = detailMode ? RemoteFramePacing.Quality.DETAIL : pacing.quality();
                        long started = SystemClock.elapsedRealtime();
                        RemoteDesktopClient.Frame frame;
                        try { frame = session.frame(quality); }
                        catch (Exception error) { painting.set(false); throw error; }
                        long duration = SystemClock.elapsedRealtime() - started;
                        if (quality != RemoteFramePacing.Quality.DETAIL) pacing.received(duration);
                        failures = 0;
                        runOnUiThread(() -> displayFrame(attempt, frame));
                        Thread.sleep(Math.max(1, quality.intervalMs - duration));
                    } catch (SSLHandshakeException error) {
                        throw new SecurityException("Confira o pareamento no Windows.", error);
                    } catch (IOException error) {
                        if (!detailMode) pacing.reduce();
                        boolean expired = error instanceof RemoteDesktopClient.SessionExpired;
                        boolean first = ++failures == 1;
                        if (first || expired) runOnUiThread(() -> connectionInterrupted(attempt, expired));
                        if (expired && session != null) {
                            synchronized (lifecycle) {
                                if (client == session) client = null;
                            }
                            session.disconnect();
                            session = null;
                        }
                        Thread.sleep(Math.min(4000, 300L << Math.min(failures - 1, 4)));
                    }
                }
            } catch (InterruptedException stopped) {
                Thread.currentThread().interrupt();
            } catch (Exception error) {
                String message = error instanceof SecurityException && error.getMessage() == null
                        ? "Vínculo removido. Pareie novamente." : error.getMessage();
                runOnUiThread(() -> {
                    if (attempt == generation && visible)
                        disconnect(message == null ? "Não foi possível acessar a tela." : message);
                });
            } finally {
                if (session != null) session.disconnect();
            }
        });
    }

    private void displayFrame(int attempt, RemoteDesktopClient.Frame frame) {
        painting.set(false);
        if (attempt != generation || !visible) return;
        screen.show(frame);
        if (!interactive) ready(true);
        String detail = frame.monitorCount > 1
                ? "Monitor " + frame.monitorNumber + " de " + frame.monitorCount + " · Mova o ponteiro até a borda para trocar."
                : "Use o joystick ou toque na tela para controlar o PC.";
        if (!detail.contentEquals(status.getText())) status.setText(detail);
    }

    private void connectionInterrupted(int attempt, boolean expired) {
        if (attempt != generation || !visible) return;
        inputEpoch++;
        joystick.stop();
        screen.cancelGesture();
        if (expired) screen.clear();
        ready(false);
        status.setText("Reconectando… Confira a rede e se o Windows está desbloqueado.");
    }

    private void disconnect(String message) {
        RemoteDesktopClient previous;
        synchronized (lifecycle) {
            generation++;
            previous = client;
            client = null;
            queuedMove = null;
        }
        streamRequested = false;
        inputEpoch++;
        connection.setText("Conectar");
        if (previous != null) input.execute(previous::disconnect);
        joystick.stop();
        screen.clear();
        ready(false);
        status.setText(message);
    }

    private void ready(boolean active) {
        interactive = active;
        joystick.setEnabled(active);
        joystick.invalidate();
        for (Button control : controls) control.setEnabled(active);
    }

    private void send(String action, int x, int y, String text, int delta) {
        send(action, x, y, text, delta, null, 0);
    }

    private void send(String action, int x, int y, String text, int delta, String monitor, int edge) {
        RemoteDesktopClient current = client;
        if (current == null) return;
        int epoch = inputEpoch;
        if (action.equals("move")) {
            synchronized (lifecycle) {
                if (queuedMove != null && queuedMove.client == current && queuedMove.epoch == epoch &&
                        Objects.equals(queuedMove.monitor, monitor)) {
                    queuedMove.x = x;
                    queuedMove.y = y;
                    queuedMove.edge = edge;
                    return;
                }
                PointerMove move = new PointerMove(current, x, y, monitor, edge, epoch);
                queuedMove = move;
                input.execute(() -> {
                    synchronized (lifecycle) {
                        if (queuedMove == move) queuedMove = null;
                    }
                    transmit(move.client, "move", move.x, move.y, null, 0, move.monitor, move.edge, move.epoch);
                });
            }
            return;
        }
        // Keep pointer updates on the correct side of button presses and releases.
        synchronized (lifecycle) { queuedMove = null; }
        try {
            input.execute(() -> transmit(current, action, x, y, text, delta, monitor, edge, epoch));
        } catch (RejectedExecutionException stopped) { }
    }

    private void transmit(RemoteDesktopClient target, String action, int x, int y, String text, int delta,
                          String monitor, int edge, int epoch) {
        try {
            if (client == target && inputEpoch == epoch) target.input(action, x, y, text, delta, monitor, edge);
        } catch (Exception ignored) { } // A failed input is never replayed when the screen resumes.
    }

    private static final class PointerMove {
        final RemoteDesktopClient client;
        final String monitor;
        final int epoch;
        int x, y, edge;
        PointerMove(RemoteDesktopClient client, int x, int y, String monitor, int edge, int epoch) {
            this.client = client;
            this.x = x;
            this.y = y;
            this.monitor = monitor;
            this.edge = edge;
            this.epoch = epoch;
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
        button.setTextColor(new ColorStateList(new int[][]{new int[]{-android.R.attr.state_enabled}, new int[0]},
                new int[]{0xff8191a4, TEXT}));
        button.setMinimumWidth(dp(44));
        button.setMinHeight(dp(44));
        button.setPadding(dp(6), 0, dp(6), 0);
        button.setBackground(new RippleDrawable(ColorStateList.valueOf(0x3590e9cf),
                round(0xff15232d, 9, 0xff3b5161), null));
        return button;
    }

    private Button tool(LinearLayout row, String label, Runnable run) {
        Button button = button(label);
        button.setOnClickListener(view -> run.run());
        LinearLayout.LayoutParams item = new LinearLayout.LayoutParams(0, dp(44), 1);
        item.setMargins(dp(3), dp(2), dp(3), dp(2));
        row.addView(button, item);
        controls.add(button);
        return button;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    private GradientDrawable round(int color, int radius, int edge) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(color);
        shape.setCornerRadius(dp(radius));
        shape.setStroke(dp(1), edge);
        return shape;
    }

    private final class Joystick extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Runnable step = this::advance;
        private int finger = -1;
        private float axisX, axisY;
        private long lastStep;

        Joystick() {
            super(RemoteDesktopActivity.this);
            setContentDescription("Joystick do mouse. Mantenha o dedo inclinado para mover o ponteiro.");
        }

        private float centerY() { return (getHeight() - dp(16)) / 2f; }
        private float radius() { return Math.min(getWidth() / 2f - dp(6), centerY() - dp(3)); }

        @Override protected void onDraw(Canvas canvas) {
            float x = getWidth() / 2f, y = centerY(), ring = radius();
            if (ring <= 0) return;
            paint.setColor(0xff1c323b);
            canvas.drawCircle(x, y, ring, paint);
            paint.setColor(0xff101a23);
            canvas.drawCircle(x, y, ring - dp(2), paint);
            paint.setColor(0xff426774);
            for (int dir = 0; dir < 4; dir++) {
                double angle = dir * Math.PI / 2;
                canvas.drawCircle(x + (float) Math.cos(angle) * (ring - dp(6)),
                        y + (float) Math.sin(angle) * (ring - dp(6)), dp(1), paint);
            }
            float knobX = x + axisX * ring * .55f, knobY = y + axisY * ring * .55f;
            paint.setColor(finger != -1 ? MainActivity.ACCENT : 0xff567a96);
            canvas.drawCircle(knobX, knobY, ring * .39f, paint);
            paint.setColor(finger != -1 ? 0xffdef2f3 : 0xffa9c3d6);
            canvas.drawCircle(knobX, knobY, dp(3), paint);
            paint.setColor(MUTED);
            paint.setTextSize(dp(11));
            paint.setTextAlign(Paint.Align.CENTER);
            canvas.drawText("Joystick", x, getHeight() - dp(1), paint);
        }

        @Override public boolean onTouchEvent(MotionEvent event) {
            if (!isEnabled() || radius() <= 0) return false;
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    finger = event.getPointerId(0);
                    steer(event.getX(), event.getY());
                    lastStep = SystemClock.uptimeMillis();
                    postOnAnimation(step);
                    return true;
                case MotionEvent.ACTION_MOVE:
                    int index = event.findPointerIndex(finger);
                    if (index >= 0) steer(event.getX(index), event.getY(index));
                    return true;
                case MotionEvent.ACTION_POINTER_UP:
                    if (event.getPointerId(event.getActionIndex()) == finger) stop();
                    return true;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    stop();
                    return true;
                default: return finger != -1;
            }
        }

        private void steer(float x, float y) {
            float reach = radius() * .73f;
            float dx = (x - getWidth() / 2f) / reach, dy = (y - centerY()) / reach;
            float scale = Math.max(1f, (float) Math.hypot(dx, dy));
            axisX = dx / scale;
            axisY = dy / scale;
            invalidate();
        }

        private void advance() {
            if (finger == -1 || !isEnabled()) return;
            long now = SystemClock.uptimeMillis();
            float seconds = Math.min(34, now - lastStep) / 1000f;
            lastStep = now;
            float strength = (float) Math.hypot(axisX, axisY);
            if (strength > .13f) {
                float amount = (strength - .13f) / .87f;
                float distance = (0.04f + .95f * amount * amount) * seconds;
                screen.move(axisX / strength * distance, axisY / strength * distance);
            }
            postOnAnimation(step);
        }

        void stop() {
            finger = -1;
            axisX = axisY = 0f;
            removeCallbacks(step);
            invalidate();
        }
    }

    private final class Screen extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
        private final RectF fit = new RectF();
        private final Path cursor = new Path();
        private final int slop = ViewConfiguration.get(RemoteDesktopActivity.this).getScaledTouchSlop();
        RemoteDesktopClient.Frame frame;
        private RemotePointer pointer = new RemotePointer();
        private float startX, startY, lastTouchX, lastTouchY;
        private long touchAt, lastEdgeAt;
        private int lastEdge;
        private boolean down, dragging, relativeDrag;

        Screen() { super(RemoteDesktopActivity.this); setClickable(true); }

        @Override protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            if (frame == null) return;
            float scale = Math.min((float) getWidth() / frame.image.getWidth(), (float) getHeight() / frame.image.getHeight());
            float width = frame.image.getWidth() * scale, height = frame.image.getHeight() * scale;
            float left = (getWidth() - width) / 2, top = (getHeight() - height) / 2;
            fit.set(left, top, left + width, top + height);
            canvas.drawBitmap(frame.image, null, fit, paint);
            if (pointer.visible()) {
                float x = fit.left + fit.width() * pointer.x() / RemotePointer.MAX;
                float y = fit.top + fit.height() * pointer.y() / RemotePointer.MAX;
                cursor.reset();
                cursor.moveTo(x, y);
                cursor.lineTo(x, y + dp(17));
                cursor.lineTo(x + dp(5), y + dp(12));
                cursor.lineTo(x + dp(12), y + dp(12));
                cursor.close();
                paint.setStyle(Paint.Style.STROKE);
                paint.setStrokeWidth(dp(2));
                paint.setColor(Color.BLACK);
                canvas.drawPath(cursor, paint);
                paint.setStyle(Paint.Style.FILL);
                paint.setColor(Color.WHITE);
                canvas.drawPath(cursor, paint);
            }
        }

        void show(RemoteDesktopClient.Frame next) {
            if (frame != null && !Objects.equals(frame.monitor, next.monitor)) {
                pointer = new RemotePointer();
                pointer.observe(next.x, next.y);
                relativeDrag = dragging;
                lastEdge = 0;
            }
            frame = next;
            // Frames can arrive after newer mouse commands. Only use their cursor before local input.
            if (!down && joystick.finger == -1) pointer.observe(next.x, next.y);
            invalidate();
        }

        void move(float x, float y) {
            if (frame == null || down) return;
            float aspect = (float) frame.image.getWidth() / frame.image.getHeight();
            motion(pointer.advance(x, y, aspect), x, y);
        }

        void click(String action) {
            pointer.set(pointer.x(), pointer.y());
            dispatch(action);
        }

        @Override public boolean onTouchEvent(MotionEvent event) {
            if (frame == null || fit.isEmpty() || !interactive) return false;
            float x = event.getX(), y = event.getY();
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    if (!fit.contains(x, y)) return false;
                    down = true;
                    dragging = false;
                    relativeDrag = false;
                    startX = lastTouchX = x;
                    startY = lastTouchY = y;
                    touchAt = SystemClock.elapsedRealtime();
                    return true;
                case MotionEvent.ACTION_MOVE:
                    if (!down) return false;
                    if (!dragging && Math.hypot(x - startX, y - startY) > slop) {
                        dragging = true;
                        point("down", startX, startY);
                    }
                    float dx = x - lastTouchX, dy = y - lastTouchY;
                    lastTouchX = x;
                    lastTouchY = y;
                    if (dragging && relativeDrag)
                        motion(pointer.advance(dx / fit.width(), dy / fit.height(), 1), dx, dy);
                    else if (dragging) {
                        float edge = dp(4);
                        float atX = dx < 0 && x <= fit.left + edge ? fit.left
                                : dx > 0 && x >= fit.right - edge ? fit.right : x;
                        float atY = dy < 0 && y <= fit.top + edge ? fit.top
                                : dy > 0 && y >= fit.bottom - edge ? fit.bottom : y;
                        pointer.set(normalized(atX, fit.left, fit.width()), normalized(atY, fit.top, fit.height()));
                        motion(true, dx, dy);
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    if (!down) return false;
                    if (dragging) dispatch("up");
                    else {
                        point(SystemClock.elapsedRealtime() - touchAt > 500 ? "right" : "click", x, y);
                        performClick();
                    }
                    down = dragging = false;
                    return true;
                case MotionEvent.ACTION_CANCEL:
                case MotionEvent.ACTION_POINTER_DOWN:
                    finishGesture();
                    return true;
                default: return down;
            }
        }

        @Override public boolean performClick() { return super.performClick(); }

        private void point(String action, float x, float y) {
            pointer.set(normalized(x, fit.left, fit.width()), normalized(y, fit.top, fit.height()));
            dispatch(action);
        }

        private void dispatch(String action) {
            dispatch(action, 0);
        }

        private void motion(boolean moved, float dx, float dy) {
            int edge = frame.monitorCount > 1 ? pointer.edge(dx, dy) : 0;
            long now = SystemClock.uptimeMillis();
            if (moved || (edge != 0 && (lastEdge != edge || now - lastEdgeAt >= 140))) {
                lastEdge = edge;
                lastEdgeAt = now;
                dispatch("move", edge);
            }
        }

        private void dispatch(String action, int edge) {
            invalidate();
            if (frame != null) send(action, pointer.x(), pointer.y(), null, 0, frame.monitor, edge);
        }

        private int normalized(float value, float origin, float span) {
            return Math.round(RemotePointer.MAX * Math.max(0f, Math.min(1f, (value - origin) / span)));
        }

        void cancelGesture() {
            finishGesture();
            fit.setEmpty();
        }

        private void finishGesture() {
            if (dragging) dispatch("up");
            down = dragging = false;
        }

        void clear() {
            frame = null;
            fit.setEmpty();
            down = dragging = false;
            pointer = new RemotePointer();
            lastEdge = 0;
            invalidate();
        }
    }
}
