package com.arthostudio.arthoai;

import android.animation.ValueAnimator;
import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.RadialGradient;
import android.graphics.Rect;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;
import android.os.SystemClock;

final class GlassBackdrop extends Drawable {
    private final Paint paint = new Paint();
    private Shader base;
    private Shader glow;
    private final Matrix glowPosition = new Matrix();
    private boolean running;
    private long startedAt;
    private final Runnable advance = new Runnable() {
        @Override public void run() {
            if (!running) return;
            invalidateSelf();
            scheduleSelf(this, SystemClock.uptimeMillis() + 45);
        }
    };

    void setRunning(boolean visible) {
        boolean enabled = visible && ValueAnimator.areAnimatorsEnabled();
        if (running == enabled) return;
        running = enabled;
        unscheduleSelf(advance);
        if (enabled) {
            startedAt = SystemClock.uptimeMillis();
            advance.run();
        }
    }

    @Override protected void onBoundsChange(Rect bounds) {
        float width = Math.max(1, bounds.width());
        float height = Math.max(1, bounds.height());
        base = new LinearGradient(bounds.left, bounds.top, bounds.right, bounds.bottom,
                new int[]{0xff0d1726, 0xff0a111e, 0xff090f1a}, null, Shader.TileMode.CLAMP);
        glow = new RadialGradient(bounds.left + width * .76f, bounds.top + height * .15f, height * .62f,
                new int[]{0x632d5681, 0x22244367, 0x00244367}, null, Shader.TileMode.CLAMP);
    }

    @Override public void draw(Canvas canvas) {
        paint.setShader(base);
        canvas.drawRect(getBounds(), paint);
        if (glow != null && running) {
            float phase = (SystemClock.uptimeMillis() - startedAt) / 32000f * (float) (2 * Math.PI);
            float offset = (1 - (float) Math.cos(phase)) * .5f;
            glowPosition.setTranslate(-getBounds().width() * .31f * offset, getBounds().height() * .16f * offset);
            glow.setLocalMatrix(glowPosition);
        }
        paint.setShader(glow);
        canvas.drawRect(getBounds(), paint);
    }

    @Override public void setAlpha(int alpha) { paint.setAlpha(alpha); invalidateSelf(); }
    @Override public void setColorFilter(ColorFilter filter) { paint.setColorFilter(filter); invalidateSelf(); }
    @Override public int getOpacity() { return PixelFormat.OPAQUE; }
}
