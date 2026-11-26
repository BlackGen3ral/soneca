package com.arthostudio.arthoai;

import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;

final class GlassBackdrop extends Drawable {
    private final Paint paint = new Paint();

    @Override protected void onBoundsChange(Rect bounds) {
        paint.setShader(new LinearGradient(bounds.left, bounds.top, bounds.right,
                Math.max(bounds.bottom, bounds.top + 1),
                0xff102229, MainActivity.BACKGROUND, Shader.TileMode.CLAMP));
    }

    @Override public void draw(Canvas canvas) { canvas.drawRect(getBounds(), paint); }

    @Override public void setAlpha(int alpha) { paint.setAlpha(alpha); invalidateSelf(); }
    @Override public void setColorFilter(ColorFilter filter) { paint.setColorFilter(filter); invalidateSelf(); }
    @Override public int getOpacity() { return PixelFormat.OPAQUE; }
}
